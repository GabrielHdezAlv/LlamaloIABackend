using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text;
using Google.Cloud.Firestore;
using Google.Cloud.Storage.V1;
using Microsoft.AspNetCore.Authorization;
using Newtonsoft.Json;
using ProyectoLlamaloIA.Backend.Models;
using ProyectoLlamaloIA.Backend.Repository;
using ProyectoLlamaloIA.Backend.Utils;

[Route("auth")]
[ApiController]
public class AuthController : ControllerBase
{
    private readonly HttpClient _httpClient;
    private readonly UserRepository _userRepository;
    private readonly FirestoreDb _firestoreDb;
    private const string FirebaseApiKey = "AIzaSyBeDYuVYfL9Multc9cOkXsk7lsL6t-YOUQ"; // ⚠️ Reemplázalo con tu API Key de Firebase

    public AuthController(UserRepository userRepository, FirestoreDb firestoreDb)
    {
        _httpClient = new HttpClient();
        _userRepository = userRepository;
        _firestoreDb = firestoreDb;
    }

    // ✅ 1️⃣ REGISTRO de usuario en Firebase Auth y Firestore
    [HttpPost("register")]
    public async Task<IActionResult> RegisterUser([FromBody] NewUser request)
    {
        if (string.IsNullOrEmpty(request.email) || string.IsNullOrEmpty(request.password))
        {
            return BadRequest(new { message = "El email y la contraseña son obligatorios." });
        }

        var payload = new
        {
            email = request.email,
            password = request.password,
            returnSecureToken = true
        };

        var response = await _httpClient.PostAsync(
            $"https://identitytoolkit.googleapis.com/v1/accounts:signUp?key={FirebaseApiKey}",
            new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json")
        );

        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            return BadRequest(new { message = "No se pudo registrar el usuario en Firebase.", error = responseBody });
        }

        var firebaseResponse = JsonConvert.DeserializeObject<FirebaseAuthResponse>(responseBody);

        // 🔹 Hasheamos la contraseña ANTES de guardarla en Firestore
        string hashedPassword = PasswordHasher.HashPassword(request.password);

        // Obtenemos el primer agente de la lista de agentes
        CollectionReference agentsRef = _firestoreDb.Collection("agents");
        QuerySnapshot snapshot = await agentsRef.Limit(1).GetSnapshotAsync();
        
        DocumentSnapshot firstAgentDoc = snapshot.Documents[0];
        Agent firstAgent = firstAgentDoc.ConvertTo<Agent>();

        // 🔹 Guardamos en Firestore con el mismo ID que Firebase Auth
        User newUser = new User
        {
            userId = firebaseResponse.localId,
            name = request.name,
            lastname = request.lastname,
            profileImage = "",
            enterprise = "",
            enterpriseWeb = "",
            email = request.email,
            password = hashedPassword, // 🔐 Guardamos la contraseña cifrada
            created_at = DateTime.UtcNow,
            twilio = new TwilioInfo("", 0, "", "", ""),
            agent = firstAgent
        };

        _userRepository.Add(newUser);

        // 🔹 Guardar usuario en Firestore (colección "users")
        DocumentReference docRef = _firestoreDb.Collection("users").Document(firebaseResponse.localId);
        await docRef.SetAsync(new
        {
            name = request.name,
            lastname = request.lastname,
            email = request.email,
            createdAt = Timestamp.GetCurrentTimestamp()
        });

