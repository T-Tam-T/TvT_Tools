# TvT_Tools

Unity 编辑器工具脚本合集（Editor 扩展），由 `Editor_2026_7_17` 迁移而来，使用 Git 进行版本管理。

## 使用方式

把本文件夹（或需要的子文件夹）复制到 Unity 工程的 `Assets/Editor/` 下即可。
所有工具菜单都在 **`Tools / TvTTools`**、**`Assets / TvTTools`**、**`GameObject / TvTTools`** 下。

## 需要的包

| 包 | 用途 | 说明 |
| --- | --- | --- |
| `com.unity.ugui` | `TextureDeduplicator.cs` 用到 `UnityEngine.UI`（Image / RawImage） | 编辑器自带，一般无需额外安装 |

> 除上表外，本工具集只用 Unity 内置的 UnityEngine / UnityEditor API，**不依赖额外第三方包**。

## 目录结构

```
TvT_Tools/
├─ PrefabExporterWindow/   Prefab 导出工具
├─ ResourceManager/        资源管理器（多标签资源检查/整理）
├─ TXTvisualWindow/        TXT 可视化阅读窗口
├─ Editor.asmdef           根目录脚本的程序集定义（Editor 专用，无外部引用）
└─ *.cs                    各独立编辑器工具（见下表）
```

## 工具清单

### 根目录独立工具

| 文件 | 菜单入口 | 说明 |
| --- | --- | --- |
| `TvTCopyHelper.cs` | `Assets/TvTTools/TvT 智能复制 %#d` 等 | **智能复制**：选中材质/贴图按规则复制并自动命名；Hierarchy 里对**粒子 / 模型（Mesh / SkinnedMesh Renderer）**对象智能复制（复制对象 + 其材质）。含 `TvT 复制设置` 窗口 |
| `ResourceManager/` | `Tools/TvTTools/资源管理器` | **资源管理器**，见下方专节 |
| `GlobalResourceCheckerWindow.cs` | `Tools/TvTTools/全局资源检查器` | 全工程资源检查（按条件批量筛查资源） |
| `TextureDeduplicator.cs` | `Tools/TvTTools/贴图去重工具` | 贴图查重工具（需 `com.unity.ugui`） |
| `Texture Splitter.cs` | `Tools/TvTTools/纹理分割器` | 把一张贴图切分成多张 |
| `TextureImportConfig.cs` | `Tools/TvTTools/贴图压缩格式/…` | 批量应用 / 预览贴图压缩格式设置 |
| `BulkRenameWindow.cs` | `Tools/TvTTools/批量改名` | 批量重命名 |
| `AnimationRenameWindow.cs` | `Tools/TvTTools/批量改名(动画保持)` | 批量改名，并同步修正动画绑定路径（动画不丢） |
| `BatchClearMaterials.cs` | `Assets/TvTTools/去除模型材质` | 清除模型上的材质 / 材质贴图 |
| `BatchFolderCreator.cs` | `Assets/TvTTools/文件夹/…` | 批量创建文件夹、快速创建特效资源文件夹、复制文件夹路径 |
| `FolderTag.cs` | `Assets/TvTTools/文件夹标记/…`、`Window/文件夹标记管理器` | 在 Project 里给文件夹加彩色标记 |
| `SLGNamingTool.cs` | `Assets/TvTTools/SLG规范化命名及路径` | SLG 规范命名 / 路径整理；`Tools/TvTTools/SLG规范化命名/切换 FX_ 前缀智能补全` |
| `CustomParticleSystemMenu.cs` | `GameObject/TvTTools/创建空粒子发射器` | 快速创建空粒子发射器 |
| `PrefabExporterWindow/` | `Tools/TvTTools/Prefab导出工具` | 按 Excel 表批量导出 Prefab |
| `TXTvisualWindow/` | `Tools/TvTTools/TXTvisual` | TXT 可视化阅读窗口 |
| `SpineSettings.asset`、`常用.preset` | — | 工具的默认配置 / 预设资源 |

### 资源管理器（`ResourceManager/`）

菜单：`Tools / TvTTools / 资源管理器`（另有 `条件设置`、`未引用资源检测与移动`）。

把场景对象 / 预制体拖进左侧列表 → 点「分析所选对象」，即可在各标签页查看与整理资源。

**主标签页**

| 标签 | 内容 |
| --- | --- |
| 概览 | 资源总览、问题汇总 |
| 粒子 | 粒子系统列表与条件检查 |
| 材质 | 材质列表；顶部可切换「按对象平铺（默认）」/「按 Shader 分组」 |
| 贴图 | 贴图列表与平台设置检查 |
| 网格 | 网格列表（顶点/三角面/包围盒等） |
| 导出 | 批量导出 / 重命名等辅助功能 |

**拓展标签页**（顶部「拓展 ▾」下拉切换）

| 模块 | 内容 |
| --- | --- |
| 改色 | 粒子改色（读取/复制/粘贴各颜色模块色值） |
| 动画 | 动画剪辑查看与整理 |
| 复制 | **资源复制**：复制贴图/材质/模型并（可选）重新指认给检测对象 |
| 去重 | **贴图去重**（按材质分组，展开看「重复贴图 → 原贴图」，一次克隆只产出一个 `_dedup`）+ **材质球去重**（参数完全一致的材质合并为同一个，可下拉选择保留项并定位） |
| 冗余 | 冗余检测（如 shader 开关关闭但贴图槽非空等） |
| 回档 | **回档**：读取 `Assets/ResourceManagerRollback/` 下的回档文件，对比「当前资源 → 回档资源」并一键还原 |

**回档机制**

「资源复制」和「材质球去重 / 贴图去重」在修改引用前会自动生成回档文件
（`Assets/ResourceManagerRollback/回档_日期_时间.json`，带来源标记），
在「拓展 → 回档」里选日期 → 看对比 → 「确认回档」即可还原；历史资源已被删除时会提示无法还原。

## 备注

- 根目录脚本由 `Editor.asmdef` 统一编译为 `Editor` 程序集（仅 Editor 平台）。
- `TvT_TestProject/`（如果存在）只是本地用来验证编译的测试工程，不属于工具本体。
