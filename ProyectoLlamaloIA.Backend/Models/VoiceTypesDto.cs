namespace ProyectoLlamaloIA.Backend.Models;

public record VoiceTypesDto(
    double InboundPercent,
    double OutboundPercent,
    int InboundCount,
    int OutboundCount
);