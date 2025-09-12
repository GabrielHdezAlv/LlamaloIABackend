using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Google.Cloud.Firestore;
using ProyectoLlamaloIA.Backend.Models;

[Route("user")]
[ApiController]
public class UserController : ControllerBase
{
    private readonly FirestoreDb _firestoreDb;
    private readonly ILogger<Language> _logger;

    public UserController(FirestoreDb firestoreDb, ILogger<Language> logger)
    {
        _firestoreDb = firestoreDb;
        _logger = logger;
    }

    [HttpGet("profile")]
    [Authorize] // 🔒 Requiere autenticación con JWT
    public async Task<IActionResult> GetUserProfile()
    {
        try
        {
            // Obtener el UID del usuario autenticado a partir del token JWT
            string userId = User.FindFirst("user_id")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;


            if (string.IsNullOrEmpty(userId))
                return Unauthorized(new { message = "Token inválido o expirado" });

            // Obtener los datos del usuario desde Firestore
            DocumentReference docRef = _firestoreDb.Collection("users").Document(userId);
            DocumentSnapshot snapshot = await docRef.GetSnapshotAsync();

            if (!snapshot.Exists)
                return NotFound(new { message = "Usuario no encontrado en Firestore" });

            var userData = snapshot.ToDictionary();

            return Ok(new
            {
                userId = userData["id"],
                name = userData["name"],
                lastname = userData["lastname"],
                profileImage = userData["profileImage"],
                enterprise = userData["enterprise"],
                enterpriseWeb = userData["enterpriseWeb"],
                email = userData["email"],
                createdAt = userData["createdAt"],
                twilio = userData["twilio"],
                agent = userData["agent"],
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = "Error al obtener el perfil del usuario", error = ex.Message });
        }
    }
    
    [HttpGet("allLanguages")]
    [Authorize]
    public async Task<IActionResult> GetAllAgents()
    {
        try
        {
            _logger.LogInformation("Iniciando la consulta para obtener todos los idiomas.");

            // Intentar obtener la lista de idiomas de Firestore
            CollectionReference colRef = _firestoreDb.Collection("languages");
                
            // Obtener todos los documentos de la colección
            QuerySnapshot snapshot = await colRef.GetSnapshotAsync();

            if (snapshot.Documents.Count == 0)
            {
                _logger.LogWarning("No se encontraron idiomas en la colección 'languages'.");
                return NotFound(new { message = "No se encontraron idiomas." });
            }

            // Mapear los documentos a instancias del modelo 'Agent'
            var languages = snapshot.Documents
                .Select(doc => doc.ConvertTo<Language>())  // Convertir el documento en un objeto 'Language'
                .ToList();

            _logger.LogInformation("Se han recuperado {LanguageCount} idiomas.", languages.Count);
                
            return Ok(languages);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ocurrió un error al obtener los idiomas.");
            return BadRequest(new { message = "Error al obtener los idiomas", error = ex.Message });
        }
    }
}