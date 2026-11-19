using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using WooPulse.Api.Data;
using WooPulse.Api.Dtos;
using WooPulse.Api.Integrations.WooCommerce;

namespace WooPulse.Api.Services;

/// <summary>
/// What: business rules for a store's WooCommerce connection.
///
/// Permissions (owner decision, DEVLOG #011):
///   view status      → any member (no secrets are ever returned)
///   save / change    → Owner only (it holds API keys)
///   test again       → Owner or Admin
///   disconnect       → Owner only
///
/// ENCRYPTION: ASP.NET Core Data Protection.
///   _protector.Protect("cs_abc")   → "CfDJ8...long base64..."  (stored in the DB)
///   _protector.Unprotect("CfDJ8…") → "cs_abc"                  (only in memory, when needed)
/// It uses authenticated encryption (AES-256-CBC + HMAC-SHA256) with keys that it creates,
/// stores and rotates itself (see Setup/WooCommerceSetup.cs for where they live).
/// The "purpose" string isolates these secrets: a protector made with a different purpose
/// can't decrypt them, even with the same keys.
/// </summary>
public class ConnectionService
{
    private readonly AppDbContext _db;
    private readonly WooCommerceClient _woo;
    private readonly IDataProtector _protector;

    public ConnectionService(AppDbContext db, WooCommerceClient woo, IDataProtectionProvider dataProtection)
    {
        _db = db;
        _woo = woo;
        _protector = dataProtection.CreateProtector("WooPulse.StoreConnection.v1");
    }

    private static StoreConnectionDto ToDto(StoreConnection c) => new(
        c.ShopUrl, c.KeyHint, c.AuthMode.ToString(), c.CreatedAtUtc,
        c.LastTestedAtUtc, c.LastTestOk, c.LastTestMessage);

