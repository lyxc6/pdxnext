namespace PdxLauncher.App.ViewModels;

/// <summary>
/// 「请打开某一行的创意工坊详情子页」这个请求：行本身 + 该从哪儿回来。
/// <para>
/// 详情页原先只服务创意工坊列表，页头的返回按钮写死退回「创意工坊」。现在 Mod 库的行也能进来，
/// 返回目标就必须由发起方告诉页面，否则从 Mod 库点进来会被甩到创意工坊列表去。
/// </para>
/// </summary>
/// <param name="Row">要展示的那一行（已经归一成详情页认识的形状）。</param>
/// <param name="ReturnTag">返回后要去导航的页面 Tag；null 表示沿用默认（创意工坊列表）。</param>
/// <param name="Parent">
/// 上级条目（从合集内容点进子项时是合集本身）。非 null 时「返回」先回上级条目详情，
/// 而不是直接跳回创意工坊列表。
/// </param>
public sealed record WorkshopDetailNavigationRequest(
    WorkshopRowViewModel Row,
    string? ReturnTag,
    WorkshopRowViewModel? Parent = null);
