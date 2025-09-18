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

    public MediaStreamController(DeepgramService deepgram)
    {
        _deepgram = deepgram;
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

        // Abrir sesión de streaming con Deepgram
        await using var session = await _deepgram.OpenStreamingSessionAsync((text, isFinal) =>
        {
            Console.WriteLine($"🎤 {text} (final={isFinal})");
        });

        var buffer = new byte[8192];
        var sb = new StringBuilder();

        try
        {
            while (ws.State == WebSocketState.Open)
            {
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                if (result.MessageType == WebSocketMessageType.Close)
                    break;

                sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                if (result.EndOfMessage)
                {
                    try
                    {
                        var json = sb.ToString();
                        sb.Clear();

                        var msg = JsonSerializer.Deserialize<TwilioMediaEvent>(json);

                        if (msg?.Event == "media" && msg.Media != null)
                        {
                            // Usar la propiedad PayloadBytes directamente
                            var pcm16_16k = MuLawDecoder.DecodeAndResampleTo16kHz(msg.Media.PayloadBytes);

                            await session.SendAudioAsync(pcm16_16k);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"⚠️ Error procesando evento Twilio: {ex.Message}");
                        sb.Clear(); // limpiar buffer para siguiente mensaje
                    }
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

// Ajuste en MediaPayload
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