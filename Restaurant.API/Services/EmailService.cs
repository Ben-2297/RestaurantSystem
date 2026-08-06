using SendGrid;
using SendGrid.Helpers.Mail;

namespace Restaurant.API.Services;

public interface IEmailService
{
    Task SendVerificationEmailAsync(string targetEmail, string userDisplayName, string verificationLink);
}

public class EmailService : IEmailService
{
    private readonly IConfiguration _config;

    public EmailService(IConfiguration config)
    {
        _config = config;
    }

    public async Task SendVerificationEmailAsync(string targetEmail, string userDisplayName, string verificationLink)
    {
        string apiKey = _config["SendGrid:ApiKey"] ?? throw new ArgumentNullException("SendGrid ApiKey is missing.");
        string fromEmail = _config["SendGrid:FromEmail"] ?? "noreply@app.com";
        string fromName = _config["SendGrid:FromName"] ?? "Restaurant App";

        var client = new SendGridClient(apiKey);
        var from = new EmailAddress(fromEmail, fromName);
        var to = new EmailAddress(targetEmail, userDisplayName);
        
        string subject = "Verify Your Account - Sariling Atin";
        
        // Plain text fallback
        string plainTextContent = $"Hello {userDisplayName},\n\nPlease verify your account by clicking this link: {verificationLink}";
        
        // Rich HTML email template matching your app colors
        string htmlContent = $@"
            <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; border: 1px solid #eee; padding: 20px; border-radius: 12px;'>
                <h2 style='color: #D81B60;'>Mabuhay, {userDisplayName}!</h2>
                <p>Thank you for registering with Sariling Atin. Please verify your email address to unlock account features, track delivery orders, and manage your profile settings.</p>
                <div style='text-align: center; margin: 30px 0;'>
                    <a href='{verificationLink}' style='background-color: #D81B60; color: white; padding: 12px 24px; text-decoration: none; border-radius: 25px; font-weight: bold; display: inline-block;'>Verify Account</a>
                </div>
                <p style='color: #666; font-size: 12px;'>If you didn't request this email, you can safely ignore it.</p>
            </div>";

        var msg = MailHelper.CreateSingleEmail(from, to, subject, plainTextContent, htmlContent);
        
        var response = await client.SendEmailAsync(msg);
        
        if (!response.IsSuccessStatusCode)
        {    
            var body = await response.Body.ReadAsStringAsync();
            throw new Exception($"Failed to dispatch verification email. SendGrid Status: {response.StatusCode}. Details: {body}");
        }
    }
}