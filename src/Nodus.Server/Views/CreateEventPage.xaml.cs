using Nodus.Server.ViewModels;
using Nodus.Shared.Services;

namespace Nodus.Server.Views;

public partial class CreateEventPage : ContentPage
{
	public CreateEventPage(CreateEventViewModel vm)
	{
		InitializeComponent();
		BindingContext = vm;
	}
}
