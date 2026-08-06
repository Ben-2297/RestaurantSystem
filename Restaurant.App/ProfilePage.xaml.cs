using System.Text.Json;

namespace Restaurant.App;

public partial class ProfilePage : ContentPage
{
    private static readonly HttpClient HttpClient = new HttpClient();

    public ProfilePage()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // 1. Check user login authentication status
        bool isLoggedIn = Preferences.Default.Get("IsLoggedIn", false);

        if (!isLoggedIn)
        {
            await this.DisplayAlertAsync("Sign In Required", "Please log in to view and manage your profile details.", "OK");
            await Navigation.PushModalAsync(new NavigationPage(new LoginPage()));
            return;
        }

        // 2. User is authenticated -> Fetch details from local preferences
        NameLabel.Text = Preferences.Default.Get("UserFullName", "Guest User");
        EmailLabel.Text = Preferences.Default.Get("UserEmail", "No email linked");
        PhoneEntry.Text = Preferences.Default.Get("UserPhone", "None");
        AddressEntry.Text = Preferences.Default.Get("UserAddress", "None");

        // 3. Query live database status or fallback to local preferences
        int userId = Preferences.Default.Get("UserId", 0);
        bool isVerified = await FetchLiveVerificationStatusAsync(userId);

        // Update badge layout based on IsVerified (true = 1, false = 0)
        UpdateVerificationBadge(isVerified);
    }

    /// <summary>
    /// Attempts to query the live API for the latest user database fields
    /// </summary>
    private async Task<bool> FetchLiveVerificationStatusAsync(int userId)
{
    // 1. Fallback to local preference if userId wasn't saved/passed
    if (userId <= 0)
    {
        return Preferences.Default.Get("IsAccountVerified", false);
    }

    try
    {
        ApiAuthHelper.ApplyAuthHeader(HttpClient);
        HttpResponseMessage response = await HttpClient.GetAsync($"{ApiSettings.BaseUrl}/api/users/{userId}");
        if (response.IsSuccessStatusCode)
        {
            string json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            // 2. Case-insensitive property lookup (handles "isVerified", "IsVerified", "is_verified")
            if (doc.RootElement.TryGetProperty("isVerified", out var isVerifiedProp) ||
                doc.RootElement.TryGetProperty("IsVerified", out isVerifiedProp) ||
                doc.RootElement.TryGetProperty("is_verified", out isVerifiedProp))
            {
                bool verified = false;

                // 3. Robust parsing for both SQL bit/int (1/0) and JSON boolean (true/false)
                if (isVerifiedProp.ValueKind == JsonValueKind.True)
                {
                    verified = true;
                }
                else if (isVerifiedProp.ValueKind == JsonValueKind.False)
                {
                    verified = false;
                }
                else if (isVerifiedProp.ValueKind == JsonValueKind.Number)
                {
                    verified = isVerifiedProp.GetInt32() == 1;
                }

                // Cache updated status and return
                Preferences.Default.Set("IsAccountVerified", verified);
                return verified;
            }
        }
    }
    catch
    {
        // API connection offline or unavailable; fallback to saved preference
    }

    return Preferences.Default.Get("IsAccountVerified", false);
}

    /// <summary>
    /// Renders the status badge based on IsVerified boolean
    /// </summary>
    private void UpdateVerificationBadge(bool isVerified)
    {
        if (isVerified)
        {
            StatusBadge.BackgroundColor = Color.FromRgb(220, 245, 220); // Soft Green
            StatusLabel.TextColor = Color.FromRgb(30, 120, 30);
            StatusLabel.Text = "✓ Verified Account";
        }
        else
        {
            StatusBadge.BackgroundColor = Color.FromRgb(255, 230, 230); // Soft Red Alert
            StatusLabel.TextColor = Color.FromRgb(200, 40, 40);
            StatusLabel.Text = "⚠️ Unverified Account (Check Email)";
        }
    }

    private async void OnViewOrderHistoryClicked(object? sender, EventArgs? e)
    {
        await Navigation.PushAsync(new OrderHistoryPage());
    }

    private async void OnViewPaymentHistoryClicked(object? sender, EventArgs? e)
    {
        await Navigation.PushAsync(new PaymentHistoryPage());
    }

    private async void OnChangePasswordClicked(object? sender, EventArgs? e)
    {
        string oldPassword = OldPasswordEntry.Text ?? string.Empty;
        string newPassword = NewPasswordEntry.Text ?? string.Empty;

        if (string.IsNullOrWhiteSpace(oldPassword) || string.IsNullOrWhiteSpace(newPassword))
        {
            await this.DisplayAlertAsync("Validation", "Please fill out both password fields.", "OK");
            return;
        }

        await this.DisplayAlertAsync("Security Processing", "Password change transmission hook configured.", "OK");
        
        OldPasswordEntry.Text = string.Empty;
        NewPasswordEntry.Text = string.Empty;
    }

    /// <summary>
    /// Clears session data and returns the user back to the main view without forcing login
    /// </summary>
    private async void OnLogoutClicked(object? sender, EventArgs? e)
    {
        bool confirm = await this.DisplayAlertAsync("Log Out", "Are you sure you want to log out?", "Yes", "No");
        if (!confirm) return;

        // Clear user session state
        Preferences.Default.Set("IsLoggedIn", false);
        Preferences.Default.Remove("UserId");
        Preferences.Default.Remove("UserFullName");
        Preferences.Default.Remove("UserEmail");
        Preferences.Default.Remove("UserPhone");
        Preferences.Default.Remove("UserAddress");
        Preferences.Default.Remove("IsAccountVerified");

        MainPage.IsUserLoggedIn = false;

        // Navigate back to the home page
        await Navigation.PopToRootAsync();
    }
}