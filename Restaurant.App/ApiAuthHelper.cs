using System.Net.Http.Headers;

namespace Restaurant.App;

public static class ApiAuthHelper
{
    public static void ApplyAuthHeader(HttpClient client)
    {
        var token = Preferences.Default.Get("AuthToken", string.Empty);

        if (string.IsNullOrWhiteSpace(token))
        {
            client.DefaultRequestHeaders.Authorization = null;
            return;
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }
}
