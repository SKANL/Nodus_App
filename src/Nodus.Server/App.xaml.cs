using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;

namespace Nodus.Server;

public partial class App : Application
{
	public App()
	{
		// Add global exception handlers to diagnose crashes
		AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
		{
			var ex = e.ExceptionObject as Exception;
			Debug.WriteLine($"[FATAL UNHANDLED] {ex?.ToString()}");
			Console.WriteLine($"[FATAL UNHANDLED] {ex?.ToString()}");
		};

		TaskScheduler.UnobservedTaskException += (sender, e) =>
		{
			Debug.WriteLine($"[TASK EXCEPTION] {e.Exception}");
			Console.WriteLine($"[TASK EXCEPTION] {e.Exception}");
			e.SetObserved();
		};

		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		try
		{
			var mainPage = Handler?.MauiContext?.Services.GetRequiredService<MainPage>();
			return new Window(mainPage!);
		}
		catch (Exception ex)
		{
			Debug.WriteLine($"[CREATEWINDOW ERROR] {ex}");
			Console.WriteLine($"[CREATEWINDOW ERROR] {ex}");
			throw;
		}
	}
}