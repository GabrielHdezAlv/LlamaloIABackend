using System.Net.WebSockets;
using System.Text;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using ProyectoLlamaloIA.Backend.Decodificator;
using ProyectoLlamaloIA.Backend.Models;
using ProyectoLlamaloIA.Backend.Services;
using Twilio.AspNet.Core;
using Twilio.TwiML;
using Twilio.TwiML.Voice;
using Twilio.Types;

namespace ProyectoLlamaloIA.Backend.Controllers;

[Route("voice")]
[ApiController]
public class VoiceController : ControllerBase
{
    private readonly FirestoreDb _firestore;
    private readonly FirebaseService _firebaseService;
    private readonly DeepgramService _deepgramService;
    private readonly GPTService _gptService;
    private readonly ElevenLabsService _elevenService;
    private readonly ILogger<VoiceController> _logger;
    private readonly HttpClient  _httpClient;
    
    public VoiceController(
        FirestoreDb firestore,
        FirebaseService firebaseService,
        DeepgramService deepgramService,
        GPTService gptService,
        ElevenLabsService elevenService,
        ILogger<VoiceController> logger,
        HttpClient httpClient)
    {
        _firestore = firestore;
        _firebaseService = firebaseService;
        _deepgramService = deepgramService;
        _gptService = gptService;
        _elevenService = elevenService;
        _logger = logger;
        _httpClient = httpClient;
    }
    
    private static string NormalizeNumber(string number) =>
        number?.Replace("+", "").Replace(" ", "").Replace("-", "").Trim() ?? "";
    
    private static DocumentSnapshot? FindAgentByPhone(IEnumerable<DocumentSnapshot> docs, string targetNumber)
    {
        var normalizedTarget = NormalizeNumber(targetNumber);

        foreach (var doc in docs)
        {
            if (doc.TryGetValue<Dictionary<string, object>>("twilio", out var twilioData))
            {
                if (twilioData.TryGetValue("countryCode", out var countryCodeObj) &&
                    twilioData.TryGetValue("phoneNumber", out var phoneNumberObj))
                {
                    var fullNumber = $"{countryCodeObj}{phoneNumberObj}";
                    var normalizedFullNumber = NormalizeNumber(fullNumber);

                    if (normalizedFullNumber == normalizedTarget)
                    {
                        return doc;
                    }
                }
            }
        }

        return null;
    }
    
    [HttpPost("incoming")]
    public async Task<IActionResult> IncomingCall([FromForm] string To)
    {
        _logger.LogInformation("📞 Llamada entrante detectada. Número marcado: {To}", To);

        var response = new VoiceResponse();

        // Buscar agente en Firestore
        var agentsRef = _firestore.Collection("users");
        var snapshot = await agentsRef.GetSnapshotAsync();
        var matchingDoc = FindAgentByPhone(snapshot.Documents, To);

        if (matchingDoc == null)
        {
            response.Say("El número de agente no está configurado.", voice: "alice");
            return Content(response.ToString(), "application/xml");
        }

        var agent = matchingDoc.ConvertTo<AgentConfig>();
        var firstInteractionUrl = agent.Twilio["firstInteractionUrl"].ToString();

        // 1️⃣ Reproducir audio de bienvenida
        response.Play(new Uri(firstInteractionUrl));

        // 2️⃣ Iniciar streaming hacia MediaStreamController
        var start = new Start();
        start.Stream(url: "wss://llamaloiabackend.onrender.com/voice/stream");
        response.Append(start);

        // 3️⃣ Redirigir a record para escuchar al usuario
        response.Redirect(new Uri($"/voice/record?To={Uri.EscapeDataString(To)}", UriKind.Relative));

        return Content(response.ToString(), "application/xml");
    }
    
    // Paso 2: grabar al usuario
    [HttpPost("record")]
    public IActionResult RecordUser([FromForm] string? To, [FromQuery] string? ToQuery)
    {
        To ??= ToQuery;

        var response = new VoiceResponse();

        var processUrl = new Uri($"/voice/process?To={Uri.EscapeDataString(To)}", UriKind.Relative);

        response.Record(
            action: processUrl,
            method: "POST",
            maxLength: 5,       // Cada turno dura hasta 15 segundos
            timeout: 3,          // Finaliza si hay 1 segundo de silencio
            playBeep: false,     // Sin pitido
            trim: "trim-silence" // Elimina silencios al inicio/final
        );

        return Content(response.ToString(), "application/xml");
    }
    
