using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;

namespace Nodus.Server;

public partial class App : Application
{
	public App()
	{
		LogDebug("App Constructor started");
		InitializeComponent();
		LogDebug("InitializeComponent completed");

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
			var mainPage = Handler?.MauiContext?.Services.GetRequiredService<MainPage>();
			LogDebug("MainPage resolved successfully");
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