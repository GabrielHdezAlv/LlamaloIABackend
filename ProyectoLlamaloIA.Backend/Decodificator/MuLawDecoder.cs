using System.IO;
using NAudio.Wave;

namespace ProyectoLlamaloIA.Backend.Decodificator;

public static class MuLawDecoder
{
    private const int BIAS = 0x84;
    private const int MAX = 32635;

    private static readonly short[] muLawDecompressTable = new short[256];

    static MuLawDecoder()
    {
        for (int i = 0; i < 256; i++)
            muLawDecompressTable[i] = DecodeMulawByte((byte)i);
    }

    // Decodifica µ-law a PCM16 8kHz
    private static short DecodeMulawByte(byte mulaw)
    {
        mulaw = (byte)~mulaw;
        int sign = (mulaw & 0x80);
        int exponent = (mulaw & 0x70) >> 4;
        int mantissa = (mulaw & 0x0F);
        int sample = ((mantissa << 4) + 8) << (exponent + 3);
        if (sign != 0)
            sample = -sample;
        return (short)sample;
    }

    // Convierte el array de µ-law a PCM16 y opcionalmente resamplea a 16kHz
    public static byte[] DecodeAndResampleTo16kHz(byte[] mulawData)
    {
        // Paso 1: decodificar a PCM16 8kHz
        var pcm16_8k = new byte[mulawData.Length * 2];
        for (int i = 0; i < mulawData.Length; i++)
        {
            short sample = muLawDecompressTable[mulawData[i]];
            pcm16_8k[i * 2] = (byte)(sample & 0xFF);
            pcm16_8k[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
        }

        // Paso 2: resample 8kHz -> 16kHz usando NAudio
        using var ms = new MemoryStream(pcm16_8k);
        using var reader = new RawSourceWaveStream(ms, new WaveFormat(8000, 16, 1));
        using var resampler = new MediaFoundationResampler(reader, new WaveFormat(16000, 16, 1));
        using var outStream = new MemoryStream();
        var buffer = new byte[1024];
        int read;
        while ((read = resampler.Read(buffer, 0, buffer.Length)) > 0)
        {
            outStream.Write(buffer, 0, read);
        }

        return outStream.ToArray(); // PCM16 a 16kHz listo para Deepgram
    }
}