    // Paso 3: procesar audio y responder
    [HttpPost("process")]
    public async Task<IActionResult> ProcessUser([FromForm] string RecordingUrl, [FromForm] string? To, [FromQuery] string? ToQuery)
    {
        To ??= ToQuery;
        var response = new VoiceResponse();

        // Obtener agente
        var agentsRef = _firestore.Collection("users");
        var snapshot = await agentsRef.GetSnapshotAsync();

        var normalizedTo = NormalizeNumber(To);
        var matchingDoc = FindAgentByPhone(snapshot.Documents, normalizedTo);

        if (matchingDoc == null)
        {
            response.Say("No pude identificar al agente.");
            return Content(response.ToString(), "application/xml");
        }

        var agent = matchingDoc.ConvertTo<AgentConfig>();
        
        // Autenticarse con Twilio para descargar grabación protegida
        if (!agent.Twilio.TryGetValue("twilioSid", out var accountSidObj) ||
            !agent.Twilio.TryGetValue("twilioToken", out var authTokenObj))
        {
            _logger.LogError("❌ Faltan las credenciales de Twilio en Firestore.");
            response.Say("Error de configuración del agente. No se pudo continuar.");
            return Content(response.ToString(), "application/xml");
        }

        var twilioSid = accountSidObj.ToString();
        var twilioToken = authTokenObj.ToString();

        // Crear request con credenciales por llamada (evita problemas de concurrencia)
        var request = new HttpRequestMessage(HttpMethod.Get, RecordingUrl);
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($"{twilioSid}:{twilioToken}"))
            );

        // Descargar la grabación en memoria
        using var originalStream = await (await _httpClient.SendAsync(request)).Content.ReadAsStreamAsync();
        using var memoryStream = new MemoryStream();
        await originalStream.CopyToAsync(memoryStream);
        memoryStream.Position = 0;
        
        _logger.LogInformation("📥 Grabación de audio descargada y cargada en memoria. Tamaño: {Size} bytes", memoryStream.Length);

        // Transcribir con Deepgram
        var transcript = await _deepgramService.TranscribeFromStreamAsync(memoryStream);
        _logger.LogInformation("🎤 Transcripción del usuario: {Transcript}", transcript);

        
        // Transcribir
        /*using var originalStream = await _httpClient.GetStreamAsync(RecordingUrl);
        using var memoryStream = new MemoryStream();
        await originalStream.CopyToAsync(memoryStream);
        memoryStream.Position = 0;

        _logger.LogInformation("📥 Grabación de audio descargada y cargada en memoria. Tamaño: {Size} bytes", memoryStream.Length);

        var transcript = await _deepgramService.TranscribeFromStreamAsync(memoryStream);
        _logger.LogInformation("🎤 Transcripción del usuario: {Transcript}", transcript);*/

        // Detectar intención de terminar
        if (transcript.ToLower().Contains("gracias") || transcript.ToLower().Contains("ya está") || transcript.ToLower().Contains("eso es todo"))
        {
            _logger.LogInformation("📞 Usuario terminó la llamada con: \"{Transcript}\"", transcript);
            response.Say("Gracias por tu llamada. Hasta pronto.");
            return Content(response.ToString(), "application/xml");
        }
        
        if (string.IsNullOrWhiteSpace(transcript))
        {
            _logger.LogWarning("⚠️ La transcripción está vacía. No se detectó habla en la grabación.");
            response.Say("No pude escucharte claramente. ¿Puedes repetirlo?");
            response.Redirect(new Uri("/voice/record", UriKind.Relative));
            return Content(response.ToString(), "application/xml");
        }

        // Generar respuesta IA
        var reply = await _gptService.GetReply(transcript);
        _logger.LogInformation("🧠 Respuesta generada por GPT: {Reply}", reply);
        
        // Loguear la respuesta recibida de GPT
        _logger.LogInformation("Respuesta de GPT para ElevenLabs: {Response}", reply);

        // Generar audio
        var audioStream = await _elevenService.GenerateAudioStreamAsync(reply, agent.Agent["labsId"].ToString());
        _logger.LogInformation("🔊 Audio generado con ElevenLabs para el texto: \"{Reply}\"", reply);
        
        // Subir a Firebase
        var audioUrl = await _firebaseService.UploadAudioAsync(agent.UserId, audioStream);
        _logger.LogInformation("📤 Audio subido a Firebase. URL: {AudioUrl}", audioUrl);

        response.Play(new Uri(audioUrl));
        response.Redirect(new Uri("/voice/record", UriKind.Relative));

        return Content(response.ToString(), "application/xml");
    }
    
    
}

public class TwilioMediaEventVoice
{
    public string Event { get; set; } = "";
    public MediaPayloadVoice? Media { get; set; }
}

public class MediaPayloadVoice
{
    public string Payload { get; set; } = "";
}