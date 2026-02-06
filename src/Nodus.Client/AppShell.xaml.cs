namespace Nodus.Client;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();
        Routing.RegisterRoute(nameof(Views.ScanPage), typeof(Views.ScanPage));
        Routing.RegisterRoute(nameof(Views.VotingPage), typeof(Views.VotingPage));
        Routing.RegisterRoute(nameof(Views.JudgeRegistrationPage), typeof(Views.JudgeRegistrationPage));
	}
}
