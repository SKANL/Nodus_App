using Nodus.Client.ViewModels;
using Nodus.Shared.Services;

namespace Nodus.Client.Views;

public partial class HomePage : ContentPage
{
    public HomePage(HomeViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