        return Ok(new
        {
            Token = firebaseResponse.idToken,
            UserId = firebaseResponse.localId
        });
    }

    // ✅ 2️⃣ LOGIN de usuario y generación de token
    [HttpPost("login")]
    public async Task<IActionResult> FirebaseLogin([FromBody] LoginRequest request)
    {
        if (string.IsNullOrEmpty(request.email) || string.IsNullOrEmpty(request.password))
        {
            return BadRequest(new { message = "Email y contraseña son obligatorios." });
        }

        var payload = new
        {
            email = request.email,
            password = request.password,
            returnSecureToken = true
        };

        var response = await _httpClient.PostAsync(
            $"https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key={FirebaseApiKey}",
            new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json")
        );

        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"🔥 Error de autenticación: {responseBody}");
            return Unauthorized(new { message = "Credenciales inválidas.", error = responseBody });
        }

        var firebaseResponse = JsonConvert.DeserializeObject<FirebaseAuthResponse>(responseBody);
    
        return Ok(new
        {
            Token = firebaseResponse.idToken,
            UserId = firebaseResponse.localId
        });
    }
    
    // ✅ 2️⃣ ACTUALIZAR los datos del usuario
    [HttpPut("profile")]
    [Authorize] // 🔒 Requiere JWT
    public async Task<IActionResult> UpdateUserProfile([FromBody] UpdateUserProfileDto updateDto)
    {
        try
        {
            // 🔑 Obtener el UID desde el token JWT
            string? userId = User.FindFirst("user_id")?.Value
                            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                            ?? User.FindFirst("sub")?.Value;

            if (string.IsNullOrEmpty(userId))
                return Unauthorized(new { message = "Token inválido o expirado" });

            // 🔍 Buscar el documento del usuario en Firestore
            DocumentReference docRef = _firestoreDb.Collection("users").Document(userId);
            DocumentSnapshot snapshot = await docRef.GetSnapshotAsync();

            if (!snapshot.Exists)
                return NotFound(new { message = "Usuario no encontrado en Firestore" });

            // 📦 Solo actualizar los campos que se reciban
            var updates = new Dictionary<string, object>();

            if (!string.IsNullOrEmpty(updateDto.Name))
                updates["name"] = updateDto.Name;

            if (!string.IsNullOrEmpty(updateDto.Lastname))
                updates["lastname"] = updateDto.Lastname;

            if (!string.IsNullOrEmpty(updateDto.ProfileImage))
                updates["profileImage"] = updateDto.ProfileImage;

            if (!string.IsNullOrEmpty(updateDto.Enterprise))
                updates["enterprise"] = updateDto.Enterprise;

            if (!string.IsNullOrEmpty(updateDto.EnterpriseWeb))
                updates["enterpriseWeb"] = updateDto.EnterpriseWeb;

            if (updates.Count == 0)
                return BadRequest(new { message = "No se enviaron datos para actualizar" });

            // ✏️ Actualizar Firestore
            await docRef.UpdateAsync(updates);

            // 🔄 Obtener de nuevo el perfil actualizado
            DocumentSnapshot updatedSnapshot = await docRef.GetSnapshotAsync();
            var updatedData = updatedSnapshot.ToDictionary();

            return Ok(new
            {
                message = "Perfil actualizado correctamente",
                name = updatedData["name"],
                lastname = updatedData["lastname"],
                profileImage = updatedData["profileImage"],
                enterprise = updatedData["enterprise"],
                enterpriseWeb = updatedData["enterpriseWeb"],
                email = updatedData["email"],
                createdAt = updatedData["createdAt"]
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = "Error al actualizar el perfil del usuario", error = ex.Message });
        }
    }
    
    [HttpPost("profile/image")]
    [Authorize]
    public async Task<IActionResult> UploadProfileImage([FromForm] IFormFile image, [FromForm] string userId)
    {
        try
        {
            // Validar autenticación y que el userId corresponda al token
            string? tokenUserId = User.FindFirst("user_id")?.Value
                               ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                               ?? User.FindFirst("sub")?.Value;

            if (string.IsNullOrEmpty(tokenUserId) || tokenUserId != userId)
                return Unauthorized(new { message = "No tienes permiso para modificar este perfil" });

            // Validar archivo
            if (image == null || image.Length == 0)
                return BadRequest(new { message = "Debe enviarse un archivo de imagen" });

            if (image.Length > 5 * 1024 * 1024) // 5MB
                return BadRequest(new { message = "La imagen no puede superar los 5MB" });

            var allowedTypes = new[] { "image/png", "image/jpeg", "image/jpg" };
            if (!allowedTypes.Contains(image.ContentType))
                return BadRequest(new { message = "Formato de imagen no permitido. Use PNG o JPG" });

            // Subida a Firebase Storage
            string bucketName = "llamaloio.firebasestorage.app";
            string extension = Path.GetExtension(image.FileName);
            string fileName = $"profile-images/{userId}/{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}{extension}";

            using var stream = image.OpenReadStream();
            var storage = await StorageClient.CreateAsync();
            await storage.UploadObjectAsync(bucketName, fileName, image.ContentType, stream);
            
            string imageUrl = $"https://storage.googleapis.com/{bucketName}/{fileName}";

            // Actualizar Firestore con la nueva URL
            DocumentReference docRef = _firestoreDb.Collection("users").Document(userId);
            await docRef.UpdateAsync(new Dictionary<string, object>
            {
                { "profileImage", imageUrl }
            });

            // Opcional: guardar historial en otra colección
            // await _firestoreDb.Collection("profileImageHistory").AddAsync(new { userId, imageUrl, timestamp = DateTime.UtcNow });

            // Respuesta
            return Ok(new
            {
                message = "Imagen de perfil actualizada correctamente",
                imageUrl = imageUrl
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = "Error al subir la imagen de perfil", error = ex.Message });
        }
    }

    // ✅ 3️⃣ Obtener datos del usuario autenticado 🔐
    [HttpGet("user")]
    [Authorize]
    public IActionResult GetUserData()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var email = User.FindFirst(ClaimTypes.Email)?.Value;
        
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { message = "No se pudo obtener el usuario autenticado." });
        }

        return Ok(new { UserId = userId, Email = email });
    }
}

// 🔹 Clase para mapear la respuesta de Firebase
public class FirebaseAuthResponse
{
    public string idToken { get; set; }
    public string refreshToken { get; set; }
    public string expiresIn { get; set; }
    public string localId { get; set; }
}

public class NewUser
{
    [Required(ErrorMessage = "El nombre del usuario es obligatorio.")]
    [FirestoreProperty("name")]
    public string name { get; set; }
    
    [Required(ErrorMessage = "El apellido del usuario es obligatorio.")]
    [FirestoreProperty("lastname")]
    public string lastname { get; set; }
    
    [Required(ErrorMessage = "El email del usuario es obligatorio.")]
    [EmailAddress(ErrorMessage = "El email no tiene un formato válido.")]
    [FirestoreProperty("email")]
    public string email { get; set; }
    
    [Required(ErrorMessage = "La contraseña del usuario es obligatorio.")]
    [MinLength(8, ErrorMessage = "La contraseña debe tener al menos 6 caracteres.")]
    [FirestoreProperty("password")]
    public string password { get; set; }
}