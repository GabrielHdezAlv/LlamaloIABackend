using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc;

namespace ProyectoLlamaloIA.Backend.Controllers;

[ApiController]
[Route("api/elevenLabs")]
public class ElevenLabsController : ControllerBase
{
    private readonly HttpClient _httpClient;
    private const string ApiKey = "sk_0f16002177bf557f9617b0dfc6ea0f5d23571031949145a5";
    private const string VoiceId = "zRUArUmK0DWSP7K6mmLW";

    public ElevenLabsController(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient();
    }

    public class TextoRequest
    {
        public string Texto { get; set; }
    }

    [HttpPost("generar-audio")]
    public async Task<IActionResult> GenerarAudio([FromBody] TextoRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Texto))
        {
            return BadRequest("Texto es requerido.");
        }

        var endpoint = $"https://api.elevenlabs.io/v1/text-to-speech/{VoiceId}";

        var payload = new
        {
            text = request.Texto,
            model_id = "eleven_multilingual_v2",
            voice_settings = new
            {
                stability = 0.5,
                similarity_boost = 0.8
            }
        };

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json")
        };

        httpRequest.Headers.Add("xi-api-key", ApiKey);
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("audio/mpeg"));

        var response = await _httpClient.SendAsync(httpRequest);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            return StatusCode((int)response.StatusCode, $"Error de ElevenLabs: {error}");
        }

        var audioStream = await response.Content.ReadAsStreamAsync();

        return File(audioStream, "audio/mpeg", "voz.mp3");
    }
}