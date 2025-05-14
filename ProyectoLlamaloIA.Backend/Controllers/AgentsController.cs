using System.Security.Claims;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProyectoLlamaloIA.Backend.Models;

namespace ProyectoLlamaloIA.Backend.Controllers;

public class AgentsController : ControllerBase
{
    private readonly FirestoreDb _firestoreDb;
    private readonly ILogger<AgentsController> _logger;
    
    public AgentsController(FirestoreDb firestoreDb, ILogger<AgentsController> logger)
    {
        _firestoreDb = firestoreDb;
        _logger = logger;
    }

    [HttpGet("/agents/allAgents")]
    [Authorize]
    public async Task<IActionResult> GetAllAgents()
    {
        try
        {
            _logger.LogInformation("Iniciando la consulta para obtener todos los agentes.");

            // Intentar obtener la lista de agentes de Firestore
            CollectionReference colRef = _firestoreDb.Collection("agents");
                
            // Obtener todos los documentos de la colección
            QuerySnapshot snapshot = await colRef.GetSnapshotAsync();

            if (snapshot.Documents.Count == 0)
            {
                _logger.LogWarning("No se encontraron agentes en la colección 'agents'.");
                return NotFound(new { message = "No se encontraron agentes." });
            }

            // Mapear los documentos a instancias del modelo 'Agent'
            var agents = snapshot.Documents
                .Select(doc => doc.ConvertTo<Agent>())  // Convertir el documento en un objeto 'Agent'
                .ToList();

            _logger.LogInformation("Se han recuperado {AgentCount} agentes.", agents.Count);
                
            return Ok(agents);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ocurrió un error al obtener los agentes.");
            return BadRequest(new { message = "Error al obtener los agentes", error = ex.Message });
        }
    }
    
    [HttpPost("/agents/agentConfig")]
    [Authorize]
    public async Task<IActionResult> setAgentConfig([FromBody] Agent agentConfig)
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
            var userAgentConfig = userSnapshot.GetValue<IDictionary<string, object>>("agent");

            // Si existe, verificar si hay cambios y actualizar el mapa
            if (userAgentConfig["labsId"]?.ToString() != agentConfig.LabsId)
            {
                userAgentConfig["labsId"] = agentConfig.LabsId;
                _logger.LogInformation("Actualizando el código de ElevenLabs del agente para el usuario: {UserId}", userId);
            }
            if (userAgentConfig["name"]?.ToString() != agentConfig.Name)
            {
                userAgentConfig["name"] = agentConfig.Name;
                _logger.LogInformation("Actualizando el nombre del agente para el usuario: {UserId}", userId);
            }
            if (userAgentConfig["photoUrl"]?.ToString() != agentConfig.PhotoUrl)
            {
                userAgentConfig["photoUrl"] = agentConfig.PhotoUrl;
                _logger.LogInformation("Actualizando la foto del agente para el usuario: {UserId}", userId);
            }
            if (userAgentConfig["language"]?.ToString() != agentConfig.Language)
            {
                userAgentConfig["language"] = agentConfig.Language;
                _logger.LogInformation("Actualizando el idioma del agente para el usuario: {UserId}", userId);
            }

            _logger.LogInformation("Actualizando la configuración del agente en Firestore para el usuario: {UserId}", userId);
            await userDocRef.SetAsync(
                new { agent = userAgentConfig },
                SetOptions.MergeFields("agent")
            );

            return Ok(new { message = "Configuración de agente actualizada correctamente." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al procesar la configuración del agente para el usuario.");
            return BadRequest(new { message = "Error al procesar la configuración del agente", error = ex.Message });
        }
    }
}