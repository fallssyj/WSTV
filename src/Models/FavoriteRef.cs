namespace WSTV.Models;

/// <summary>
/// 收藏频道的轻量标识符，只存 TvgId + TvgName 用于运行时反查完整 Channel
/// </summary>
public class FavoriteRef
{
    public string TvgId { get; set; } = string.Empty;
    public string TvgName { get; set; } = string.Empty;
}
