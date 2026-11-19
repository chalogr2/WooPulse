namespace WooPulse.Api.Dtos;

public record InvitationDebugDto(
    int Id,
    int StoreId,
    string StoreName,   // added DEVLOG #008
    string Email,
    string Role,
    string Status,
    string Token,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ExpiresAtUtc
);