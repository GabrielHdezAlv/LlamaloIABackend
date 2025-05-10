using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Google.Cloud.Firestore;

[Route("api/user")]
[ApiController]
public class UserController : ControllerBase
{
    private readonly FirestoreDb _firestoreDb;

    public UserController(FirestoreDb firestoreDb)
    {
        _firestoreDb = firestoreDb;
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
                userId = userId,
                name = userData["name"],
                lastname = userData["lastname"],
                email = userData["email"],
                createdAt = userData["createdAt"],
                twilio = userData["twilio"],
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = "Error al obtener el perfil del usuario", error = ex.Message });
        }
    }
}