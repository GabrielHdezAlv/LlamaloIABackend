using System.Net.Http.Headers;
using System.Security.Claims;
using Google;
using Google.Cloud.Firestore;
using Google.Cloud.Storage.V1;
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
    private readonly IConfiguration _configuration;
    
    private const string ApiKey = "sk_0f16002177bf557f9617b0dfc6ea0f5d23571031949145a5";
    private const string VoiceId = "zRUArUmK0DWSP7K6mmLW";

    public ElevenLabsController(IHttpClientFactory httpClientFactory, FirestoreDb firestoreDb, ILogger<AgentsController> logger, IConfiguration configuration)
    {
        _httpClient = httpClientFactory.CreateClient();
        _firestoreDb = firestoreDb;
        _logger = logger;
        _configuration = configuration;
    }
    
    [HttpPost("setFirstInteraction")]
    [Authorize]
    public async Task<IActionResult> setFirstInteraction([FromBody] string firstInteraction)
         {
        try
        {
            // Obtener el UID del usuario autenticado
            string userId = User.FindFirst("user_id")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(new { message = "Token inválido o expirado" });

            // Obtener la referencia al documento del usuario en la colección "users"
            DocumentReference userDocRef = _firestoreDb.Collection("users").Document(userId);
            DocumentSnapshot userSnapshot = await userDocRef.GetSnapshotAsync();

            if (!userSnapshot.Exists)
                return NotFound(new { message = "Usuario no encontrado." });

            // Verificar si el campo 'twilio' ya existe en el documento
            var twilioConfig = userSnapshot.GetValue<IDictionary<string, object>>("twilio");

            if (twilioConfig["firstInteraction"]?.ToString() != firstInteraction)
            {
                var endpoint = $"https://api.elevenlabs.io/v1/text-to-speech/{VoiceId}";

                var payload = new
                {
                    text = firstInteraction,
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
                
                // Subir el audio a Firebase Storage
                var bucket = _configuration["Firebase:StorageBucket"];
                var storage = StorageClient.Create();
                var audioId = Guid.NewGuid().ToString();
                var objectName = $"audios/{userId}/{audioId}.mp3";

                
                // Verificar si el archivo ya existe
                try
                {
                    // Comprobar si el archivo ya existe en el bucket
                    var existingObject = await storage.GetObjectAsync(bucket, objectName);

                    if (existingObject != null)
                    {
                        // Eliminar el archivo anterior
                        _logger.LogInformation("Eliminando archivo antiguo en Firebase Storage: {ObjectName}", objectName);
                        await storage.DeleteObjectAsync(bucket, objectName);
                    }
                }
                catch (GoogleApiException ex) when (ex.Error.Code == 404)
                {
                    // Si el archivo no existe (error 404), no hacer nada
                    _logger.LogInformation("Archivo no encontrado, no es necesario eliminar.");
                }

                // Subir el nuevo archivo
                await storage.UploadObjectAsync(
                    bucket,
                    objectName,
                    "audio/mpeg",
                    audioStream,
                    new UploadObjectOptions { PredefinedAcl = PredefinedObjectAcl.PublicRead }
                );

                var publicUrl = $"https://storage.googleapis.com/{bucket}/{objectName}";
                _logger.LogInformation("Audio subido y disponible en: {Url}", publicUrl);

                // Guardar la URL directamente en el campo "firstInteraction"
                twilioConfig["firstInteractionUrl"] = publicUrl;
                twilioConfig["firstInteraction"] = firstInteraction;


                await userDocRef.SetAsync(
                    new { twilio = twilioConfig },
                    SetOptions.MergeFields("twilio"));

                return Ok(new { message = "Audio generado y URL guardada exitosamente.", audioUrl = publicUrl });
            }

            return Ok(new { message = "El audio a generar tiene el mismo contenido que el ya realizado" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al procesar la configuración de Twilio para el usuario.");
            return BadRequest(new { message = "Error al procesar la configuración de Twilio", error = ex.Message });
        }
    }
}