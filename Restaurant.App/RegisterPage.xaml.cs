using System.Text;
using System.Text.Json;

namespace Restaurant.App;

public partial class RegisterPage : ContentPage
{
    private static readonly HttpClient HttpClient = new HttpClient();

    public RegisterPage()
    {
        InitializeComponent();
    }

    private async void OnRegisterClicked(object? sender, EventArgs? e)
    {
        string fullName = FullNameEntry.Text?.Trim() ?? string.Empty;
        string phone = PhoneEntry.Text?.Trim() ?? string.Empty;
        string address = AddressEntry.Text?.Trim() ?? string.Empty;
        string email = EmailEntry.Text?.Trim() ?? string.Empty;
        string password = PasswordEntry.Text ?? string.Empty;
        string confirmPassword = ConfirmPasswordEntry.Text ?? string.Empty;

        // 1. Check for empty fields
        if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(phone) || 
            string.IsNullOrWhiteSpace(address) || string.IsNullOrWhiteSpace(email) || 
            string.IsNullOrWhiteSpace(password))
        {
            await this.DisplayAlertAsync("Validation", "Please fill out all fields.", "OK");
            return;
        }

        // 2. Validate password match
        if (password != confirmPassword)
        {
            await this.DisplayAlertAsync("Validation", "Passwords do not match. Please re-enter.", "OK");
            return;
        }

        try
        {
            RegisterLoading.IsRunning = true;

            var registrationPayload = new 
            {
                Email = email,
                Password = password,
                FullName = fullName,
                Address = address,
                PhoneNumber = phone
            };

            string json = JsonSerializer.Serialize(registrationPayload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            HttpResponseMessage response = await HttpClient.PostAsync($"{ApiSettings.BaseUrl}/api/auth/register", content);

            if (response.IsSuccessStatusCode)
            {
                string jsonResponse = await response.Content.ReadAsStringAsync();
                
                using var doc = JsonDocument.Parse(jsonResponse);
                var root = doc.RootElement;

                string jwtToken = root.TryGetProperty("token", out var tokenProp) ? tokenProp.GetString() ?? string.Empty : string.Empty;
                string apiFullName = root.TryGetProperty("fullName", out var nameProp) ? nameProp.GetString() ?? string.Empty : string.Empty;
                string apiAddress = root.TryGetProperty("address", out var addrProp) ? addrProp.GetString() ?? string.Empty : string.Empty;
                string apiPhone = root.TryGetProperty("phoneNumber", out var phoneProp) ? phoneProp.GetString() ?? string.Empty : string.Empty;
                bool isVerified = root.TryGetProperty("isVerified", out var verifiedProp) && verifiedProp.GetBoolean();

                // Save variables to device storage
                Preferences.Default.Set("IsLoggedIn", true);
                Preferences.Default.Set("UserEmail", email);
                Preferences.Default.Set("AuthToken", jwtToken); 
                Preferences.Default.Set("UserFullName", apiFullName);
                Preferences.Default.Set("UserAddress", apiAddress);
                Preferences.Default.Set("UserPhone", apiPhone);
                Preferences.Default.Set("IsAccountVerified", isVerified);

                await this.DisplayAlertAsync("Welcome!", $"Account created successfully! Welcome, {apiFullName}.", "OK");
                
                // FIXED WARNING CS0618: Upgraded to .NET 10 Window active navigation mapping style
                if (Application.Current?.Windows.Count > 0)
                {
                    Application.Current.Windows[0].Page = new AppShell();
                }
            }
            else
            {
                string errorReason = await response.Content.ReadAsStringAsync();
                await this.DisplayAlertAsync("Registration Failed", errorReason, "OK");
            }
        }
        catch (Exception ex)
        {
            await this.DisplayAlertAsync("Connection Failure", $"Could not communicate with backend server.\n\nDetails: {ex.Message}", "OK");
        }
        finally
        {
            RegisterLoading.IsRunning = false;
        }
    }
}