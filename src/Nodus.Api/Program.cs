using Microsoft.AspNetCore.Cors;
using Nodus.Api.Middleware;
using Nodus.Shared.Abstractions;
using Nodus.Shared.Services;
using Nodus.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

// Register MongoDB Service using shared secrets
builder.Services.AddSingleton<IDatabaseService>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<MongoDbService>>();
    return new MongoDbService(Nodus.Shared.Config.AppSecrets.MongoConnectionString, Nodus.Shared.Config.AppSecrets.MongoDatabaseName, logger);
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
