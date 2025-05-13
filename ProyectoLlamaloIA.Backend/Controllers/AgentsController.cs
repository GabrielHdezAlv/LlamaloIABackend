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

    [HttpGet("allAgents")]
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
}