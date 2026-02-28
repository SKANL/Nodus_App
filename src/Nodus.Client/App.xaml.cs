using Microsoft.Extensions.DependencyInjection;
using Nodus.Shared.Abstractions;

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
            var shell = new AppShell();

            // Auto-start BLE scanning once the window is fully ready.
            // Must be deferred so Shiny's IBleManager has time to initialise.
            shell.Loaded += async (_, _) =>
            {
                try
                {
                    var ble = IPlatformApplication.Current?.Services
                                  .GetService<IBleClientService>();
                    if (ble != null)
                        await ble.StartScanningForServerAsync();
                }
                catch (Exception ex)
                {
                    // BLE may not be available (Windows desktop, simulator) — degrade gracefully.
                    System.Diagnostics.Debug.WriteLine(
                        $"[BLE] Auto-scan could not start: {ex.Message}");
                }
            };

            return new Window(shell);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CRITICAL] APP STARTUP CRASH: {ex}");
            System.Diagnostics.Debug.WriteLine($"[CRITICAL] APP STARTUP CRASH: {ex}");
            throw;
        }
    }
}