using Microsoft.Extensions.Logging;
using Shiny;

namespace Nodus.Server;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
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

// Database Service
		var dbPath = Path.Combine(FileSystem.AppDataDirectory, "nodus.db");
		builder.Services.AddSingleton<Nodus.Shared.Services.DatabaseService>(sp =>
		{
			var logger = sp.GetRequiredService<ILogger<Nodus.Shared.Services.DatabaseService>>();
			return new Nodus.Shared.Services.DatabaseService(dbPath, logger);
		});
        builder.Services.AddSingleton<Nodus.Server.Services.BleServerService>();

        // Init Shiny
#if ANDROID
        builder.Services.AddBluetoothLeHosting();
#endif

        // Services
        builder.Services.AddSingleton<Nodus.Server.Services.VoteAggregatorService>();
        builder.Services.AddSingleton<Nodus.Server.Services.ExportService>();

        // Pages
        builder.Services.AddSingleton<MainPage>();
        builder.Services.AddTransient<Nodus.Server.Views.CreateEventPage>();
        builder.Services.AddSingleton<Nodus.Server.Views.ResultsPage>();
        builder.Services.AddSingleton<Nodus.Server.Views.TopologyPage>();

        // ViewModels (if not already scanned or using auto-wire)
        builder.Services.AddTransient<Nodus.Server.ViewModels.CreateEventViewModel>();
        builder.Services.AddSingleton<Nodus.Server.ViewModels.ResultsViewModel>();
        builder.Services.AddSingleton<Nodus.Server.ViewModels.TopologyViewModel>();

        return builder.Build();
	}
}
