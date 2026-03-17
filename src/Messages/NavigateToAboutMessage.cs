using CommunityToolkit.Mvvm.Messaging.Messages;

namespace WSTV.Messages;

public class NavigateToAboutMessage : ValueChangedMessage<bool>
{
    public NavigateToAboutMessage() : base(true) { }
}
