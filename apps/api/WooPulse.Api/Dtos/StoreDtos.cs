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
    string? DeletedBySub
);

public record CreateStoreRequestDto(string Name);
public record RenameStoreRequestDto(string Name);