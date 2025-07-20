using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Mvc;
using ProyectoLlamaloIA.Backend.Models;
using ProyectoLlamaloIA.Backend.Services;
using Twilio.AspNet.Core;
using Twilio.TwiML;
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
    
    // Paso 1: llamada entrante
    [HttpPost("incoming")]
    public async Task<IActionResult> IncomingCall([FromForm] string To)
    {
        _logger.LogInformation("📞 Llamada entrante detectada. Número marcado: {To}", To);

        var response = new VoiceResponse();

        var normalizedTo = NormalizeNumber(To);

        var agentsRef = _firestore.Collection("users");
        var snapshot = await agentsRef.GetSnapshotAsync();

        DocumentSnapshot? matchingDoc = null;

        foreach (var doc in snapshot.Documents)
        {
            if (doc.TryGetValue<Dictionary<string, object>>("twilio", out var twilioData))
            {
                if (twilioData.TryGetValue("countryCode", out var countryCodeObj) &&
                    twilioData.TryGetValue("phoneNumber", out var phoneNumberObj))
                {
                    var fullNumber = $"{countryCodeObj}{phoneNumberObj}";
                    var normalizedFullNumber = NormalizeNumber(fullNumber);

                    if (normalizedFullNumber == normalizedTo)
                    {
                        matchingDoc = doc;
                        break;
                    }
                }
            }
        }

        if (matchingDoc == null)
        {
            response.Say("El número de agente no está configurado.");
            return Content(response.ToString(), "application/xml");
        }

        var agent = matchingDoc.ConvertTo<AgentConfig>();
        var welcomeUrl = agent.Twilio["firstInteractionUrl"].ToString();

        response.Play(new Uri(welcomeUrl));
        response.Redirect(new Uri("/voice/record", UriKind.Relative));

        return Content(response.ToString(), "application/xml");
    }
    
    // Paso 2: grabar al usuario
    [HttpPost("record")]
    public IActionResult RecordUser([FromForm] string? To, [FromQuery] string? ToQuery)
    {
        To ??= ToQuery;

        var response = new VoiceResponse();
        response.Say("Puedes empezar a hablar después del tono. Presiona cualquier tecla para finalizar.");

        var processUrl = new Uri($"/voice/process?To={Uri.EscapeDataString(To)}", UriKind.Relative);

        response.Record(
            action: processUrl,
            method: "POST",
            maxLength: 30,
            finishOnKey: "#",
            playBeep: true);

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
        var twilioSid = agent.Twilio["accountSid"].ToString();
        var twilioToken = agent.Twilio["authToken"].ToString();

        var byteArray = System.Text.Encoding.ASCII.GetBytes($"{twilioSid}:{twilioToken}");
        _httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));

        // Transcribir
        using var recordingStream = await _httpClient.GetStreamAsync(RecordingUrl);
        var transcript = await _deepgramService.TranscribeFromStreamAsync(recordingStream);

        // Detectar intención de terminar
        if (transcript.ToLower().Contains("gracias") || transcript.ToLower().Contains("ya está") || transcript.ToLower().Contains("eso es todo"))
        {
            response.Say("Gracias por tu llamada. Hasta pronto.");
            return Content(response.ToString(), "application/xml");
        }

        // Generar respuesta IA
        var prompt = agent.Agent["prompt"].ToString();
        var reply = await _gptService.GetReply(prompt, transcript);

        // Generar audio
        var audioStream = await _elevenService.GenerateAudioStreamAsync(reply, agent.Agent["labsId"].ToString());

        // Subir a Firebase
        var audioUrl = await _firebaseService.UploadAudioAsync(agent.UserId, audioStream);

        response.Play(new Uri(audioUrl));
        response.Redirect(new Uri("/voice/record", UriKind.Relative));

        return Content(response.ToString(), "application/xml");
    }
}