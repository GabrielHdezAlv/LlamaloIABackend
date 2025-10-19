namespace ProyectoLlamaloIA.Backend.Models;

public record VoiceQualityAggDto(
    double TotalMinutes,
    double TimeSavedMinutes,
    double AvgLatencyMs,
    double AvgCallDurationSec,
    VoiceTypesDto Types,
    IEnumerable<CountryShareDto> Countries
);