    /// <summary>
    /// "https://Shop.com/" → "https://Shop.com". Null if not an absolute HTTPS URL.
    /// HTTPS is required: the keys travel in every request (devlog #006).
    /// Subfolder installs (https://site.com/shop) keep their path.
    /// </summary>
    internal static string? NormalizeShopUrl(string? input)
    {
        if (!Uri.TryCreate(input?.Trim(), UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttps) return null;
        // GetLeftPart(Path) drops any ?query or #fragment the user pasted.
        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    public async Task<(StoreConnectionDto? dto, IResult? error)> GetAsync(string sub, int storeId)
    {
        var me = await _db.FindMembershipAsync(storeId, sub);
        if (me is null) return (null, Results.NotFound());

        var c = await _db.StoreConnections.SingleOrDefaultAsync(x => x.StoreId == storeId);
        return (c is null ? null : ToDto(c), null);   // (null, null) = member, but not connected yet
    }

    /// <summary>
    /// Save (or replace) the connection. The credentials are TESTED FIRST and only saved
    /// if WooCommerce accepts them, so a saved connection is always one that worked.
    /// </summary>
    public async Task<(StoreConnectionDto? dto, IResult? error)> SaveAsync(string sub, int storeId, SaveConnectionRequestDto req)
    {
        var me = await _db.FindMembershipAsync(storeId, sub);
        if (me is null) return (null, Results.NotFound());
        if (me.Role != StoreRole.Owner)
            return (null, Results.Problem("Owner only", "Only the store owner can change the WooCommerce connection.", statusCode: 403));

        var shopUrl = NormalizeShopUrl(req.ShopUrl);
        if (shopUrl is null)
            return (null, Results.Problem("Invalid shop URL", "Use the full address starting with https://, e.g. https://myshop.com", statusCode: 400));

        var key = (req.ConsumerKey ?? "").Trim();
        var secret = (req.ConsumerSecret ?? "").Trim();
        if (!key.StartsWith("ck_") || !secret.StartsWith("cs_"))
            return (null, Results.Problem("Invalid API key",
                "The consumer key starts with \"ck_\" and the secret with \"cs_\" (WooCommerce → Settings → Advanced → REST API).",
                statusCode: 400));

        var test = await _woo.TestAsync(shopUrl, key, secret);
        if (!test.Ok)
            return (null, Results.Problem("Connection failed", test.Message, statusCode: 400));

        var now = DateTimeOffset.UtcNow;
        var c = await _db.StoreConnections.SingleOrDefaultAsync(x => x.StoreId == storeId);
        if (c is null)
        {
            c = new StoreConnection { StoreId = storeId, CreatedAtUtc = now, CreatedBySub = sub };
            _db.StoreConnections.Add(c);
        }
        else
        {
            c.UpdatedAtUtc = now;
            c.UpdatedBySub = sub;
        }

        c.ShopUrl = shopUrl;
        c.ConsumerKeyProtected = _protector.Protect(key);
        c.ConsumerSecretProtected = _protector.Protect(secret);
        c.KeyHint = $"ck_…{key[^4..]}";   // key[^4..] = range syntax: the last 4 characters
        c.AuthMode = test.AuthMode!.Value;
        c.LastTestedAtUtc = now;
        c.LastTestOk = true;
        c.LastTestMessage = test.Message;

        await _db.SaveChangesAsync();
        return (ToDto(c), null);
    }

    /// <summary>Re-test the saved credentials (e.g. after changing something in WordPress).</summary>
    public async Task<(StoreConnectionDto? dto, IResult? error)> TestAsync(string sub, int storeId)
    {
        var me = await _db.FindMembershipAsync(storeId, sub);
        if (me is null) return (null, Results.NotFound());
        if (me.Role is not (StoreRole.Owner or StoreRole.Admin))
            return (null, Results.Problem("Not allowed", "Only owners and admins can test the connection.", statusCode: 403));

        var c = await _db.StoreConnections.SingleOrDefaultAsync(x => x.StoreId == storeId);
        if (c is null)
            return (null, Results.Problem("Not connected", "This store isn't connected to WooCommerce yet.", statusCode: 409));

        var creds = TryUnprotect(c);
        if (creds is null)
            return (null, KeysUnreadable());

        var test = await _woo.TestAsync(c.ShopUrl, creds.ConsumerKey, creds.ConsumerSecret);
        c.LastTestedAtUtc = DateTimeOffset.UtcNow;
        c.LastTestOk = test.Ok;
        c.LastTestMessage = test.Message;
        if (test.AuthMode is not null) c.AuthMode = test.AuthMode.Value;   // the server's behavior may have changed

        await _db.SaveChangesAsync();
        return (ToDto(c), null);
    }

    public async Task<IResult?> DeleteAsync(string sub, int storeId)
    {
        var me = await _db.FindMembershipAsync(storeId, sub);
        if (me is null) return Results.NotFound();
        if (me.Role != StoreRole.Owner)
            return Results.Problem("Owner only", "Only the store owner can disconnect WooCommerce.", statusCode: 403);

        var c = await _db.StoreConnections.SingleOrDefaultAsync(x => x.StoreId == storeId);
        if (c is null) return Results.NotFound();

        _db.StoreConnections.Remove(c);
        await _db.SaveChangesAsync();
        return null;
    }

    /// <summary>
    /// Decrypt saved credentials. Returns null if they can't be decrypted, which happens
    /// if the Data Protection key folder was deleted or the app moved to another machine.
    /// Will be used by the order sync (Step 2B) too.
    /// </summary>
    internal WooCredentials? TryUnprotect(StoreConnection c)
    {
        try
        {
            return new WooCredentials(
                c.ShopUrl,
                _protector.Unprotect(c.ConsumerKeyProtected),
                _protector.Unprotect(c.ConsumerSecretProtected),
                c.AuthMode);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private static IResult KeysUnreadable() => Results.Problem(
        "Saved keys unreadable",
        "The saved API key can't be decrypted (the encryption keys changed). Ask the owner to enter it again.",
        statusCode: 409);
}
