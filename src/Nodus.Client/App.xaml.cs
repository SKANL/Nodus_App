using Microsoft.Extensions.DependencyInjection;

namespace Nodus.Client;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		try
		{
			return new Window(new AppShell());
		}
		catch (Exception ex)
		{
			Console.WriteLine($"[CRITICAL] APP STARTUP CRASH: {ex}");
			System.Diagnostics.Debug.WriteLine($"[CRITICAL] APP STARTUP CRASH: {ex}");
			throw;
		}
	}
}