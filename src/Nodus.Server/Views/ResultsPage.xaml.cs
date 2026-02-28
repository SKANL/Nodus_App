namespace Nodus.Server.Views;

public partial class ResultsPage : ContentPage
{
    public ResultsPage(ViewModels.ResultsViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
