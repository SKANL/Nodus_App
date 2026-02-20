using Microsoft.Extensions.Logging;
using ZXing.Net.Maui.Controls;
using Shiny;
using Nodus.Shared.Abstractions;
using Nodus.Shared.Services;
using Nodus.Shared.Config;
using Nodus.Client.Services;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Nodus.Tests.Unit")]

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

        // Core Infrastructure Services — MongoDB Atlas
        // Database Services
        // 1. MongoDbService (Concrete) — For synchronization
        builder.Services.AddSingleton<MongoDbService>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<MongoDbService>>();
            return new MongoDbService(
                AppSecrets.MongoConnectionString,
                AppSecrets.MongoDatabaseName,
                logger);
        });

        // 2. LocalDatabaseService (Interface) — For UI & Offline use
        builder.Services.AddSingleton<IDatabaseService, LocalDatabaseService>();
         
        // Secure Storage
        builder.Services.AddSingleton<ISecureStorageService, SecureStorageService>();
        
        // UI Services
        builder.Services.AddSingleton<IDialogService, Nodus.Client.Services.DialogService>();

        // BLE Services (Shiny)
        builder.Services.AddBluetoothLE();
#if ANDROID
        builder.Services.AddBluetoothLeHosting(); // For Relay role
#endif
        builder.Services.AddSingleton<ITimerFactory, MauiTimerFactory>();

        // Protocol Services
        builder.Services.AddSingleton<Nodus.Shared.Services.TelemetryService>();
        builder.Services.AddSingleton<IChunkerService, ChunkerService>();
        builder.Services.AddSingleton<IImageCompressionService, ImageCompressionService>();
        builder.Services.AddSingleton<IFileService, FileService>();
        builder.Services.AddSingleton<MediaSyncService>();
        builder.Services.AddSingleton<Nodus.Shared.Protocol.PacketTracker>();
        builder.Services.AddSingleton<IBleClientService, BleClientService>();
        builder.Services.AddSingleton<IRelayHostingService, RelayHostingService>();
        builder.Services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
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
        builder.Services.AddTransient<Nodus.Client.Views.SettingsPage>();
        builder.Services.AddTransient<Nodus.Client.ViewModels.SettingsViewModel>();

		return builder.Build();
	}
}
