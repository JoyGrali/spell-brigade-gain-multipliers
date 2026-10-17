# Spell Brigade — Gain Multipliers / Stat Editor

《The Spell Brigade（咒语旅团）》的 MelonLoader 模组：局内法力（经验）倍率、结算金币倍率、玩家属性编辑、局外巫师等级直接设置。带游戏内面板（uGUI + TMP，中文界面）。

- 游戏版本：1.2.0.20127（IL2CPP，Unity 6）
- 依赖：[MelonLoader](https://melonloader.co/) 0.7.x
- 许可证：MIT

## 安装

### 方式一：双击安装（推荐）

1. 解压压缩包到任意位置（不要只解压 exe）；
2. 双击 `安装.bat`；
3. 安装器会自动从 Steam 注册表 / `libraryfolders.vdf` 找游戏目录，找到后复制 DLL 到 `Mods\`；
   找不到会提示你粘贴路径（该文件夹里应有 `TheSpellBrigade.exe`）；
4. 游戏装在 `Program Files` 下时会自动申请管理员权限。

### 方式二：手动放置

把 `Mods\SpellBrigadeGainMultipliers.dll` 复制到：

```
<游戏目录>\Mods\
```

即 `<Steam>\steamapps\common\The Spell Brigade\Mods\`。

> 需要先装好 MelonLoader。装好后启动游戏会弹出 MelonLoader 控制台，属正常现象。

## 卸载

- 双击 `卸载.bat`（同样会自动找目录）；或
- 手动删除 `<游戏目录>\Mods\SpellBrigadeGainMultipliers.dll`。

配置会保留在 `UserData\MelonPreferences.cfg` 的 `[GainMultipliers]` 段，重装后沿用。

## 使用

进游戏后左上角出现面板，**F8 显示/隐藏**，右上角 `隐藏` 收起。

### 顶部

- `> 倍率` / `> 属性`：切换两页；
- `[x] 启用修改`：**总开关**。取消勾选后倍数、金币、属性的补丁全部不生效（存档修复类钳制仍保留），适合"今天不想开 mod"的时候用，不用卸载。

### 倍率页

| 项 | 说明 |
|---|---|
| **法力（局内经验）** | 局内升级速度。`PartyLevelManager.TryAddExperience` 的入参 × 倍率 |
| **金币（结算）** | 一局结算金币。`RunGoldCalculator.Calculate` 返回的 `EarnedGold` 各分量 × 倍率，面板显示与进钱包一致 |
| **钱包兜底** | 默认 1.0，别动。只有金币倍率不生效时才用它 |
| **巫师等级（直接设置）** | 直接读写局外巫师等级（1~10），点数值框输入或 `-`/`+` 调整 |

> 局外巫师等级原本按**游玩分钟数**换算，游戏那套升阶算法在超出上限时会越界写出垃圾值，
> 所以本模组**不再提供巫师经验倍率**，改为直接设置等级。

按钮：`加 10 法力`（走 XP 钩子）、`加 10 金币`（走钱包钩子）、`模拟一次结算`（走金币钩子）、
`修复巫师等级`（把存档里的非法 rank 纠正回合法值）、`清零计数`、`恢复默认`。

长按 `+`/`-` 可以连续调整（0.35 秒后每 0.05 秒触发）。点数值框可直接打字，`Enter` 确定、`Esc` 取消。

### 属性页

第一列是游戏里的**真实属性值**，右边是你加的加成：

```
伤害 / 最大生命 / 移动速度 / 幸运 / 闪避 / 暴击率 / 暴击伤害 / 拾取范围 /
护甲 / 生命回复 / 重掷次数 / 复活次数 / 金币获取 / 法力获取 / 掉落率 / 投射物
```

实现方式是给每个存活玩家的 `PlayerStats` 挂一个自己的 `StaticStatModifier`，游戏自己的
`Stat.GetValue()` 会重新求和，所以游戏内属性面板和实际效果都会变。属性只在进局后生效（没玩家数据时显示 `还没有玩家数据`）。

## 配置文件

`<游戏目录>\UserData\MelonPreferences.cfg`：

```toml
[GainMultipliers]
Enabled = true
XpMultiplier = 2.0
GoldMultiplier = 2.0
GoldGrantMultiplier = 1.0
Verbose = false
ChineseLabels = true
Stat_Luck = 0.0
Stat_Dodge = 0.0
...
```

面板上每次改动都会延迟合并写回（最多 1 秒一次），不会刷盘。

## 实现要点

| 目标 | 补丁 |
|---|---|
| `PartyLevelManager.TryAddExperience(float)` | Prefix 改入参（法力倍率） |
| `RunGoldCalculator.Calculate(...)` | Postfix 缩放 `EarnedGold` 各分量（金币倍率） |
| `LocalPlayer.AddGold(int)` | Prefix（钱包兜底 + 观测） |
| `CharacterRankProgression.SetRankProgression(int, float)` | Prefix 钳制非法 rank/progress，避免存档被写坏 |
| `CharacterBadgeDisplay.Show(int, int)` | Prefix 钳制徽章索引 + Finalizer 兜底，避免坏数据让菜单卡死 |

- **防重复应用**：MelonLoader 会自动 Patch 程序集里所有 `[HarmonyPatch]` 类，所以不再手动 `PatchAll`；
  所有 Prefix/Postfix 用 `[ThreadStatic]` 嵌套深度守卫，只在外层生效。
- **面板**：IMGUI 在这个构建里既没有 CJK 字体（动态字体 API 被裁）也拿不到按钮事件，
  所以面板改用 uGUI + TextMeshPro 渲染（字体取游戏自带的 TMP 字体），点击仍用轮询命中检测。

## 自己编译

```powershell
cd SpellBrigadeGainMultipliers
pwsh -File .\build.ps1     # 生成 out\SpellBrigadeGainMultipliers.dll
pwsh -File .\package.ps1   # 生成 dist\SpellBrigadeGainMultipliers-<版本>.zip
```

构建脚本不使用 MSBuild：本机没有 .NET 6 targeting pack，所以直接用 SDK 自带的 `csc.dll`，
引用游戏自带的 .NET 6 运行时（`<游戏目录>\dotnet`）、`MelonLoader\net6` 和 `Il2CppAssemblies\*.dll`。
源码会先复制到 `%TEMP%` 的 ASCII 路径再编译（csc 处理不了中文路径）。

## 已知限制

- 局内法力倍率/金币倍率只在单机或你是主机时生效（服务端结算）。
- 联机使用属于单方面改动，组队前请先和队友说明。
- 游戏更新后 IL2CPP 代理会变，方法名失效需要重新生成并核对。
