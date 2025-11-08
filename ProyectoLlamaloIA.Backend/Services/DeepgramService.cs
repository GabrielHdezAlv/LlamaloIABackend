using System;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace ProyectoLlamaloIA.Backend.Services;

public class DeepgramStreamingSession : IAsyncDisposable
{
    private readonly ClientWebSocket _ws;
    private readonly Uri _uri = new("wss://api.deepgram.com/v1/listen?language=es&punctuate=true&interim_results=true");
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _listenerTask;

    public event Action<string, bool>? OnTranscript; // (texto, isFinal)

    public DeepgramStreamingSession(string apiKey)
    {
        _ws = new ClientWebSocket();
        _ws.Options.SetRequestHeader("Authorization", $"Token {apiKey}");
        _ws.ConnectAsync(_uri, CancellationToken.None).Wait();
        _listenerTask = Task.Run(ReceiveLoopAsync);
    }

    public async Task SendAudioAsync(byte[] audioData)
    {
        await _ws.SendAsync(
            new ArraySegment<byte>(audioData),
            WebSocketMessageType.Binary,
            endOfMessage: true,
            cancellationToken: _cts.Token
        );
    }

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[8192];
        while (!_cts.IsCancellationRequested)
        {
            var result = await _ws.ReceiveAsync(buffer, _cts.Token);
            if (result.MessageType == WebSocketMessageType.Close) break;

            var msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
            try
            {
                using var doc = JsonDocument.Parse(msg);
                var isFinal = doc.RootElement.GetProperty("is_final").GetBoolean();
                var transcript = doc.RootElement
                    .GetProperty("channel")
                    .GetProperty("alternatives")[0]
                    .GetProperty("transcript")
                    .GetString();

                if (!string.IsNullOrEmpty(transcript))
                    OnTranscript?.Invoke(transcript, isFinal);
            }
            catch
            {
                // Ignorar mensajes que no son transcripciones
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try
        {
            await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
        }
        catch { }
    }
}

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

    public async Task<DeepgramStreamingSession> OpenStreamingSessionAsync(Action<string, bool>? onTranscript = null)
    {
        var session = new DeepgramStreamingSession(_apiKey);
        if (onTranscript != null)
            session.OnTranscript += onTranscript;
        return session;
    }

    public async Task<string> TranscribeFromUrlAsync(string audioUrl)
    {
        var requestUrl = "https://api.deepgram.com/v1/listen?language=es&punctuate=true&detect_language=false";
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
            return doc.RootElement
                .GetProperty("results")
                .GetProperty("channels")[0]
                .GetProperty("alternatives")[0]
                .GetProperty("transcript")
                .GetString() ?? "";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al parsear transcripción Deepgram");
            return "";
        }
    }

    public async Task<string> TranscribeFromStreamAsync(Stream audioStream, string mediaType = "audio/wav")
    {
        var requestUrl = "https://api.deepgram.com/v1/listen?language=es&punctuate=true&detect_language=false";
        var content = new StreamContent(audioStream);
        if (string.Equals(mediaType, "audio/x-wav", StringComparison.OrdinalIgnoreCase))
            mediaType = "audio/wav";
        content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);

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
            return doc.RootElement
                .GetProperty("results")
                .GetProperty("channels")[0]
                .GetProperty("alternatives")[0]
                .GetProperty("transcript")
                .GetString() ?? "";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al parsear transcripción Deepgram");
            return "";
        }
    }
}
