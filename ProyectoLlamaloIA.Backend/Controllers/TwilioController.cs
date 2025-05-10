using ProyectoLlamaloIA.Backend.Models;

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

    public TwilioController(FirestoreDb firestoreDb)
    {
        _firestoreDb = firestoreDb;
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
            }
            else
            {
                // Si existe, verificar si hay cambios y actualizar el mapa
                if (twilioConfig["countryCode"]?.ToString() != config.CountryCode)
                {
                    twilioConfig["countryCode"] = config.CountryCode;
                    hasChanges = true;
                }
                if (twilioConfig["phoneNumber"]?.ToString() != config.PhoneNumber.ToString())
                {
                    twilioConfig["phoneNumber"] = config.PhoneNumber;
                    hasChanges = true;
                }
                if (twilioConfig["twilioSid"]?.ToString() != config.TwilioSid)
                {
                    twilioConfig["twilioSid"] = config.TwilioSid;
                    hasChanges = true;
                }
                if (twilioConfig["twilioToken"]?.ToString() != config.TwilioToken)
                {
                    twilioConfig["twilioToken"] = config.TwilioToken;
                    hasChanges = true;
                }
            }

            // Si hay cambios, actualizamos el documento con la nueva configuración de Twilio
            if (hasChanges)
            {
                await userDocRef.SetAsync(
                    new { twilio = twilioConfig }, 
                    SetOptions.MergeFields("twilio") // Solo actualiza el campo "twilio"
                );
                return Ok(new { message = "Configuración de Twilio actualizada correctamente." });
            }

            return Ok(new { message = "No hubo cambios en la configuración de Twilio." });
        }
        catch (Exception ex)
        {
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
}

