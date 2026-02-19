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
// Nota: Blazor WASM corre en el browser — usar Atlas requiere un API proxy backend.
builder.Services.AddSingleton<IDatabaseService>(sp => {
    var logger = sp.GetRequiredService<ILogger<MongoDbService>>();
    return new MongoDbService("mongodb://localhost:27017", "nodus_db", logger);
});


// Settings Service
builder.Services.AddSingleton<Nodus.Shared.Abstractions.ISettingsService, SettingsService>();

builder.Services.AddScoped<ProjectService>();
builder.Services.AddScoped<QrGeneratorService>();
builder.Services.AddBlazoredLocalStorage(); // Keep for legacy/migration if needed, or remove later

await builder.Build().RunAsync();
