using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Restaurant.API.Data;
using Scalar.AspNetCore;
using Restaurant.API.Models;
using Restaurant.API.Services;
using Stripe;

var builder = WebApplication.CreateBuilder(args);

// Tell Kestrel to bind to all network interfaces (0.0.0.0) so Tailscale traffic can reach it
builder.WebHost.UseUrls("http://0.0.0.0:5123");

StripeConfiguration.ApiKey = builder.Configuration["Stripe:SecretKey"];

// 1. Add DB Context Configuration
builder.Services.AddDbContext<DataContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// STEP 4 INJECTION: Register the implementation mapping for your active production SendGrid email service context
builder.Services.AddScoped<IEmailService, EmailService>();

// Add CORS REGISTRATION HERE (Updated to accept mobile traffic alongside Blazor)
builder.Services.AddCors(options =>
{
    options.AddPolicy("BlazorClientPolicy", policy =>
    {
        policy.WithOrigins("http://localhost:5113") // Matches your Blazor port
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
    
    // Fallback permissive policy to allow cross-network mobile emulator/device requests
    options.AddPolicy("AllowMobileDevices", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// 2. Add JWT Authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration.GetSection("AppSettings:Token").Value!)),
            ValidateIssuer = false,
            ValidateAudience = false
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddHttpClient();

// 3. Register Controller Services with Safe JSON Loop Handling Configuration
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Prevents 500 error cyclic infinite loops when serializing relational database items
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });

// Register OpenAPI/Swagger 
builder.Services.AddOpenApi();
builder.Services.AddScoped<IGoogleChatService, GoogleChatService>();
builder.Services.AddScoped<IGeminiAdminInsightsService, GeminiAdminInsightsService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

// ACTIVATE CORS MIDDLEWARE HERE (Using mobile configuration)
app.UseCors("AllowMobileDevices");

// // app.UseHttpsRedirection();

// 4. Authentication Middleware (Crucial Ordering!)
app.UseAuthentication(); 
app.UseAuthorization();

// 5. Map Controller Endpoints 
app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<DataContext>();
    
    // Automatically applies any pending migrations and builds the database schema if missing.
    // If two API instances start at the same time, SQL can throw "database already exists" during create.
    // Retry migration once in that specific race scenario.
    try
    {
        context.Database.Migrate();
    }
    catch (SqlException ex) when (ex.Number == 1801)
    {
        context.Database.Migrate();
    }

    // Check if the table is currently empty
    if (!context.Users.Any())
    {
        context.Users.AddRange(            
            new User
            {
                Id = 1,
                Username = "admin",
                // Dynamically hashes the password string right now
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("password123"),
                Role = "Admin",
                IsVerified = true
            }
        );
        
        // Tells SQL Server to respect our explicit hardcoded IDs (1 and 2) instead of auto-generating them
        context.Database.OpenConnection();
        try
        {
            context.Database.ExecuteSqlRaw("SET IDENTITY_INSERT Users ON");
            context.SaveChanges();
            context.Database.ExecuteSqlRaw("SET IDENTITY_INSERT Users OFF");
        }
        finally
        {
            context.Database.CloseConnection();
        }
    }
}

app.Run();