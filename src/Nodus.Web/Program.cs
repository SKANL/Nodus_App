using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Nodus.Web;
using Nodus.Web.Services;
using Blazored.LocalStorage;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// Register Nodus services
builder.Services.AddSingleton<Nodus.Shared.Abstractions.IDatabaseService>(sp => 
{
    var logger = sp.GetRequiredService<ILogger<Nodus.Shared.Services.DatabaseService>>();
    // "nodus_web.db" will be created in the browser's virtual file system.
    // Ensure sqlite-net-pcl uses the correct provider for WASM persistence if needed.
    // For standard improved compatibility, relying on the package defaults.
    return new Nodus.Shared.Services.DatabaseService("nodus_web.db", logger);
});

// Settings Service
builder.Services.AddSingleton<Nodus.Shared.Abstractions.ISettingsService, SettingsService>();

builder.Services.AddScoped<ProjectService>();
builder.Services.AddScoped<QrGeneratorService>();
builder.Services.AddBlazoredLocalStorage(); // Keep for legacy/migration if needed, or remove later

await builder.Build().RunAsync();
