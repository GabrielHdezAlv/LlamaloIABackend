using System.Diagnostics;
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

        // ✅ Verificar antigüedad de la cuenta
        if (matchingDoc.TryGetValue<Google.Cloud.Firestore.Timestamp>("createdAt", out var createdAtTimestamp))
        {
            var createdAt = createdAtTimestamp.ToDateTime();
            var accountAge = DateTime.UtcNow - createdAt;

            // if (accountAge.TotalDays >= 7)
            // {
            //     _logger.LogWarning("❌ Cuenta con {Days} días de antigüedad. No se permite la llamada.", accountAge.TotalDays);
            //     response.Say("Su cuenta tiene más de 7 días de antigüedad. No puede realizar esta llamada.", voice: "alice");
            //     return Content(response.ToString(), "application/xml");
            // }
        }

        // ✅ Validar minutos consumidos
        var agent = matchingDoc.ConvertTo<AgentConfig>();

        if (agent.Twilio.TryGetValue("twilioSid", out var sidObj) &&
            agent.Twilio.TryGetValue("twilioToken", out var tokenObj))
        {
            var minutes = await GetTwilioMinutesAsync(sidObj.ToString(), tokenObj.ToString());

            if (minutes >= 30)
            {
                _logger.LogWarning("❌ Usuario ha consumido {Minutes} minutos. No se permite la llamada.", minutes);
                response.Say("Ha superado los 30 minutos de llamadas permitidos. No puede realizar esta llamada.", voice: "alice");
                return Content(response.ToString(), "application/xml");
            }
        }
        else
        {
            _logger.LogError("❌ Faltan credenciales Twilio del agente para validar minutos.");
        }
        
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
    
    private async Task<double> GetTwilioMinutesAsync(string sid, string token)
    {
        using var client = new HttpClient();
        var byteArray = Encoding.ASCII.GetBytes($"{sid}:{token}");
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));

        // Consumo de este mes
        var response = await client.GetAsync($"https://api.twilio.com/2010-04-01/Accounts/{sid}/Usage/Records/ThisMonth.json");
        if (!response.IsSuccessStatusCode)
            return 0;

        var content = await response.Content.ReadAsStringAsync();
        var json = System.Text.Json.JsonDocument.Parse(content);

        // Buscar el registro de llamadas (total)
        var calls = json.RootElement.GetProperty("usage_records")
            .EnumerateArray()
            .FirstOrDefault(x => x.GetProperty("category").GetString() == "calls");

        if (calls.ValueKind == System.Text.Json.JsonValueKind.Undefined)
            return 0;

        // Retornar minutos consumidos como double
        if (double.TryParse(calls.GetProperty("usage").GetString(), out var minutes))
            return minutes;

        return 0;
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
            maxLength: 30,       // Permitir preguntas extensas (hasta 30 segundos por turno)
            timeout: 3,          // Detectar fin de turno tras 3 segundos de silencio (balance entre fluidez y respuesta ágil)
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
        using var downloadResponse = await _httpClient.SendAsync(request);
        var mediaType = downloadResponse.Content.Headers.ContentType?.MediaType ?? "audio/wav";
        using var originalStream = await downloadResponse.Content.ReadAsStreamAsync();
        using var memoryStream = new MemoryStream();
        await originalStream.CopyToAsync(memoryStream);
        memoryStream.Position = 0;
        
        _logger.LogInformation("📥 Grabación de audio descargada y cargada en memoria. Tamaño: {Size} bytes", memoryStream.Length);

        // Transcribir con Deepgram
        var timing = Stopwatch.StartNew();
        var transcript = await _deepgramService.TranscribeFromStreamAsync(memoryStream, mediaType);
        timing.Stop();
        _logger.LogInformation(
            "🎤 Deepgram transcripción completada en {ElapsedMs} ms. Texto: {Transcript}",
            timing.ElapsedMilliseconds,
            transcript);

        
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
            var farewell = "Gracias por tu llamada. Hasta pronto.";
            timing.Restart();
            var audStream = await _elevenService.GenerateAudioStreamAsync(farewell, agent.Agent["labsId"].ToString());
            timing.Stop();
            _logger.LogInformation("🔊 ElevenLabs despedida generada en {ElapsedMs} ms", timing.ElapsedMilliseconds);

            timing.Restart();
            var audUrl = await _firebaseService.UploadAudioAsync(agent.UserId, audStream);
            timing.Stop();
            _logger.LogInformation("📤 Firebase subida despedida en {ElapsedMs} ms. URL: {AudioUrl}", timing.ElapsedMilliseconds, audUrl);

            response.Play(new Uri(audUrl));
            return Content(response.ToString(), "application/xml");
        }
        
        if (string.IsNullOrWhiteSpace(transcript))
        {
            _logger.LogWarning("⚠️ La transcripción está vacía. No se detectó habla en la grabación.");
            var retry = "No pude escucharte claramente. ¿Puedes repetirlo?";
            timing.Restart();
            var audStream = await _elevenService.GenerateAudioStreamAsync(retry, agent.Agent["labsId"].ToString());
            timing.Stop();
            _logger.LogInformation("🔊 ElevenLabs reintento generado en {ElapsedMs} ms", timing.ElapsedMilliseconds);

            timing.Restart();
            var audUrl = await _firebaseService.UploadAudioAsync(agent.UserId, audStream);
            timing.Stop();
            _logger.LogInformation("📤 Firebase subida reintento en {ElapsedMs} ms. URL: {AudioUrl}", timing.ElapsedMilliseconds, audUrl);

            response.Play(new Uri(audUrl));
            response.Redirect(new Uri("/voice/record", UriKind.Relative));
            return Content(response.ToString(), "application/xml");
            
            response.Say("No pude escucharte claramente. ¿Puedes repetirlo?");
            response.Redirect(new Uri("/voice/record", UriKind.Relative));
            return Content(response.ToString(), "application/xml");
        }

        // Generar respuesta IA
        timing.Restart();
        var reply = await _gptService.GetReply(transcript);
        timing.Stop();
        _logger.LogInformation("🧠 GPT respuesta generada en {ElapsedMs} ms", timing.ElapsedMilliseconds);
        _logger.LogInformation("🧠 Contenido respuesta GPT: {Reply}", reply);
        
        // Loguear la respuesta recibida de GPT
        _logger.LogInformation("Respuesta de GPT para ElevenLabs: {Response}", reply);

        // Generar audio
        timing.Restart();
        var audioStream = await _elevenService.GenerateAudioStreamAsync(reply, agent.Agent["labsId"].ToString());
        timing.Stop();
        _logger.LogInformation("🔊 ElevenLabs respuesta generada en {ElapsedMs} ms", timing.ElapsedMilliseconds);
        
        // Subir a Firebase
        timing.Restart();
        var audioUrl = await _firebaseService.UploadAudioAsync(agent.UserId, audioStream);
        timing.Stop();
        _logger.LogInformation("📤 Firebase subida respuesta en {ElapsedMs} ms. URL: {AudioUrl}", timing.ElapsedMilliseconds, audioUrl);

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