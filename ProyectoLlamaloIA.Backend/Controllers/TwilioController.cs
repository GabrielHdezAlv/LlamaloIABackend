using System.Text;
using ProyectoLlamaloIA.Backend.Models;
using Microsoft.Extensions.Logging; 

namespace ProyectoLlamaloIA.Backend.Controllers;

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Google.Cloud.Firestore;

[Route("api/twilio")]
[ApiController]
public class TwilioController : ControllerBase
{
    private readonly FirestoreDb _firestoreDb;
    private readonly ILogger<TwilioController> _logger;

    public TwilioController(FirestoreDb firestoreDb, ILogger<TwilioController> logger)
    {
        _firestoreDb = firestoreDb;
        _logger = logger;
    }

    [HttpPost("config")]
    [Authorize]
    public async Task<IActionResult> SetTwilioConfig([FromBody] TwilioInfo config)
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

            bool hasChanges = false;

            // Si no existe, crear el mapa 'twilio' con los valores proporcionados
            if (twilioConfig == null)
            {
                twilioConfig = new Dictionary<string, object>
                {
                    { "countryCode", config.CountryCode },
                    { "phoneNumber", config.PhoneNumber },
                    { "twilioSid", config.TwilioSid },
                    { "twilioToken", config.TwilioToken }
                };
                hasChanges = true;
                _logger.LogInformation("Creando nueva configuración de Twilio para el usuario: {UserId}", userId);
            }
            else
            {
                // Si existe, verificar si hay cambios y actualizar el mapa
                if (twilioConfig["countryCode"]?.ToString() != config.CountryCode)
                {
                    twilioConfig["countryCode"] = config.CountryCode;
                    hasChanges = true;
                    _logger.LogInformation("Actualizando código de país para el usuario: {UserId}", userId);
                }
                if (twilioConfig["phoneNumber"]?.ToString() != config.PhoneNumber.ToString())
                {
                    twilioConfig["phoneNumber"] = config.PhoneNumber;
                    hasChanges = true;
                    _logger.LogInformation("Actualizando número de teléfono para el usuario: {UserId}", userId);
                }
                if (twilioConfig["twilioSid"]?.ToString() != config.TwilioSid)
                {
                    twilioConfig["twilioSid"] = config.TwilioSid;
                    hasChanges = true;
                    _logger.LogInformation("Actualizando SID de Twilio para el usuario: {UserId}", userId);
                }
                if (twilioConfig["twilioToken"]?.ToString() != config.TwilioToken)
                {
                    twilioConfig["twilioToken"] = config.TwilioToken;
                    hasChanges = true;
                    _logger.LogInformation("Actualizando Token de Twilio para el usuario: {UserId}", userId);
                }
            }

            // Validar credenciales de Twilio si hay cambios
            // Si hay cambios, actualizamos el documento con la nueva configuración de Twilio
            if (hasChanges)
            {
                _logger.LogInformation("Actualizando la configuración de Twilio en Firestore para el usuario: {UserId}", userId);
                await userDocRef.SetAsync(
                    new { twilio = twilioConfig },
                    SetOptions.MergeFields("twilio") // Solo actualiza el campo "twilio"
                );

                return Ok(new { message = "Configuración de Twilio actualizada correctamente." });
            }
            else
            {
                _logger.LogInformation("No hubo cambios en la configuración de Twilio, validando credenciales.");
                bool valid = await ValidateTwilioCredentialsAsync(config.TwilioSid, config.TwilioToken);
                if (!valid)
                {
                    _logger.LogWarning("Las credenciales de Twilio no son válidas para el SID: {TwilioSid}", config.TwilioSid);
                    return BadRequest(new { message = "Las credenciales de Twilio son inválidas." });
                }

                return Ok(new { message = "No hubo cambios en la configuración de Twilio, pero las credenciales son válidas." });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al procesar la configuración de Twilio para el usuario.");
            return BadRequest(new { message = "Error al procesar la configuración de Twilio", error = ex.Message });
        }
    }


    
    [HttpGet("info")]
    [Authorize]
    public async Task<IActionResult> GetTwilioConfig()
    {
        try
        {
            // Obtener el UID del usuario autenticado
            string userId = User.FindFirst("user_id")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(new { message = "Token inválido o expirado" });

            // Obtener la referencia al documento del usuario en la colección "users"
            DocumentReference userDocRef = _firestoreDb.Collection("users").Document(userId);

            // Intentar obtener la configuración de Twilio del usuario
            DocumentReference twilioDocRef = userDocRef.Collection("twilio").Document("config");
            DocumentSnapshot twilioSnapshot = await twilioDocRef.GetSnapshotAsync();

            if (!twilioSnapshot.Exists)
            {
                return NotFound(new { message = "Configuración de Twilio no encontrada para el usuario." });
            }

            // Si existe, devolver la configuración de Twilio
            var twilioConfig = twilioSnapshot.ToDictionary();
            return Ok(twilioConfig);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = "Error al obtener la configuración de Twilio", error = ex.Message });
        }
    }
    
    private async Task<bool> ValidateTwilioCredentialsAsync(string sid, string token)
    {
        using (var client = new HttpClient())
        {
            var byteArray = Encoding.ASCII.GetBytes($"{sid}:{token}");
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));

            try
            {
                var response = await client.GetAsync($"https://api.twilio.com/2010-04-01/Accounts/{sid}.json");

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Credenciales de Twilio válidas para el SID: {Sid}", sid);
                    return true; // Credenciales válidas
                }
                else
                {
                    _logger.LogWarning("Error al validar las credenciales de Twilio. Código de estado: {StatusCode}", response.StatusCode);
                    return false;
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Error al conectar con la API de Twilio para el SID: {Sid}", sid);
                return false; // Error en la conexión
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inesperado al validar las credenciales de Twilio para el SID: {Sid}", sid);
                return false; // Error genérico
            }
        }
    }
}

