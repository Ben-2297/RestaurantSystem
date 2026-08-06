using System.Text;
using System.Text.Json;

namespace Restaurant.App;

public partial class LoginPage : ContentPage
{
    private static readonly HttpClient HttpClient = new HttpClient();

    public LoginPage()
    {
        InitializeComponent();
    }

    // FIXED WARNING CS8622: Signature updated to allow nullable event parameters for .NET 10
    private async void OnLoginClicked(object? sender, EventArgs? e)
    {
        // FIXED WARNING CS8600: Added explicit string fallback operators to avoid null handling complaints
        string email = EmailEntry.Text?.Trim() ?? string.Empty;
        string password = PasswordEntry.Text ?? string.Empty;

        // FIXED WARNING CS0618: Upgraded standard 'DisplayAlert' to the new .NET 10 'DisplayAlertAsync'
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            await this.DisplayAlertAsync("Validation", "Please fill in both Email and Password fields.", "OK");
            return;
        }

        try
        {
            LoginLoading.IsRunning = true;

            // Maps the email input to the 'Username' key required by the backend UserLoginDto contract
            var loginData = new 
            { 
                Username = email, 
                Password = password,
                ClientType = "Mobile" // Tells the updated backend API to run the strict "User" role enforcement block
            };
            
            string json = JsonSerializer.Serialize(loginData);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            // Post request to backend API controller layer
            HttpResponseMessage response = await HttpClient.PostAsync($"{ApiSettings.BaseUrl}/api/auth/login", content);

            if (response.IsSuccessStatusCode)
            {
                // Grabs the custom JSON string containing the token and new profile information
                string jsonResponse = await response.Content.ReadAsStringAsync();
                
                // Parse the data object model safely using JsonDocument property matching
                using var doc = JsonDocument.Parse(jsonResponse);
                var root = doc.RootElement;

                // Extract properties using case-insensitive or exact property names depending on serialization formats
                string jwtToken = root.TryGetProperty("token", out var tokenProp) ? tokenProp.GetString() ?? string.Empty : string.Empty;
                string fullName = root.TryGetProperty("fullName", out var nameProp) ? nameProp.GetString() ?? string.Empty : string.Empty;
                string address = root.TryGetProperty("address", out var addrProp) ? addrProp.GetString() ?? string.Empty : string.Empty;
                string phoneNumber = root.TryGetProperty("phoneNumber", out var phoneProp) ? phoneProp.GetString() ?? string.Empty : string.Empty;

                // ------------------- NEW FIX: Extract UserId -------------------
                int userId = 0;
                if (root.TryGetProperty("userId", out var idProp) || 
                    root.TryGetProperty("id", out idProp) || 
                    root.TryGetProperty("Id", out idProp))
                {
                    if (idProp.ValueKind == JsonValueKind.Number)
                        userId = idProp.GetInt32();
                }

                // ------------------- NEW FIX: Extract IsVerified -------------------
                bool isVerified = false;
                if (root.TryGetProperty("isVerified", out var verifiedProp) || 
                    root.TryGetProperty("IsVerified", out verifiedProp) ||
                    root.TryGetProperty("is_verified", out verifiedProp))
                {
                    if (verifiedProp.ValueKind == JsonValueKind.True)
                        isVerified = true;
                    else if (verifiedProp.ValueKind == JsonValueKind.Number)
                        isVerified = verifiedProp.GetInt32() == 1;
                }

                // Save all verified profile attributes persistently to the local device storage system
                Preferences.Default.Set("IsLoggedIn", true);
                Preferences.Default.Set("UserId", userId); // <--- ADDED
                Preferences.Default.Set("IsAccountVerified", isVerified); // <--- ADDED
                Preferences.Default.Set("UserEmail", email);
                Preferences.Default.Set("AuthToken", jwtToken); 
                Preferences.Default.Set("UserFullName", fullName);
                Preferences.Default.Set("UserAddress", address);
                Preferences.Default.Set("UserPhone", phoneNumber);

                // Greets the customer using their actual full name pulled from the database relation layout!
                await this.DisplayAlertAsync("Success", $"Welcome back, {fullName}!", "OK");

                // FIXED FOR .NET 10: Modifies the active target page context via Windows collection references
                if (Application.Current?.Windows.Count > 0)
                {
                    Application.Current.Windows[0].Page = new AppShell();
                }
            }
            else
            {
                // Grabs the custom string return message from your backend error handling blocks
                string errorReason = await response.Content.ReadAsStringAsync();
                await this.DisplayAlertAsync("Login Failed", string.IsNullOrWhiteSpace(errorReason) ? "Invalid credentials." : errorReason, "OK");
            }
        }
        catch (Exception ex)
        {
            // Tailscale routing diagnostics helper
            await this.DisplayAlertAsync("Connection Failure", 
                $"Could not reach API at {ApiSettings.BaseUrl}.\n\nDetails: {ex.Message}", "OK");
        }
        finally
        {
            LoginLoading.IsRunning = false;
        }
    }

    // FIXED: Routes your mobile view stack over to the new combined data input registration form
    private async void OnSignUpButtonClicked(object? sender, EventArgs? e)
    {
        await Navigation.PushAsync(new RegisterPage());
    }
}