namespace WooPulse.Api.Dtos;

// role: 2 Admin, 3 Member (we'll validate; no inviting Owner via API)
public record InviteRequestDto(string Email, int Role);

public record InvitationDto(
    int Id,
    int StoreId,
    string Email,
    int Role,
    int Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ExpiresAtUtc
);

public record AcceptInviteRequestDto(string Token);