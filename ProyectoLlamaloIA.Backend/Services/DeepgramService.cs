using System.Net.Http.Headers;
using System.Text.Json;

namespace ProyectoLlamaloIA.Backend.Services;

public class DeepgramService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<DeepgramService> _logger;
    private readonly string _apiKey;
    
    public DeepgramService(IConfiguration config, ILogger<DeepgramService> logger)
    {
        _apiKey = config["Deepgram:ApiKey"] ?? throw new Exception("Falta Deepgram:ApiKey en configuración");
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Token", _apiKey);
        _logger = logger;
    }
    
    public async Task<string> TranscribeFromUrlAsync(string audioUrl)
    {
        var requestUrl = "https://api.deepgram.com/v1/listen?language=es&punctuate=true";

        var payload = new { url = audioUrl };

        var content = new StringContent(JsonSerializer.Serialize(payload));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var response = await _httpClient.PostAsync(requestUrl, content);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            _logger.LogError("Deepgram error: {Error}", error);
            return "No se pudo transcribir el audio.";
        }

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        try
        {
            var transcript = doc.RootElement
                .GetProperty("results")
                .GetProperty("channels")[0]
                .GetProperty("alternatives")[0]
                .GetProperty("transcript")
                .GetString();

            return transcript ?? "";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al parsear transcripción Deepgram");
            return "";
        }
    }
    
    public async Task<string> TranscribeFromStreamAsync(Stream audioStream)
    {
        var requestUrl = "https://api.deepgram.com/v1/listen?language=es&punctuate=true";

        var content = new StreamContent(audioStream);
        content.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg"); // Asegúrate de usar el tipo correcto

        var response = await _httpClient.PostAsync(requestUrl, content);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            _logger.LogError("Deepgram error: {Error}", error);
            return "No se pudo transcribir el audio.";
        }

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        try
        {
            var transcript = doc.RootElement
                .GetProperty("results")
                .GetProperty("channels")[0]
                .GetProperty("alternatives")[0]
                .GetProperty("transcript")
                .GetString();

            return transcript ?? "";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al parsear transcripción Deepgram");
            return "";
        }
    }
}