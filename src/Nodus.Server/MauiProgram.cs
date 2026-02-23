using Microsoft.Extensions.Logging;
using Nodus.Shared.Abstractions;
using Nodus.Shared.Services;
using Nodus.Shared.Config;
using Shiny;

namespace Nodus.Server;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		LogDebug("--- Starting Nodus Application ---");
		try
		{
			var builder = MauiApp.CreateBuilder();
			LogDebug("Builder created");

			builder
				.UseMauiApp<App>()
				.ConfigureFonts(fonts =>
				{
					fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
					fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
				});

#if DEBUG
			builder.Logging.AddDebug();
			builder.Logging.SetMinimumLevel(LogLevel.Debug);
#else
			builder.Logging.SetMinimumLevel(LogLevel.Information);
#endif

// Database Service — MongoDB Atlas
            LogDebug("Registering Database Services");
            // Database Services
            // 1. MongoDbService (Concrete) — For synchronization
            builder.Services.AddSingleton<Nodus.Infrastructure.Services.MongoDbService>(sp => {
                var logger = sp.GetRequiredService<ILogger<Nodus.Infrastructure.Services.MongoDbService>>();
                return new Nodus.Infrastructure.Services.MongoDbService(
                    AppSecrets.MongoConnectionString,
                    AppSecrets.MongoDatabaseName,
                    logger);
            });

            // 2. LocalDatabaseService (Interface) — For UI & Offline use
            builder.Services.AddSingleton<Nodus.Shared.Abstractions.IDatabaseService, Nodus.Infrastructure.Services.LocalDatabaseService>();

            builder.Services.AddSingleton<Nodus.Server.Services.BleServerService>();

            // Init Shiny
#if ANDROID
            builder.Services.AddBluetoothLeHosting();
#endif

            LogDebug("Registering Core Services");
            // Services
            builder.Services.AddSingleton<Nodus.Shared.Abstractions.IDateTimeProvider, Nodus.Shared.Services.SystemDateTimeProvider>();
            builder.Services.AddSingleton<Nodus.Shared.Abstractions.IFileService, Nodus.Shared.Services.FileService>();
            builder.Services.AddSingleton<Nodus.Shared.Abstractions.IFileSaverService, Nodus.Server.Services.FileSaverService>();
            builder.Services.AddSingleton<Nodus.Server.Services.CloudSyncService>();
            
            builder.Services.AddSingleton<Nodus.Shared.Services.TelemetryService>();
            builder.Services.AddSingleton<Nodus.Shared.Services.VoteAggregatorService>();
            builder.Services.AddSingleton<Nodus.Shared.Services.VoteIngestionService>();
            builder.Services.AddSingleton<Nodus.Server.Services.ExportService>();

            LogDebug("Registering UI Components");
            // Pages
            builder.Services.AddSingleton<MainPage>();
            builder.Services.AddTransient<Nodus.Server.Views.CreateEventPage>();
            builder.Services.AddSingleton<Nodus.Server.Views.ResultsPage>();
            builder.Services.AddSingleton<Nodus.Server.Views.TopologyPage>();

            // ViewModels (if not already scanned or using auto-wire)
            builder.Services.AddTransient<Nodus.Server.ViewModels.CreateEventViewModel>();
            builder.Services.AddSingleton<Nodus.Server.ViewModels.ResultsViewModel>();
            builder.Services.AddSingleton<Nodus.Server.ViewModels.TopologyViewModel>();

            LogDebug("Building MauiApp");
            var app = builder.Build();
            LogDebug("MauiApp built successfully");
            return app;
		}
		catch (Exception ex)
		{
			LogDebug($"FATAL STARTUP ERROR: {ex}");
			throw;
		}
	}

	private static void LogDebug(string message)
	{
		try
		{
			var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Nodus_Debug.log");
			var logLine = $"[{DateTime.Now:HH:mm:ss}] {message}";
			File.AppendAllText(logPath, logLine + Environment.NewLine);
			Console.WriteLine(logLine);
		}
		catch { }
	}
}
