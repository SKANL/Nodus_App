using Nodus.Client.ViewModels;

namespace Nodus.Client.Views;

public partial class ConnectionProgressPage : ContentPage
{
    private readonly ConnectionProgressViewModel _viewModel;

    public ConnectionProgressPage(ConnectionProgressViewModel vm)
    {
        InitializeComponent();
        _viewModel = vm;
        BindingContext = vm;
    }

    protected override void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);
        _viewModel.OnNavigatedTo();
    }

    protected override void OnNavigatedFrom(NavigatedFromEventArgs args)
    {
        base.OnNavigatedFrom(args);
        _viewModel.OnNavigatedFrom();
    }
}
