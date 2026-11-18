using CommunityToolkit.Mvvm.Input;
using TundraDrive.Desktop.Localization;

namespace TundraDrive.Desktop.ViewModels;

public sealed partial class AboutViewModel : ViewModelBase
{
    public ILocalizationService Loc { get; }
    public string CompanyName => "TundraLab";
    public string ProductName => "TundraDrive";
    public string DeveloperName => "Gustavo Henrique Novaes Gomes";
    public string RepositoryUrl => MainWindowViewModel.RepositoryUrl;
    public Func<Uri, Task>? OpenExternalUriCallback { get; set; }

    public AboutViewModel(ILocalizationService localization)
    {
        Loc = localization;
    }

    [RelayCommand]
    private async Task OpenRepositoryAsync()
    {
        if (OpenExternalUriCallback is not null)
        {
            await OpenExternalUriCallback.Invoke(new Uri(RepositoryUrl));
        }
    }
}
