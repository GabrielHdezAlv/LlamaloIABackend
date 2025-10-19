namespace ProyectoLlamaloIA.Backend.Models;

public record VoiceSummaryDto(
    int TotalCalls,
    double MinutesUsed,
    double TimeSavedMinutes,
    int Last7dCalls
);