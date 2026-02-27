namespace Nodus.Server.Views;

public partial class TopologyPage : ContentPage
{
    public TopologyPage(ViewModels.TopologyViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
