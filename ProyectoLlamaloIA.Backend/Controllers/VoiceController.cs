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
    
    public VoiceController(
        FirestoreDb firestore,
        FirebaseService firebaseService,
        DeepgramService deepgramService,
        GPTService gptService,
        ElevenLabsService elevenService,
        ILogger<VoiceController> logger)
    {
        _firestore = firestore;
        _firebaseService = firebaseService;
        _deepgramService = deepgramService;
        _gptService = gptService;
        _elevenService = elevenService;
        _logger = logger;
    }
    
    // Paso 1: llamada entrante
    [HttpPost("incoming")]
    public async Task<IActionResult> IncomingCall([FromForm] string To)
    {
        _logger.LogInformation("📞 Llamada entrante detectada. Número marcado: {To}", To);

        var response = new VoiceResponse();

        // Normalizar número recibido para comparar sin +, espacios ni guiones
        string NormalizeNumber(string number) => number?.Replace("+", "").Replace(" ", "").Replace("-", "").Trim() ?? "";

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
    public IActionResult RecordUser()
    {
        var response = new VoiceResponse();

        response.Say("Puedes empezar a hablar después del tono. Presiona cualquier tecla para finalizar.");
        response.Record(
            action: new Uri("/voice/process", UriKind.Relative),
            method: "POST",
            maxLength: 30,
            finishOnKey: "#",
            playBeep: true);

        return Content(response.ToString(), "application/xml");
    }
    
    // Paso 3: procesar audio y responder
    [HttpPost("process")]
    public async Task<IActionResult> ProcessUser([FromForm] string RecordingUrl, [FromForm] string To)
    {
        var response = new VoiceResponse();

        // Obtener agente
        var agentsRef = _firestore.Collection("users");
        var snapshot = await agentsRef.GetSnapshotAsync();

        DocumentSnapshot? matchingDoc = snapshot.Documents.FirstOrDefault(doc =>
            doc.TryGetValue<Dictionary<string, object>>("twilio", out var twilioData)
            && twilioData.TryGetValue("number", out var number)
            && number.ToString() == To);

        if (matchingDoc == null)
        {
            response.Say("No pude identificar al agente.");
            return Content(response.ToString(), "application/xml");
        }

        var agent = matchingDoc.ConvertTo<AgentConfig>();

        // Transcribir
        var transcript = await _deepgramService.TranscribeFromUrlAsync(RecordingUrl);

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