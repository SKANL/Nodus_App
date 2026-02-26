namespace Nodus.Client;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();
        Routing.RegisterRoute(nameof(Views.ScanPage), typeof(Views.ScanPage));
        Routing.RegisterRoute(nameof(Views.VotingPage), typeof(Views.VotingPage));
        Routing.RegisterRoute(nameof(Views.ConnectionProgressPage), typeof(Views.ConnectionProgressPage));
        Routing.RegisterRoute(nameof(Views.SettingsPage), typeof(Views.SettingsPage));
	}
}
