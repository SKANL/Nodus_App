using Nodus.Shared.Services;
using Nodus.Server.Services; // Assuming BleServerService is in Nodus.Server.Services

namespace Nodus.Server;

public partial class MainPage : TabbedPage
{
    public MainPage(Views.CreateEventPage createEventPage, Views.ResultsPage resultsPage, Views.TopologyPage topologyPage)
    {
        InitializeComponent();

        Children.Add(new NavigationPage(createEventPage) { Title = "Eventos", IconImageSource = "calendar.png" });
        Children.Add(new NavigationPage(resultsPage) { Title = "Resultados", IconImageSource = "chart.png" });
        Children.Add(new NavigationPage(topologyPage) { Title = "Mapa de Red", IconImageSource = "network.png" });
    }
}
