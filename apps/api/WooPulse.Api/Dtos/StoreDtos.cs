namespace WooPulse.Api.Dtos;

public record StoreDto(
    int Id,
    string Name,
    string OwnerSub,

    DateTimeOffset CreatedAtUtc,
    string CreatedBySub,

    DateTimeOffset? UpdatedAtUtc,
    string? UpdatedBySub,

    bool IsDeleted,
    DateTimeOffset? DeletedAtUtc,
    string? DeletedBySub,

    // The CALLER's role in this store: "Owner" | "Admin" | "Member" (DEVLOG #011).
    // Lets the UI decide which buttons to show without asking for the member list.
    string MyRole
);

public record CreateStoreRequestDto(string Name);
public record RenameStoreRequestDto(string Name);