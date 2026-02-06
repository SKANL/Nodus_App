using Nodus.Client.ViewModels;
using Nodus.Shared.Services;
using ZXing.Net.Maui;

namespace Nodus.Client.Views;

public partial class JudgeRegistrationPage : ContentPage
{
    private readonly JudgeRegistrationViewModel _viewModel;

	public JudgeRegistrationPage(JudgeRegistrationViewModel viewModel)
	{
		InitializeComponent();
        _viewModel = viewModel;
		BindingContext = _viewModel;

        CameraView.Options = new BarcodeReaderOptions
        {
            Formats = BarcodeFormats.TwoDimensional,
            AutoRotate = true,
            Multiple = false
        };
	}

    protected override void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);
        CameraView.IsDetecting = true;
    }

    protected override void OnNavigatedFrom(NavigatedFromEventArgs args)
    {
        base.OnNavigatedFrom(args);
        CameraView.IsDetecting = false;
    }

    private void CameraBarcodeReaderView_BarcodesDetected(object sender, BarcodeDetectionEventArgs e)
    {
        var first = e.Results?.FirstOrDefault();
        if (first is null) return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            _viewModel.ProcessScanResultCommand.Execute(first.Value);
        });
    }
    // Called when navigated to
    public async Task InitializeAsync(string projectId, string eventId)
    {
        await _viewModel.InitializeAsync(projectId, eventId);
    }

    private async void OnCancelClicked(object sender, EventArgs e)
    {
        await Navigation.PopAsync();
    }
}
