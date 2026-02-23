namespace WooPulse.Api.Validation;

public static class StoreValidation
{
    public const int NameMin = 2;
    public const int NameMax = 60;

    public static string? NormalizeName(string? name)
        => string.IsNullOrWhiteSpace(name) ? null : name.Trim();

    public static bool IsValidName(string name)
        => name.Length >= NameMin && name.Length <= NameMax;
}