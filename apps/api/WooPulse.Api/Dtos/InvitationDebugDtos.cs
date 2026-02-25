namespace WooPulse.Api.Dtos;

public record InvitationDebugDto(
    int Id,
    int StoreId,
    string Email,
    int Role,
    int Status,
    string Token,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ExpiresAtUtc
);