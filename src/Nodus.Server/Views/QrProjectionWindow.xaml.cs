using Microsoft.Maui.Controls;

namespace Nodus.Server.Views;

public partial class QrProjectionWindow : ContentPage
{
    public QrProjectionWindow(object viewModelContext)
    {
        InitializeComponent();
        BindingContext = viewModelContext;
    }

    private async void OnCloseClicked(object sender, EventArgs e)
    {
        await Navigation.PopModalAsync();
    }
}
