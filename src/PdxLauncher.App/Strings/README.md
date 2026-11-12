# 界面文案（i18n）约定

目标：全 UI 双语（`zh-CN` 默认、`en-US`），**不重启即可切换**（`{x:Bind L.Xxx, Mode=OneWay}` 会随 `Localizer.Apply` 刷新）。

## 1. 文案源

```
src/PdxLauncher.App/Strings/zh-CN/Resources.resw
src/PdxLauncher.App/Strings/en-US/Resources.resw
```

- WinUI 官方 `.resw` 格式；编译时由 `makepri` 编进 `PdxLauncher.App.pri`，运行期由 Windows App SDK 的 `ResourceManager` 读取。
- **资源名必须是不含点的 PascalCase 标识符**。`.resw` 里的 `.` 是「属性分隔符」（`Foo.Text` 会变成子树 `Foo`），带点会被 makepri 拆开，`ResourceLoader` 按平名取不到。
- 标识符由原点分键推导：每段首字母大写后拼接（`mods.title` → `ModsTitle`）。
- 两种语言的资源名必须完全一致；占位符统一 `{0}`、`{1}`…，两种语言的数量与序号必须一致。
- 值不能为空、首尾不能有空格；多行文案在 XML 里写真实换行，不要写字面 `\n`。

## 2. 生成

编译期由 `src/PdxLauncher.Localization.Generator`（Roslyn 增量源生成器）读取这两个 `.resw`，生成：

- `Localizer` 的 partial：每个资源一个只读属性；
- `internal static class Keys`：每个资源一个 `public const string`；
- `AllKeys` 表（切语言时批量发 `PropertyChanged`）。

生成物只存在于内存，**不落盘、不需要提交**。缺键、两语言键集不一致、占位符不一致都会在编译期报 `PDXI18N001/002`。

## 3. XAML 用法

页面/窗口类里加：

```csharp
using PdxLauncher.App.Localization;

public Localizer L => Localizer.Instance;
```

绑定一律 **`Mode=OneWay`**（`x:Bind` 默认是 OneTime，切语言不会更新）：

```xml
<TextBlock Text="{x:Bind L.ModsTitle, Mode=OneWay}" />
<Button Content="{x:Bind L.ModsRefresh, Mode=OneWay}" />
<TextBox PlaceholderText="{x:Bind L.ModsSearchPlaceholder, Mode=OneWay}" />
<Button AutomationProperties.Name="{x:Bind L.ModsRefresh, Mode=OneWay}" ... />
```

- `AutomationProperties.AutomationId` **不要翻译**（保持 ASCII，UI 测试依赖）；`AutomationProperties.Name` **要**翻译。
- `DataTemplate` 里如果 `x:DataType` 指向行 VM（`ModRowViewModel` 等），行 VM 必须继承 `LocalizedObservableObject`（它带 `L` 属性），然后用 `{x:Bind L.Foo, Mode=OneWay}`。若 `x:DataType` 是页面/VM，直接用页面上的 `L`。
- 带占位符的文案不要在 XAML 里拼，放到 VM 计算属性里 `T(...)`。
- `Run.Text` 这类内联元素尽量改造成多个 `TextBlock`／一个 VM 字符串，避免内联绑定。

## 4. VM / 代码后置用法

VM 基类从 `ObservableObject` 换成 `LocalizedObservableObject`（命名空间 `PdxLauncher.App.Localization`）：

```csharp
public sealed partial class ModsViewModel : LocalizedObservableObject
{
    public string HeaderText => T(Keys.ModsTitle);
    public string CountText => T(Keys.ModsEnabledCount, EnabledCount, TotalCount);
    // 需要在切语言时重建的集合/多值字符串，override Relocalize() 里做，然后 OnPropertyChanged(nameof(Xxx))
    protected override void Relocalize()
    {
        BuildFilters();              // 重建列表实例
        OnPropertyChanged(nameof(Filters));
        OnPropertyChanged(nameof(CountText));
    }
}
```

- `T(...)` / `L` 都是**每次访问时**去当前 `ResourceContext` 取值，所以计算属性天然会跟着变；只有"缓存到字段里的文案"才需要 `Relocalize()` 重建。
- 用枚举/选项类承载文案的（catalog），文案必须放在**属性 getter 里取**，不能在构造时算好存字段；集合是 `IReadOnlyList<Option>` 时，在 `Relocalize()` 里重建集合再 `OnPropertyChanged`。
- 代码后置里的对话框：

```csharp
var dialog = new ContentDialog
{
    XamlRoot = XamlRoot,
    Title = Localizer.Instance.ModsDeleteTitle,
    Content = Localizer.Instance.ModsDeleteBody,
    PrimaryButtonText = Localizer.Instance.CommonDelete,
    CloseButtonText = Localizer.Instance.CommonCancel,
};
```

- `Title`/`Content` 需要带参数时用 `Localizer.Instance.T(Keys.Xxx, arg)`。

## 5. 不要翻译的东西

- 日志：`AppServices.Logger.*`、`ILogger`、`Debug.WriteLine` 的消息保持中文。
- 代码注释、XML 文档注释保持中文。
- 文件路径、JSON 键、注册表值、枚举持久化值、`AutomationId`、`Tag`、`x:Name`。
- 商店/发行商/游戏名等**数据**（来自 Steam/Paradox 的字符串）不翻译。
- 例外：Steam 工坊标签的**展示名**要翻译（`WorkshopTagText` / `WorkshopTagKeys`）；发给 Steam 的机器名保持英文。工坊筛选、Mod Studio 上传标签与 Mod 详情标签都按展示名显示。
- 唯一例外：给用户看的异常/失败提示（对话框、状态栏文案）**要**翻译。

## 6. 数字与格式

- 字节大小用 `PdxLauncher.App.Localization.SizeFormatter.Format(long)`。
- 日期时间用 `ToString("g", CultureInfo.CurrentCulture)`；`Localizer` 已经设置了 `CultureInfo.DefaultThreadCurrentUICulture`。

## 7. 验证

```bash
bash tools/build-app.sh -v q     # 0 警告 0 错误（生成器会在编译期校验键集）
dotnet "tests/PdxLauncher.Checks/bin/Release/net10.0/PdxLauncher.Checks.dll"
```

自查残留（PowerShell/终端里都可用）：

```bash
grep -nP '"[^"]*[\x{4e00}-\x{9fff}]' <你的文件>.cs
grep -nP '(Text|Content|Header|PlaceholderText|Description|AutomationProperties\.Name)="[^"]*[\x{4e00}-\x{9fff}]' <你的文件>.xaml
```

注释里的中文会命中，人工确认即可。

## 8. 边界

- 只改分配给你的文件；其它文件（尤其是 `Localization/*.cs`、`Strings/*/Resources.resw`、`PdxLauncher.Localization.Generator`）不要动。
- 需要公共键（比如"确定"）先在 `Strings/zh-CN/Resources.resw` 里找 `Common*`，没有就在任务里报给 Lead，不要自己加。
- 不要执行 `git add`/`commit`/`checkout` 等改变仓库状态的命令。
