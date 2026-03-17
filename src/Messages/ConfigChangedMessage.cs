using CommunityToolkit.Mvvm.Messaging.Messages;
using WSTV.Models;

namespace WSTV.Messages;

/// <summary>
/// 当订阅配置发生变化（切换激活源、刷新、添加等）时广播此消息
/// </summary>
public class ConfigChangedMessage : ValueChangedMessage<AppConfig>
{
    public ConfigChangedMessage(AppConfig value) : base(value) { }
}
