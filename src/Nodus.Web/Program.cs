using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Nodus.Web;
using Nodus.Web.Services;
using Nodus.Shared.Abstractions;
using Nodus.Shared.Services;
using Blazored.LocalStorage;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// Register Nodus services
builder.Services.AddScoped<IDatabaseService, WebDatabaseService>();
builder.Services.AddScoped<ISettingsService, SettingsService>();
builder.Services.AddScoped<EventService>();
builder.Services.AddScoped<ProjectService>();
builder.Services.AddScoped<QrGeneratorService>();

// NodusApiService: reads base URL and API key from wwwroot/appsettings[.Environment].json
// Keys: "NodusApi:BaseUrl", "NodusApi:ApiKey". BaseUrl defaults to HostEnvironment.BaseAddress (same-origin).
builder.Services.AddScoped<NodusApiService>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var rawUrl = config["NodusApi:BaseUrl"];
    // config[] returns "" (not null) when the key exists but is empty → ?? won't trigger fallback.
    var apiBaseUrl = string.IsNullOrWhiteSpace(rawUrl)
        ? builder.HostEnvironment.BaseAddress
        : rawUrl;
    var http = new HttpClient { BaseAddress = new Uri(apiBaseUrl.TrimEnd('/') + "/") };
    var apiKey = config["NodusApi:ApiKey"];
    if (!string.IsNullOrWhiteSpace(apiKey))
        http.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
    return new NodusApiService(http);
});

builder.Services.AddBlazoredLocalStorage();

await builder.Build().RunAsync();
