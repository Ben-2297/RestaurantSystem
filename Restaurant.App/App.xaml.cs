namespace Restaurant.App;

public partial class App : Application
{
    public const string CheckoutReturnSessionIdKey = "CheckoutReturnSessionId";
    public const string CheckoutReturnStatusKey = "CheckoutReturnStatus";

    public App()
    {
        InitializeComponent();
    }

    protected override void OnAppLinkRequestReceived(Uri uri)
    {
        base.OnAppLinkRequestReceived(uri);

        if (!string.Equals(uri.Scheme, "restaurantapp", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.Host, "payments", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var path = uri.AbsolutePath?.Trim('/').ToLowerInvariant() ?? string.Empty;
        var sessionId = GetQueryParameter(uri, "session_id");

        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            Preferences.Default.Set(CheckoutReturnSessionIdKey, sessionId);
        }

        Preferences.Default.Set(CheckoutReturnStatusKey, path);
    }

    private static string? GetQueryParameter(Uri uri, string key)
    {
        var query = uri.Query.TrimStart('?');
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var split = part.Split('=', 2);
            if (split.Length == 0)
            {
                continue;
            }

            if (!string.Equals(Uri.UnescapeDataString(split[0]), key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return split.Length > 1 ? Uri.UnescapeDataString(split[1]) : string.Empty;
        }

        return null;
    }

    // Notice the '?' right after Window here:
    protected override Window CreateWindow(IActivationState? activationState)
    {
        // ALWAYS boot directly into the AppShell tab structure first
        return new Window(new AppShell());
    }
}