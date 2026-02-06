using Nodus.Client.ViewModels;

namespace Nodus.Client.Views;

public partial class VotingPage : ContentPage
{
    private readonly VotingViewModel _viewModel;

	public VotingPage(VotingViewModel viewModel)
	{
		InitializeComponent();
        BindingContext = _viewModel = viewModel;
	}

    // Called when navigated to
    public async Task InitializeAsync(string projectId, string eventId)
    {
        await _viewModel.InitializeAsync(projectId, eventId);
    }
}
