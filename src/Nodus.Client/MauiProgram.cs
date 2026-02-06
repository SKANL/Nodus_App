using Microsoft.Extensions.Logging;
using ZXing.Net.Maui.Controls;
using Shiny;
using Nodus.Shared.Abstractions;
using Nodus.Shared.Services;
using Nodus.Client.Abstractions;
using Nodus.Client.Services;

namespace Nodus.Client;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
            .UseShiny()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			})
            .UseBarcodeReader();

#if DEBUG
		builder.Logging.AddDebug();
		builder.Logging.SetMinimumLevel(LogLevel.Debug);
#else
		builder.Logging.SetMinimumLevel(LogLevel.Information);
#endif

        // Core Infrastructure Services
        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "nodus_client.db");
        builder.Services.AddSingleton<IDatabaseService>(sp => 
        {
            var logger = sp.GetRequiredService<ILogger<DatabaseService>>();
            return new DatabaseService(dbPath, logger);
        });

        // BLE Services (Shiny)
        builder.Services.AddBluetoothLE();
#if ANDROID
        builder.Services.AddBluetoothLeHosting(); // For Relay role
#endif

        // Protocol Services
        builder.Services.AddSingleton<Nodus.Shared.Protocol.PacketTracker>();
        builder.Services.AddSingleton<IBleClientService, BleClientService>();
        builder.Services.AddSingleton<IRelayHostingService, RelayHostingService>();
        builder.Services.AddSingleton<ISwarmService, SwarmService>();

		// Pages & ViewModels (Transient for fresh state)
		builder.Services.AddTransient<Nodus.Client.Views.HomePage>();
		builder.Services.AddTransient<Nodus.Client.ViewModels.HomeViewModel>();
		builder.Services.AddTransient<Nodus.Client.Views.JudgeRegistrationPage>();
		builder.Services.AddTransient<Nodus.Client.ViewModels.JudgeRegistrationViewModel>();
		builder.Services.AddTransient<Nodus.Client.Views.VotingPage>();
		builder.Services.AddTransient<Nodus.Client.ViewModels.VotingViewModel>();
		builder.Services.AddTransient<Nodus.Client.Views.ScanPage>();
		builder.Services.AddTransient<Nodus.Client.ViewModels.ScanViewModel>();

		return builder.Build();
	}
}
