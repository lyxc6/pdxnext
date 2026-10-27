# 界面文案（i18n）约定

目标：全 UI 双语（`zh-CN` 默认、`en-US`），**切换语言后立即生效**（不重启）。

## 1. 文案源

```
tools/i18n/zh-CN/<area>.json
tools/i18n/en-US/<area>.json
```

- 扁平键值 JSON，键里不要出现嵌套对象。
- 两种语言**必须同键集**，缺少或多出都会让生成器报错。
- 占用位统一写成 `{0}`、`{1}`…；两种语言的占位符**数量与序号必须一致**。
- 值不能为空、首尾不能有空格。

### 键命名

`<area>.<主题>[.<子项>]`，每段正则 `^[a-z][a-zA-Z0-9]*$`，至少两段。
同一个键**不允许**出现在两个 `<area>.json` 里（会报"重复键"），所以每个 area 文件只放自己前缀的键。

标识符由生成器从键推导：每段首字母大写后拼接。
`app.name` → `AppName`；`common.copyPath` → `CommonCopyPath`；`mods.card.enabledCount` → `ModsCardEnabledCount`。
所以**不要**在同一个 area 里写出会撞名的键（`common.copyPath` 与 `common.copy.path` 都会变成 `CommonCopyPath`）。

## 2. 生成

```bash
PYTHONIOENCODING=utf-8 python tools/gen-i18n.py          # 生成 Strings.g.cs
PYTHONIOENCODING=utf-8 python tools/gen-i18n.py --check  # 只校验（CI 用）
```

产物 `src/PdxLauncher.App/Localization/Strings.g.cs` 是机器生成文件，**禁止手改**。它也提供 `internal static class Keys`（每个键一个 `public const string`），VM 里用 `Keys.ModsTitle` 比裸字符串安全。

## 3. XAML 用法

页面/窗口类里加：

```csharp
using PdxLauncher.App.Localization;

public Localizer L => Localizer.Instance;
```

绑定一律 **`Mode=OneWay`**（x:Bind 默认是 OneTime，切语言不会更新）：

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

- `T(...)` / `L` 都是**每次访问时**去当前语言表里取值，所以计算属性天然会跟着变；只有"缓存到字段里的文案"才需要 `Relocalize()` 重建。
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
- 唯一例外：给用户看的异常/失败提示（对话框、状态栏文案）**要**翻译。

## 6. 数字与格式

- 字节大小用 `PdxLauncher.App.Localization.SizeFormatter.Format(long)`。
- 日期时间用 `ToString("g", CultureInfo.CurrentCulture)`；`Localizer` 已经设置了 `CultureInfo.DefaultThreadCurrentUICulture`。

## 7. 验证

```bash
bash tools/build-app.sh -v q     # 已内置 gen-i18n + 构建锁，输出最后 20 行
```

必须 **0 警告 0 错误**。多个智能体同时构建由脚本里的锁串行化，看到等锁是正常的。

自查残留（PowerShell/终端里都可用）：

```bash
grep -nP '"[^"]*[\x{4e00}-\x{9fff}]' <你的文件>.cs
grep -nP '(Text|Content|Header|PlaceholderText|Description|AutomationProperties\.Name)="[^"]*[\x{4e00}-\x{9fff}]' <你的文件>.xaml
```

注释里的中文会命中，人工确认即可。

## 8. 边界

- 只改分配给你的文件；其它文件（尤其是 `Localization/*.cs`、`common.json`、别人的 `<area>.json`、`tools/gen-i18n.py`、`tools/build-app.sh`）不要动。
- 需要公共键（比如"确定"）先看 `tools/i18n/zh-CN/common.json` 里有没有，没有就在任务里报给 Lead，不要自己加。
- 不要执行 `git add`/`commit`/`checkout` 等改变仓库状态的命令。
