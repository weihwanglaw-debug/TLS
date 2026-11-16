using Microsoft.AspNetCore.DataProtection;

namespace LiveScore.Api;

public sealed class TrsConnectionSettings
{
    public string ApiBaseUrl { get; set; } = "";
    public string ProtectedCredential { get; set; } = "";
}

public sealed record TrsConnectionRequest(long Revision, string Field, string Value);

public sealed class TrsConnection(SetupStore store, IConfiguration configuration, IDataProtectionProvider protection)
{
    private readonly IDataProtector protector = protection.CreateProtector("TLS.TRS.Connection.v1");

    public (string ApiBaseUrl, string ApiKey) Read()
    {
        var saved = store.Read().Venue.TrsConnection;
        return saved is null
            ? (configuration["Trs:ApiBaseUrl"] ?? "", configuration["Trs:ApiKey"] ?? "")
            : (saved.ApiBaseUrl, saved.ProtectedCredential.Length == 0 ? "" : protector.Unprotect(saved.ProtectedCredential));
    }

    public object View()
    {
        var s = store.Read();
        var saved = s.Venue.TrsConnection;
        return new { s.Revision, apiBaseUrl = saved?.ApiBaseUrl ?? configuration["Trs:ApiBaseUrl"] ?? "",
            apiKey = saved is null ? configuration["Trs:ApiKey"] ?? "" : saved.ProtectedCredential.Length == 0 ? "" : protector.Unprotect(saved.ProtectedCredential) };
    }

    public object Save(TrsConnectionRequest request)
    {
        TournamentRules.Need(request.Field is "apiBaseUrl" or "apiKey" && request.Value is not null, "Choose a valid TRS connection setting.");
        var value = request.Value.Trim();
        if (request.Field == "apiBaseUrl")
            TournamentRules.Need(value.Length == 0 || value.Length <= 2000 && Uri.TryCreate(value, UriKind.Absolute, out var uri)
                && uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment), "Enter a valid TRS API URL.");
        else
            TournamentRules.Need(value.Length == 0 || value.Length is >= 32 and <= 512 && value.All(c => c is >= '!' and <= '~'), "Use an integration key of 32–512 characters without spaces.");
        store.Change(request.Revision, "TRS_CONNECTION_SETTINGS", s => {
            var old = s.Venue.TrsConnection ?? new TrsConnectionSettings { ApiBaseUrl = configuration["Trs:ApiBaseUrl"] ?? "",
                ProtectedCredential = string.IsNullOrEmpty(configuration["Trs:ApiKey"]) ? "" : protector.Protect(configuration["Trs:ApiKey"]!) };
            if (request.Field == "apiBaseUrl") old.ApiBaseUrl = value.TrimEnd('/');
            else if (value.Length > 0) old.ProtectedCredential = protector.Protect(value);
            s.Venue.TrsConnection = old;
        }, auditView: s => new { apiBaseUrl = s.Venue.TrsConnection?.ApiBaseUrl ?? configuration["Trs:ApiBaseUrl"] ?? "",
            hasCredential = s.Venue.TrsConnection is { ProtectedCredential.Length: > 0 } || s.Venue.TrsConnection is null && !string.IsNullOrEmpty(configuration["Trs:ApiKey"]) });
        return View();
    }
}
