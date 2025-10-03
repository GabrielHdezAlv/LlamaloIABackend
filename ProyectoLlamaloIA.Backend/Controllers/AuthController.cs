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
    private readonly IConfiguration _configuration;
    private const string FirebaseApiKey = "AIzaSyBeDYuVYfL9Multc9cOkXsk7lsL6t-YOUQ";
    
    public AuthController(UserRepository userRepository, FirestoreDb firestoreDb, IConfiguration configuration)
    {
        _httpClient = new HttpClient();
        _userRepository = userRepository;
        _firestoreDb = firestoreDb;
        _configuration = configuration;
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

        String textAgentInteraction = "Rol:\nEres un agente telefónico de inteligencia artificial llamado Alex y trabajas como asistente virtual de atención al cliente de JD Sports, la reconocida cadena internacional líder en moda urbana, ropa deportiva y calzado de las mejores marcas.\n\nTarea:\nTu función es ayudar a los clientes que llaman respondiendo preguntas, resolviendo problemas relacionados con pedidos, devoluciones, productos, envíos y promociones. Debes proporcionar una atención profesional, clara, amable y eficiente, alineada con la imagen moderna y de calidad de JD Sports.\n\nResponsabilidades principales:\n\nPedidos:\n\nConsultar el estado de un pedido (preparación, enviado, entregado o pendiente).\n\nProporcionar el número de seguimiento si el pedido ha sido enviado.\n\nAyudar si un pedido no ha llegado o se ha perdido.\n\nDevoluciones y cambios:\n\nExplicar la política de devoluciones: 28 días desde la recepción, siempre que el producto esté en condiciones originales y con etiqueta.\n\nInformar sobre cómo realizar una devolución (puntos JD, envío postal o devoluciones online).\n\nResolver dudas sobre reembolsos, plazos y procesos.\n\nConsultas sobre productos:\n\nConfirmar disponibilidad de productos (tallas, colores o modelos).\n\nAclarar dudas sobre tallaje, materiales, especificaciones, colecciones o lanzamientos.\n\nEn caso de que un producto esté agotado, ofrecer alternativas o informar sobre posibles reposiciones.\n\nInformación sobre envíos:\n\nExplicar los tipos de envío: estándar, exprés y recogida en tienda.\n\nInformar sobre tiempos de entrega:\n\nEnvío estándar: 3-5 días laborables.\n\nEnvío exprés: 1-2 días laborables.\n\nRecogida en tienda: 2-5 días laborables.\n\nResolver problemas con retrasos o incidencias de transporte.\n\nPagos y facturación:\n\nAclarar dudas sobre métodos de pago aceptados: tarjeta bancaria, PayPal, Klarna (pago a plazos) y tarjetas regalo JD.\n\nAsistir en casos de pago fallido o doble cobro.\n\nGestionar solicitudes de factura o comprobante de compra.\n\nCuentas y soporte técnico:\n\nAyudar con problemas de acceso a la cuenta, contraseñas olvidadas o errores en la web/app.\n\nPromociones, ofertas y lanzamientos:\n\nInformar sobre ofertas activas, rebajas, códigos de descuento, exclusivas online y próximos lanzamientos de zapatillas u otros productos.\n\nProblemas o incidencias:\n\nManejar quejas, productos defectuosos, entregas incorrectas o paquetes dañados.\n\nOfrecer solución inmediata o escalar el caso a un agente humano cuando sea necesario.\n\nProcedimiento cuando no puedes resolver:\n\nSi no puedes resolver una consulta específica, toma los datos del cliente (nombre, correo y número de pedido o teléfono) y comunica que un agente especializado le contactará lo antes posible.\n\nTono y estilo:\n\nMantén siempre un tono joven, profesional, cercano y eficiente, acorde con la imagen moderna de JD Sports.\n\nSé claro, directo y resolutivo.\n\nEvita sonar robótico, usa un lenguaje natural.\n\nPersonaliza la conversación usando el nombre del cliente si te lo proporciona.\n\nPolíticas principales de JD Sports:\n\nDevoluciones: 28 días desde la entrega (en tienda o por correo).\n\nEnvíos: estándar (3-5 días), exprés (1-2 días), recogida en tienda (2-5 días).\n\nAtención al cliente: Lunes a viernes de 9:00 a 18:00 (hora local).\n\nNotas clave:\n\nNo inventes información. Si no estás seguro, indica que un agente humano continuará la gestión.\n\nNunca des datos personales sin validar la identidad del cliente (correo, nombre o número de pedido).\n\nMantén la marca JD Sports siempre presente en tus respuestas para reforzar confianza.\n\nEjemplo breve de conversación:\nCliente: Hola, mi pedido no ha llegado.\nTú: Claro, ¿puedes darme tu número de pedido o el correo con el que realizaste la compra?\nCliente: Sí, es 47892.\nTú: Perfecto, veo que tu pedido está en tránsito y deberías recibirlo en las próximas 24 horas. Te comparto el número de seguimiento: 72910XXXXX. ¿Puedo ayudarte en algo más?";

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
            password = hashedPassword, 
            createdAt = Timestamp.GetCurrentTimestamp(),
            twilio = new TwilioInfo("", 0, "", "", "", "", textAgentInteraction),
            agent = firstAgent
        };

        _userRepository.Add(newUser);

        // 🔹 Guardar usuario en Firestore (colección "users")
        DocumentReference docRef = _firestoreDb.Collection("users").Document(firebaseResponse.localId);
        await docRef.SetAsync(newUser);

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
    
    // RECOVER de la contraseña del usuario y validación de que el correo facilitado corresponde a alguna cuenta
    [HttpPost("recover")]
    public async Task<IActionResult> RecoverPassword([FromBody] RecoverRequest request)
    {
        if (string.IsNullOrEmpty(request.Email))
        {
            return BadRequest(new { message = "El email es obligatorio." });
        }

        var payload = new
        {
            requestType = "PASSWORD_RESET",
            email = request.Email
        };

        var response = await _httpClient.PostAsync(
            $"https://identitytoolkit.googleapis.com/v1/accounts:sendOobCode?key={FirebaseApiKey}",
            new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json")
        );

        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"🔥 Error en recover: {responseBody}");
            return BadRequest(new { message = "No se pudo enviar el correo de recuperación.", error = responseBody });
        }

        return Ok(new { message = "Si el correo está registrado, recibirás un email con instrucciones." });
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
                createdAt = updatedData["createdAt"],
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = "Error al actualizar el perfil del usuario", error = ex.Message });
        }
    }
    
    [HttpPost("profile/image")]
    [Authorize]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadProfileImage([FromForm] UploadProfileImageDto dto)
    {
        var image = dto.Image;

        if (image == null || image.Length == 0)
            return BadRequest(new { message = "Debe enviarse un archivo de imagen" });

        if (image.Length > 5 * 1024 * 1024)
            return BadRequest(new { message = "La imagen no puede superar los 5MB" });

        var allowedTypes = new[] { "image/png", "image/jpeg", "image/jpg" };
        if (!allowedTypes.Contains(image.ContentType))
            return BadRequest(new { message = "Formato de imagen no permitido. Use PNG o JPG" });

        var bucket = _configuration["Firebase:StorageBucket"];
        var storage = StorageClient.Create();
        string extension = Path.GetExtension(image.FileName);
        string objectName = $"profile-images/{User.FindFirstValue("sub")}{extension}";

        // Eliminar anterior si existe
        var objects = storage.ListObjects(bucket, $"profile-images/{User.FindFirstValue("sub")}");
        foreach (var obj in objects)
            await storage.DeleteObjectAsync(bucket, obj.Name);

        using var stream = image.OpenReadStream();
        await storage.UploadObjectAsync(
            bucket,
            objectName,
            image.ContentType,
            stream,
            new UploadObjectOptions { PredefinedAcl = PredefinedObjectAcl.PublicRead }
        );

        var imageUrl = $"https://storage.googleapis.com/{bucket}/{objectName}";

        var userDocRef = _firestoreDb.Collection("users").Document(User.FindFirstValue("sub"));
        await userDocRef.UpdateAsync(new Dictionary<string, object> { { "profileImage", imageUrl } });

        return Ok(new { message = "Imagen de perfil actualizada correctamente", imageUrl });
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