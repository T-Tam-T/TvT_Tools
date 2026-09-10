# TvT_Tools

Unity 编辑器工具脚本合集（Editor 扩展），由 `Editor_2026_7_17` 迁移而来，使用 Git 进行版本管理。

## 目录说明
- `PrefabExporterWindow/` — Prefab 导出工具
- `ResourceManager/` — 资源管理器
- `TXTvisualWindow/` — TXT 可视化阅读窗口
- 根目录 `*.cs` — 各独立编辑器工具

## 使用方式
把本文件夹（或需要的子文件夹）复制到 Unity 工程的 `Assets/Editor/` 下使用。

## Timeline 轨道颜色工具（`TimelineColorTool.cs` + `TimelineTrackColors.cs`）

菜单：`Tools / TvTTools / Timeline 轨道颜色 & 背景`

**⚠ Unity 限制（com.unity.timeline 1.7+）**：从 Timeline 1.7 起 `TrackAsset` 里已经没有
`m_Color` 字段和 `color` 属性，Unity 不再保存"每条轨道各自的颜色"。所以本工具走 Unity 现存的
两条官方途径：

| 功能 | 作用范围 | 实现 |
| --- | --- | --- |
| 逐条轨道色（轨道列表里的颜色选择器 / 快捷色） | Unity 只把它画在 **表头左侧那条窄色块** | 自定义 `TrackEditor.GetTrackOptions()` 返回的 `TrackDrawOptions.trackColor` |
| 轨道类型色（下方"轨道类型色"区块、每行的「同型」按钮） | 表头色块 **+ 每个 clip 底部那条细线 + Group 轨道整块背景**，但同类型轨道共用一色 | 写入 `UnityEditor.Timeline.TrackResourceCache` 的按类型颜色缓存（反射，内部 API） |
| 整行染色（默认开启，可调不透明度） | **整行**（表头列 + clip 区），Group / Marker 行也一起染 | 借 Timeline 自带的 overlay 管线（静音变暗走的就是它）给每行铺一层半透明色罩 |

**为什么"整条轨道变色"要自己做**：Unity 只用轨道颜色画三处 —— 表头左侧窄色块、每个 clip 底部细线、
Group 轨道整块背景；clip 本体（`customSkin.clipBckg`）和表头背景（`colorTrackHeaderBackground`）都是皮肤
固定色，跟轨道颜色无关，Group 轨道更是连那条窄色块都不画（`TimelineGroupGUI` 自己重写了 Draw）。
所以"整行染色"由工具在 Timeline 窗口上叠一层色罩实现：行矩形取自轨道 GUI 的
`rowRect × treeViewToWindowTransformation`（窗口客户坐标），颜色取「逐条颜色」，没设则取显式设过的「类型色」。

- clip 底色的"逐条不同"在 1.7.6 没有官方扩展点，不做（抢注内部 `ClipEditor` 会破坏音频波形、
  错误提示、子时间轴等行为）。
- 为了让逐条颜色对 **ActivationTrack / MarkerTrack（含 SignalTrack）** 也生效，工具注册了这两个
  类型的自定义 `TrackEditor`，并原样复刻了 Unity 内部编辑器的行为（激活轨道的绑定报错提示、
  新建时自动生成 `Active` clip、Marker 轨道高度 24）。
- 颜色数据存在 **`<工程根>/ProjectSettings/TvT_TimelineColors.json`**（跟工程走、可提交 git 共享）。
  换皮肤 / 域重载后工具会自动把类型色补写回 Unity 的缓存。

