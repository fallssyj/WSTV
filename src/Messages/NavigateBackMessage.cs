using CommunityToolkit.Mvvm.Messaging.Messages;

namespace WSTV.Messages;

public class NavigateBackMessage : ValueChangedMessage<bool>
{
    public NavigateBackMessage() : base(true) { }
}
