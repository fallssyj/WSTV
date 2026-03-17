using CommunityToolkit.Mvvm.Messaging.Messages;
using WSTV.Models;

namespace WSTV.Messages;

public class PlayChannelMessage : ValueChangedMessage<(Channel Current, IReadOnlyList<Channel> Channels)>
{
    public PlayChannelMessage(Channel current, IReadOnlyList<Channel> channels)
        : base((current, channels)) { }
}
