using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;

namespace Nodus.Server;

public partial class App : Application
{
	public App()
	{
		LogDebug("App Constructor started");
		try
		{
			InitializeComponent();
			LogDebug("InitializeComponent completed");
		}
		catch (Exception ex)
		{
			LogDebug($"[APP INIT ERROR] {ex.Message}");
			LogDebug($"[APP INIT STACK] {ex.StackTrace}");
			if (ex.InnerException != null)
			{
				LogDebug($"[APP INIT INNER] {ex.InnerException.Message}");
			}
			throw;
		}

		AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
		{
			var ex = e.ExceptionObject as Exception;
			LogDebug($"[FATAL UNHANDLED] {ex}");
		};

		TaskScheduler.UnobservedTaskException += (sender, e) =>
		{
			LogDebug($"[TASK EXCEPTION] {e.Exception}");
			e.SetObserved();
		};
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		LogDebug("CreateWindow started");
		try
		{
			var services = Handler?.MauiContext?.Services;
			var mainPage = services?.GetRequiredService<MainPage>();
			
			// Start Cloud Sync
			var syncService = services?.GetRequiredService<Nodus.Server.Services.CloudSyncService>();
			syncService?.Start();

			LogDebug("MainPage and CloudSyncService resolved successfully");
			return new Window(mainPage!);
		}
		catch (Exception ex)
		{
			LogDebug($"[CREATEWINDOW ERROR] {ex}");
			throw;
		}
	}

	private void LogDebug(string message)
	{
		try
		{
			var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Nodus_Debug.log");
			var logLine = $"[{DateTime.Now:HH:mm:ss}] {message}";
			System.IO.File.AppendAllText(logPath, logLine + Environment.NewLine);
			Console.WriteLine(logLine);
		}
		catch { }
	}
}