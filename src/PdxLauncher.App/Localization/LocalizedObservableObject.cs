namespace PdxLauncher.App.Localization;

using System.Collections.Concurrent;
using System.Reflection;

using CommunityToolkit.Mvvm.ComponentModel;

using PdxLauncher.App.Services;

/// <summary>
/// 带文案的 ViewModel 基类：提供 XAML 用的 <see cref="L"/> 绑定入口、<see cref="T"/> 取文案，
/// 并在语言切换时自动给自身所有公开属性发一次 PropertyChanged（x:Bind OneWay 靠它刷新）。
/// </summary>
public abstract class LocalizedObservableObject : ObservableObject
{
    private static readonly ConcurrentDictionary<Type, string[]> PropertyNames = new();

    protected LocalizedObservableObject()
    {
        // 由 Localizer 以弱引用的方式登记（强订阅会让全局单例钉住每一个视图模型）。
        Localizer.Instance.RegisterLocalized(this);
    }

    /// <summary>XAML 里写 <c>{x:Bind L.SomeKey, Mode=OneWay}</c> 的入口。</summary>
    public Localizer L => Localizer.Instance;

    /// <summary>在 ViewModel 里取文案（键请用生成的 <c>Keys.Xxx</c> 常量）。</summary>
    protected static string T(string key, params object?[] args) => Localizer.Instance.T(key, args);

    /// <summary>语言变化后需要额外做的事（例如重建带本地化文字的选项列表）；默认什么都不做。</summary>
    protected virtual void Relocalize()
    {
    }

    /// <summary>切语言后由 <see cref="Localizer"/> 回调：先重发属性通知，再做子类自己的重建。</summary>
    internal void RefreshLocalizedText()
    {
        try
        {
            foreach (var name in PropertyNames.GetOrAdd(GetType(), DiscoverPropertyNames))
            {
                OnPropertyChanged(name);
            }

            Relocalize();
        }
        catch (Exception ex)
        {
            // 刷新失败只记日志：界面最多保留旧语言，不能因为切语言崩掉页面。
            if (AppServices.IsInitialized)
            {
                AppServices.Logger.Error("切换界面语言后刷新失败: " + GetType().Name, ex);
            }
        }
    }

    private static string[] DiscoverPropertyNames(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
            .Select(property => property.Name)
            .ToArray();
}
