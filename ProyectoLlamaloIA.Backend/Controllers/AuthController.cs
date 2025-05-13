using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Authorization;
using Newtonsoft.Json;
using ProyectoLlamaloIA.Backend.Models;
using ProyectoLlamaloIA.Backend.Repository;
using ProyectoLlamaloIA.Backend.Utils;

[Route("api/auth")]
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
            Id = firebaseResponse.localId, // ✅ Usamos el ID de Firebase Auth
            name = request.name,
            lastname = request.lastname,
            email = request.email,
            password = hashedPassword, // 🔐 Guardamos la contraseña cifrada
            created_at = DateTime.UtcNow,
            twilio = new TwilioInfo("", 0, "", ""),
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