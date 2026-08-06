using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.Components.Authorization;
using Blazored.LocalStorage;
using Restaurant.Client;
using Restaurant.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// 1. Core Services Setup
builder.Services.AddBlazoredLocalStorage();
builder.Services.AddAuthorizationCore();

// 2. Configure the shared HTTP client to target the backend API instead of the Blazor dev host
const string apiBaseAddress = "http://localhost:5123/";
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(apiBaseAddress) });

// 3. Custom Authentication State Setup (Cleaned up duplicates)
builder.Services.AddScoped<AuthenticationStateProvider, CustomAuthStateProvider>();
builder.Services.AddScoped(sp => (CustomAuthStateProvider)sp.GetRequiredService<AuthenticationStateProvider>());

await builder.Build().RunAsync();