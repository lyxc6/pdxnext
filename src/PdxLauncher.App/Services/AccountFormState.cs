namespace PdxLauncher.App.Services;

/// <summary>
/// 登录表单的「可编辑 / 可提交」判定。
/// 纯 C#，不引用任何 XAML/UI 类型，可脱离 UI 自检。
/// </summary>
/// <remarks>
/// 拆出这个类是因为一次真实故障：原先输入框与登录按钮**共用**同一个
/// <c>CanSignIn = !IsBusy &amp;&amp; !IsSignedIn &amp;&amp; Email 非空</c>。邮箱初值为空串，
/// 于是邮箱框自己在启动时就是禁用的 —— 要输入邮箱才能启用邮箱框，而邮箱只能靠这个框输入。
/// 鸡生蛋死锁，用户连邮箱都打不进去，观感就是「账号登录坏了、点了没反应」。
/// 现在把两件事分开：<see cref="CanEditCredentials"/> 只管「能不能改输入」，
/// <see cref="CanSubmit"/> 才额外要求邮箱已填。
/// </remarks>
public static class AccountFormState
{
    /// <summary>
    /// 输入控件（邮箱框 / 密码框 / 保持登录开关）是否可用。
    /// 只排除「请求进行中」与「已登录」两种状态，与邮箱是否填写无关 —— 这一点是死锁的解法。
    /// </summary>
    /// <param name="isBusy">正在发起登录请求（界面据此防风重入）。</param>
    /// <param name="isSignedIn">当前已登录（登录表单整体隐藏）。</param>
    public static bool CanEditCredentials(bool isBusy, bool isSignedIn) => !isBusy && !isSignedIn;

    /// <summary>
    /// 登录按钮是否可点：在可编辑的基础上，还要求邮箱已填写。
    /// 空邮箱时按钮置灰而不是提交后报错，保留原有交互设计。
    /// </summary>
    /// <param name="isBusy">正在发起登录请求。</param>
    /// <param name="isSignedIn">当前已登录。</param>
    /// <param name="email">邮箱原文（内部按 <see cref="string.IsNullOrWhiteSpace(string?)"/> 判断）。</param>
    public static bool CanSubmit(bool isBusy, bool isSignedIn, string? email)
        => CanEditCredentials(isBusy, isSignedIn) && !string.IsNullOrWhiteSpace(email);
}
