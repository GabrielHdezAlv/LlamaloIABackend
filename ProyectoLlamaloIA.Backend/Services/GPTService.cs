using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ProyectoLlamaloIA.Backend.Services;

public class GPTService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GPTService> _logger;
    private readonly string _apiKey;
    
    public GPTService(IConfiguration config, ILogger<GPTService> logger)
    {
        _apiKey = config["OpenAI:ApiKey"] ?? throw new Exception("Falta OpenAI:ApiKey en configuración");
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        _logger = logger;
    }
    
    public async IAsyncEnumerable<string> GetReplyStream(string userInput)
    {
        var requestUrl = "https://api.openai.com/v1/chat/completions";

        var requestBody = new
        {
            model = "ft:gpt-3.5-turbo-1106:llamalo:llamalo-ai-beta-v1:BJ0wvqjK",
            messages = new[]
            {
                new { role = "user", content = userInput }
            },
            temperature = 0.7,
            stream = true
        };

        var jsonContent = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json"
        );

        var request = new HttpRequestMessage(HttpMethod.Post, requestUrl)
        {
            Content = jsonContent
        };

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();


        await using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: ")) continue;

            var jsonLine = line.Substring("data: ".Length);
            if (jsonLine == "[DONE]") yield break;

            using var doc = JsonDocument.Parse(jsonLine);
            var content = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("delta")
                .GetProperty("content")
                .GetString();

            if (!string.IsNullOrEmpty(content))
                yield return content;
        }
    }


    
    public async Task<string> GetReply(string userInput)
    {
        var requestUrl = "https://api.openai.com/v1/chat/completions";

        var requestBody = new
        {
            model = "ft:gpt-3.5-turbo-1106:llamalo:llamalo-ai-beta-v1:BJ0wvqjK",
            messages = new[]
            {
                new { role = "user", content = userInput }
            },
            temperature = 0.7
        };

        var jsonContent = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json"
        );

        var response = await _httpClient.PostAsync(requestUrl, jsonContent);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            _logger.LogError("Error en GPT: {Error}", error);
            return "Lo siento, no he podido generar una respuesta ahora mismo.";
        }

        var responseJson = await response.Content.ReadAsStringAsync();

        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            var reply = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            return reply?.Trim() ?? "";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al procesar respuesta de GPT");
            return "Disculpa, no entendí bien tu pregunta.";
        }
    }
}