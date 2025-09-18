using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ProyectoLlamaloIA.Backend.Services;

public class ElevenLabsService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly ILogger<ElevenLabsService> _logger;
    
    public ElevenLabsService(IConfiguration config, ILogger<ElevenLabsService> logger)
    {
        _apiKey = config["ElevenLabs:ApiKey"] ?? throw new Exception("Falta ElevenLabs:ApiKey en configuración");
        _logger = logger;
        _httpClient = new HttpClient();
    }
    
    public async Task<Stream> GenerateAudioStreamAsync(string text, string voiceId)
    {
        var endpoint = $"https://api.elevenlabs.io/v1/text-to-speech/{voiceId}";

        var payload = new
        {
            text = text,
            model_id = "eleven_flash_v2_5",
            voice_settings = new
            {
                stability = 0.5,
                similarity_boost = 0.8
            }
        };

        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = content
        };

        request.Headers.Add("xi-api-key", _apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("audio/mpeg"));

        var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            _logger.LogError("Error en ElevenLabs: {Error}", error);
            throw new Exception("Error generando audio en ElevenLabs");
        }

        return await response.Content.ReadAsStreamAsync();
    }
    
    public async IAsyncEnumerable<byte[]> GenerateAudioStreamChunks(string text, string voiceId = "defaultVoiceId")
    {
        // Genera el audio completo como Stream
        await using var stream = await GenerateAudioStreamAsync(text, voiceId);

        int chunkSize = 32000; // 32 KB por chunk (ajusta según tu necesidad)
        var buffer = new byte[chunkSize];

        int bytesRead;
        while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            if (bytesRead < chunkSize)
            {
                // Si el último chunk es más pequeño, recortarlo
                var lastChunk = new byte[bytesRead];
                Array.Copy(buffer, 0, lastChunk, 0, bytesRead);
                yield return lastChunk;
            }
            else
            {
                yield return buffer;
                buffer = new byte[chunkSize]; // Reiniciar buffer para siguiente lectura
            }
        }
    }
}