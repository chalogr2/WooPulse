namespace WooPulse.Api.Dtos;

// One row of a store's member list.
// IsMe lets the UI show "Leave store" on your own row and hide "Remove" on it.
public record MemberDto(
    int Id,
    string UserSub,
    string? Email,          // null for members who haven't logged in since DEVLOG #009
    string Role,            // "Owner" | "Admin" | "Member"
    DateTimeOffset JoinedAtUtc,
    bool IsMe
);

// Body of PUT /stores/{storeId}/members/{memberId}. Role: 2 = Admin, 3 = Member.
public record ChangeRoleRequestDto(int Role);
