using Google.Apis.Auth.OAuth2;
using Google.Cloud.Storage.V1;

namespace ProyectoLlamaloIA.Backend.Services;

public class FirebaseService
{
    private readonly ILogger<FirebaseService> _logger;
    private readonly string _bucketName;
    private readonly StorageClient _storage;
    
    public FirebaseService(IConfiguration config, ILogger<FirebaseService> logger)
    {
        _logger = logger;
        _bucketName = config["Firebase:StorageBucket"] ?? throw new Exception("Falta Firebase:StorageBucket en configuración");

        // Cargar la clave desde ruta o variable
        var credentialPath = config["Firebase:CredentialsPath"];
        if (string.IsNullOrEmpty(credentialPath))
            throw new Exception("Falta Firebase:CredentialsPath en configuración");

        var credential = GoogleCredential.FromFile(credentialPath);
        _storage = StorageClient.Create(credential);
    }
    
    public async Task<string> UploadAudioAsync(string userId, Stream audioStream)
    {
        var audioId = Guid.NewGuid().ToString();
        var objectName = $"audios/{userId}/{audioId}.mp3";

        _logger.LogInformation("Subiendo audio a Firebase Storage: {Path}", objectName);

        await _storage.UploadObjectAsync(
            _bucketName,
            objectName,
            "audio/mpeg",
            audioStream,
            new UploadObjectOptions { PredefinedAcl = PredefinedObjectAcl.PublicRead }
        );

        var publicUrl = $"https://storage.googleapis.com/{_bucketName}/{objectName}";
        return publicUrl;
    }
}