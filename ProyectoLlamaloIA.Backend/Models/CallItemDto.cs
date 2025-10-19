namespace ProyectoLlamaloIA.Backend.Models;

public record CallItemDto(
    string Sid,
    DateTime StartTimeUtc,
    DateTime? EndTimeUtc,
    string Status,
    string Direction,
    int? DurationSec,
    double? LatencyMs,
    decimal? Price,
    IEnumerable<CallRecordingDto> Recordings
);

