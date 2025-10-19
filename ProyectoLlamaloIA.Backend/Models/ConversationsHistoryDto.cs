namespace ProyectoLlamaloIA.Backend.Models;

public record ConversationsHistoryDto(
    string Caller,
    IEnumerable<CallItemDto> Calls
);

