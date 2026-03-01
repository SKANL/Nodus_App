using Nodus.Api.Middleware;
using Nodus.Shared.Abstractions;
using Nodus.Shared.Services;
using Nodus.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

// Register MongoDB Service
// Priority: appsettings[MongoDB:ConnectionString] → env var MongoDB__ConnectionString
// En produccion (Render): set MongoDB__ConnectionString y MongoDB__DatabaseName como env vars.
// En desarrollo: set MongoDB:ConnectionString en appsettings.Development.json.
builder.Services.AddSingleton<IDatabaseService>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<MongoDbService>>();
    var cfg = sp.GetRequiredService<IConfiguration>();
    var connStr = cfg["MongoDB:ConnectionString"]
        ?? throw new InvalidOperationException(
            "MongoDB:ConnectionString no configurado. " +
            "En Render: agrega la variable de entorno MongoDB__ConnectionString. " +
            "En local: agrégala a appsettings.Development.json.");
    var dbName = cfg["MongoDB:DatabaseName"] ?? "nodus_db";
    return new MongoDbService(connStr, dbName, logger);
});

// Configure CORS for Blazor WASM
// Origins are read from appsettings.json [Cors:AllowedOrigins].
// Add your production domain there — never use AllowAnyOrigin() in production.
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? [];

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy
            .WithOrigins(allowedOrigins)
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseCors();
app.UseMiddleware<ApiKeyMiddleware>();
app.MapControllers();

app.Run();
