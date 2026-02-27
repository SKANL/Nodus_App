using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Nodus.Web;
using Nodus.Web.Services;
using Nodus.Shared.Abstractions;
using Nodus.Shared.Services;
using Nodus.Shared.Config;
using Blazored.LocalStorage;
var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// Register Nodus services
builder.Services.AddScoped<IDatabaseService, WebDatabaseService>();
// Settings Service
builder.Services.AddScoped<ISettingsService, SettingsService>();
builder.Services.AddScoped<EventService>();

builder.Services.AddScoped<ProjectService>();
builder.Services.AddScoped<QrGeneratorService>();

// Configure HttpClient for Backend API communication
builder.Services.AddHttpClient<BackendApiService>(client => 
{
    // Point this to Nodus.Api using HTTP to avoid local SSL certificate errors in the browser
    client.BaseAddress = new Uri("http://localhost:5280"); 
});

builder.Services.AddBlazoredLocalStorage(); // Keep for legacy/migration if needed, or remove later

await builder.Build().RunAsync();
