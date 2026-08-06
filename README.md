# .NetBlazorCuisine
# JWT Authentication, RESTFUL Api (EF Core), SignalR, C# .Net10 Development

Capstone Project for Inventory, Order, Product Management

## Runtime Configuration Checklist

This solution uses `Restaurant.API` as the backend service. The API reads configuration from `appsettings.json`, optional `appsettings.Development.json`, and environment variables.

### Required configuration values

- `ConnectionStrings:DefaultConnection`
  - SQL Server connection string used by Entity Framework Core.
- `AppSettings:Token`
  - JWT signing key used for authentication.
- `SendGrid:ApiKey`
  - SendGrid API key used by `Restaurant.API/Services/EmailService.cs`.
- `SendGrid:FromEmail`
  - Sender email address for outgoing notifications.
- `SendGrid:FromName`
  - Sender display name.
- `Stripe:SecretKey`
  - Stripe secret key used for payment operations and checkout creation.
- `Stripe:PublishableKey`
  - Stripe public key returned to clients.
- `Stripe:WebhookSecret`
  - Stripe webhook signing secret for validating incoming webhook events.
- `Stripe:CheckoutSuccessUrl`
  - Redirect URL used by Stripe after successful checkout.
- `Stripe:CheckoutCancelUrl`
  - Redirect URL used by Stripe when checkout is canceled.
- `Gemini:ApiKey` or `GEMINI_API_KEY`
  - Gemini API key used by `Restaurant.API/Services/GeminiAdminInsightsService.cs`.
- `GoogleChat:ClientId`, `GoogleChat:ClientSecret`, `GoogleChat:RefreshToken`
  - Google Chat integration values used by `Restaurant.API/Services/GoogleChatService.cs`.
- `Authentication:Google:ClientId` and `Authentication:Google:ClientSecret`
  - Alternative fallback keys for the same Google authentication flow.

### Verification steps

1. Confirm `Restaurant.API/Program.cs` is loading the expected configuration values.
2. Verify `StripeConfiguration.ApiKey` is set from `Stripe:SecretKey` at startup.
3. Check that `GeminiAdminInsightsService` can be enabled by setting either `GEMINI_API_KEY` or `Gemini:ApiKey`.
4. Make sure the database connection string is correct and the SQL Server instance is reachable.
5. When running locally, use `appsettings.Development.json` or environment variables to avoid committing secrets in source control.

### Important notes

- The backend is configured to bind to `http://0.0.0.0:5123` in `Restaurant.API/Program.cs`.
- Environment variables take precedence over values in `appsettings.json`.
- Do not commit production secret keys to the repository. Use local secrets, environment variables, or a secure vault for deployment.

