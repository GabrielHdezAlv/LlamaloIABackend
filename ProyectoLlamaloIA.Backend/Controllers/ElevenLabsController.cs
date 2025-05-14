using System.Net.Http.Headers;
using System.Security.Claims;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProyectoLlamaloIA.Backend.Models;

namespace ProyectoLlamaloIA.Backend.Controllers;

[ApiController]
[Route("elevenLabs")]
public class ElevenLabsController : ControllerBase
{
    private readonly HttpClient _httpClient;
    private readonly FirestoreDb _firestoreDb;
    private readonly ILogger<AgentsController> _logger;
    
    private const string ApiKey = "sk_0f16002177bf557f9617b0dfc6ea0f5d23571031949145a5";
    private const string VoiceId = "zRUArUmK0DWSP7K6mmLW";

    public ElevenLabsController(IHttpClientFactory httpClientFactory, FirestoreDb firestoreDb, ILogger<AgentsController> logger)
    {
        _httpClient = httpClientFactory.CreateClient();
        _firestoreDb = firestoreDb;
        _logger = logger;
    }

    public class TextoRequest
    {
        public string Texto { get; set; }
    }

    [HttpPost("generar-audio")]
    public async Task<IActionResult> GenerarAudio([FromBody] TextoRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Texto))
        {
            return BadRequest("Texto es requerido.");
        }

        var endpoint = $"https://api.elevenlabs.io/v1/text-to-speech/{VoiceId}";

        var payload = new
        {
            text = request.Texto,
            model_id = "eleven_multilingual_v2",
            voice_settings = new
            {
                stability = 0.5,
                similarity_boost = 0.8
            }
        };

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json")
        };

        httpRequest.Headers.Add("xi-api-key", ApiKey);
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("audio/mpeg"));

        var response = await _httpClient.SendAsync(httpRequest);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            return StatusCode((int)response.StatusCode, $"Error de ElevenLabs: {error}");
        }

        var audioStream = await response.Content.ReadAsStreamAsync();

        return File(audioStream, "audio/mpeg", "voz.mp3");
    }
    
    [HttpPost("setFirstInteraction")]
    [Authorize]
    public async Task<IActionResult> SetTwilioConfig([FromBody] string firstInteraction)
         {
        try
        {
            // Obtener el UID del usuario autenticado
            string userId = User.FindFirst("user_id")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(new { message = "Token inválido o expirado" });

            // Obtener la referencia al documento del usuario en la colección "users"
            DocumentReference userDocRef = _firestoreDb.Collection("users").Document(userId);

            // Intentar obtener el documento del usuario
            DocumentSnapshot userSnapshot = await userDocRef.GetSnapshotAsync();

            if (!userSnapshot.Exists)
            {
                return NotFound(new { message = "Usuario no encontrado." });
            }

            // Verificar si el campo 'twilio' ya existe en el documento
            var twilioConfig = userSnapshot.GetValue<IDictionary<string, object>>("twilio");

            // Si existe, verificar si hay cambios y actualizar el mapa
            if (twilioConfig["firstInteraction"]?.ToString() != firstInteraction)
            {
                twilioConfig["firstInteraction"] = firstInteraction;
                _logger.LogInformation("Actualizando el texto de primera interacción para el usuario: {UserId}", userId);
            }

            _logger.LogInformation("Actualizando la configuración de Twilio en Firestore para el usuario: {UserId}", userId);
            await userDocRef.SetAsync(
                new { twilio = twilioConfig },
                SetOptions.MergeFields("twilio")
            );

            return Ok(new { message = "Configuración de Twilio actualizada correctamente." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al procesar la configuración de Twilio para el usuario.");
            return BadRequest(new { message = "Error al procesar la configuración de Twilio", error = ex.Message });
        }
    }
}