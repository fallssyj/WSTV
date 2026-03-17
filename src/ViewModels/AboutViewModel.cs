using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System.Reflection;
using WSTV.Messages;

namespace WSTV.ViewModels;

public partial class AboutViewModel : ObservableObject
{
    public string AppVersion { get; } =
        Assembly.GetExecutingAssembly()
                .GetName()
                .Version
                ?.ToString(3) ?? "1.0.0";

    [RelayCommand]
    private void GoBack()
    {
        WeakReferenceMessenger.Default.Send(new NavigateBackMessage());
    }
}
