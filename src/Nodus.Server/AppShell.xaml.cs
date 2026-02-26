namespace Nodus.Server;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();
		Routing.RegisterRoute(nameof(Views.CreateEventPage), typeof(Views.CreateEventPage));
		Routing.RegisterRoute(nameof(Views.TopologyPage), typeof(Views.TopologyPage));
		Routing.RegisterRoute(nameof(Views.ResultsPage), typeof(Views.ResultsPage));
		Routing.RegisterRoute(nameof(Views.QrProjectionWindow), typeof(Views.QrProjectionWindow));
	}
}
