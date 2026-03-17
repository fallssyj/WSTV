using CommunityToolkit.Mvvm.Messaging.Messages;

namespace WSTV.Messages;

/// <summary>收藏状态变化消息（仅由 BaseChannelViewModel.ToggleFavoriteInternal 发送）</summary>
public sealed class FavoriteChangedMessage : ValueChangedMessage<bool>
{
    public FavoriteChangedMessage(bool added) : base(added) { }
}
