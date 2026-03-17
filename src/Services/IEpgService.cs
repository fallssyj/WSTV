using WSTV.Models;

namespace WSTV.Services;

/// <summary>EPG 数据服务抽象接口，便于单元测试与依赖替换</summary>
public interface IEpgService
{
    bool HasAnyData { get; }
    void Reload();
    IReadOnlyList<EpgProgram> GetPrograms(string tvgId, DateTime date, string channelName = "");
    EpgProgram? GetNowPlaying(string tvgId, string channelName = "");
    bool HasEpgFor(string tvgId, string channelName = "");
}
