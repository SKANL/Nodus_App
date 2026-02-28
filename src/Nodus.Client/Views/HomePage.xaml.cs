using Nodus.Client.ViewModels;

namespace Nodus.Client.Views;

public partial class HomePage : ContentPage
{
    private readonly HomeViewModel _viewModel;

    public HomePage(HomeViewModel vm)
    {
        InitializeComponent();
        _viewModel = vm;
        BindingContext = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.RefreshAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // Dispose previous ViewModel to prevent memory leaks from BLE event subscriptions.
        // HomeViewModel is registered as Transient — each visit to HomePage gets a new instance;
        // without explicit disposal the old instance keeps the BLE Singleton subscription alive.
        _viewModel.Dispose();
    }
}
