namespace Nodus.Client;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        // Register navigation-only routes (pushed pages, not tab bar items).
        // SettingsPage is intentionally omitted here — it is already declared as a
        // ShellContent tab in AppShell.xaml and registering it twice causes routing
        // conflicts that can silently break GoToAsync calls.
        Routing.RegisterRoute(nameof(Views.ScanPage), typeof(Views.ScanPage));
        Routing.RegisterRoute(nameof(Views.VotingPage), typeof(Views.VotingPage));
        Routing.RegisterRoute(nameof(Views.ConnectionProgressPage), typeof(Views.ConnectionProgressPage));
    }
}
