using Microsoft.AspNetCore.Cors;
using Nodus.Shared.Abstractions;
using Nodus.Shared.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

// Register MongoDB Service using shared secrets
builder.Services.AddSingleton<IDatabaseService>(sp => {
    var logger = sp.GetRequiredService<ILogger<MongoDbService>>();
    return new MongoDbService(Nodus.Shared.Config.AppSecrets.MongoConnectionString, Nodus.Shared.Config.AppSecrets.MongoDatabaseName, logger);
});

// Configure CORS for Blazor WASM
builder.Services.AddCors(options => {
    options.AddDefaultPolicy(policy => {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
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
app.MapControllers();

app.Run();
