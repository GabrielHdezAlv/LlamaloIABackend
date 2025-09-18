using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using NAudio.Wave;
using ProyectoLlamaloIA.Backend.Decodificator;
using ProyectoLlamaloIA.Backend.Services;

namespace ProyectoLlamaloIA.Backend.Controllers;

[Route("voice/stream")]
[ApiController]
public class MediaStreamController : ControllerBase
{
    private readonly DeepgramService _deepgram;
    private readonly GPTService _gpt;
    private readonly ElevenLabsService _eleven;

    public MediaStreamController(DeepgramService deepgram, GPTService gpt, ElevenLabsService eleven)
    {
        _deepgram = deepgram;
        _gpt = gpt;
        _eleven = eleven;
    }

    [HttpGet]
    public async Task Get()
    {
        if (!HttpContext.WebSockets.IsWebSocketRequest)
        {
            HttpContext.Response.StatusCode = 400;
            return;
        }

        using var ws = await HttpContext.WebSockets.AcceptWebSocketAsync();

        // Abrimos sesión de streaming con Deepgram
        await using var session = await _deepgram.OpenStreamingSessionAsync(async (text, isFinal) =>
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            Console.WriteLine($"🎤 Transcripción parcial: {text} (final={isFinal})");

            // Debounce opcional: esperar a que sea final o unos ms para no saturar GPT
            if (!isFinal) return;

            // Llamada a GPT
            var reply = await _gpt.GetReply(text);

            // Generar audio y enviar a Twilio en chunks
            await foreach (var chunk in _eleven.GenerateAudioStreamChunks(reply))
            {
                if (ws.State != WebSocketState.Open) break;

                var base64 = Convert.ToBase64String(chunk);
                var payload = JsonSerializer.Serialize(new
                {
                    @event = "media",
                    media = new { payload = base64 }
                });
                var bytes = Encoding.UTF8.GetBytes(payload);
                await ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
            }
        });

        var buffer = new byte[8192];

        try
        {
            while (ws.State == WebSocketState.Open)
            {
                var result = await ws.ReceiveAsync(buffer, CancellationToken.None);

                if (result.MessageType == WebSocketMessageType.Close) break;

                // Procesar paquete Twilio
                var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                //Console.WriteLine($"📥 Mensaje recibido de Twilio: {json}");
                var msg = JsonSerializer.Deserialize<TwilioMediaEvent>(json);

                if (msg == null) continue;

                // 🔹 Manejar evento de inicio de stream
                if (msg.Event == "start")
                {
                    var connected = JsonSerializer.Serialize(new { @event = "connected" });
                    var bytes = Encoding.UTF8.GetBytes(connected);
                    await ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);

                    Console.WriteLine("✅ Respuesta 'connected' enviada a Twilio");
                }
                
                // 🔹 Manejar audio entrante
                if (msg.Event == "media" && msg.Media != null)
                {
                    var pcm16k = MuLawDecoder.DecodeAndResampleTo16kHz(msg.Media.PayloadBytes);

                    // Enviar a Deepgram para transcripción en tiempo real
                    await session.SendAudioAsync(pcm16k);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ WebSocket error: {ex.Message}");
        }
        finally
        {
            if (ws.State == WebSocketState.Open)
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None);
        }
    }
}

// Payload Twilio
public class TwilioMediaEvent
{
    public string Event { get; set; } = "";
    public MediaPayload? Media { get; set; }
}

public class MediaPayload
{
    public string Payload { get; set; } = "";
    public byte[] PayloadBytes => Convert.FromBase64String(Payload);
}
