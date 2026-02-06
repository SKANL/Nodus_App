using Nodus.Client.ViewModels;
using ZXing.Net.Maui;

namespace Nodus.Client.Views;

public partial class ScanPage : ContentPage
{
    private readonly ScanViewModel _vm;

    public ScanPage(ScanViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.IsScanning = true;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _vm.IsScanning = false;
    }

    private void CameraBarcodeReaderView_BarcodesDetected(object sender, BarcodeDetectionEventArgs e)
    {
        if (e.Results.Length > 0)
        {
            var content = e.Results[0].Value;
            MainThread.BeginInvokeOnMainThread(async () => await _vm.ProcessQrCodeAsync(content));
        }
    }
}
