using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Authorization;

namespace Restaurant.Client.Services;

public sealed class CustomAuthStateProvider : AuthenticationStateProvider
{
    private static readonly ClaimsPrincipal Anonymous = new(new ClaimsIdentity());
    private readonly ILocalStorageService _localStorage;
    private readonly HttpClient _httpClient;

    public CustomAuthStateProvider(ILocalStorageService localStorage, HttpClient httpClient)
    {
        _localStorage = localStorage;
        _httpClient = httpClient;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var token = await _localStorage.GetItemAsync<string>("authToken");
        SetHttpClientAuthorization(token);

        if (string.IsNullOrWhiteSpace(token))
        {
            return new AuthenticationState(Anonymous);
        }

        var claims = ParseClaimsFromJwt(token);
        var identity = new ClaimsIdentity(claims, authenticationType: "jwt");
        return new AuthenticationState(new ClaimsPrincipal(identity));
    }

    public void MarkUserAsAuthenticated(string token)
    {
        SetHttpClientAuthorization(token);

        var claims = ParseClaimsFromJwt(token);
        var identity = new ClaimsIdentity(claims, authenticationType: "jwt");
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity))));
    }

    public void MarkUserAsLoggedOut()
    {
        SetHttpClientAuthorization(null);
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(Anonymous)));
    }

    private void SetHttpClientAuthorization(string? token)
    {
        _httpClient.DefaultRequestHeaders.Authorization =
            string.IsNullOrWhiteSpace(token)
                ? null
                : new AuthenticationHeaderValue("Bearer", token);
    }

    private static IEnumerable<Claim> ParseClaimsFromJwt(string jwt)
    {
        try
        {
            var payload = jwt.Split('.')[1];
            var jsonBytes = DecodeBase64WithoutPadding(payload);
            var keyValuePairs = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonBytes);

            if (keyValuePairs is null)
            {
                return Array.Empty<Claim>();
            }

            return keyValuePairs.SelectMany(entry =>
            {
                var claimType = entry.Key switch
                {
                    "role" => ClaimTypes.Role,
                    "roles" => ClaimTypes.Role,
                    "http://schemas.microsoft.com/ws/2008/06/identity/claims/role" => ClaimTypes.Role,
                    "name" => ClaimTypes.Name,
                    "unique_name" => ClaimTypes.Name,
                    _ => entry.Key
                };

                if (entry.Value.ValueKind == JsonValueKind.Array)
                {
                    return entry.Value
                        .EnumerateArray()
                        .Select(v => new Claim(claimType, v.ToString()));
                }

                return new[] { new Claim(claimType, entry.Value.ToString()) };
            });
        }
        catch
        {
            return Array.Empty<Claim>();
        }
    }

    private static byte[] DecodeBase64WithoutPadding(string base64)
    {
        var output = base64.Replace('-', '+').Replace('_', '/');
        switch (output.Length % 4)
        {
            case 2:
                output += "==";
                break;
            case 3:
                output += "=";
                break;
        }

        return Convert.FromBase64String(output);
    }
}
