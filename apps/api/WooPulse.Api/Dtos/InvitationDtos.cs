namespace WooPulse.Api.Dtos;

// role: 2 Admin, 3 Member (we'll validate; no inviting Owner via API)
public record InviteRequestDto(string Email, int Role);

public record InvitationDto(
    int Id,
    int StoreId,
    string StoreName,   // added DEVLOG #008 so the UI can show the store's name
    string Email,
    string Role,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ExpiresAtUtc
);

public record AcceptInviteRequestDto(string Token);