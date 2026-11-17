# SW-WASD — 给 SolidWorks 加 WASD 视角控制

用键盘操SolidWorks 的视角：**W/S 上下平移、A/D 左右平移、Q/E 缩放**，不用再反复「中键拖 + 滚轮」。

一个原生 SolidWorks 插件（Add-in），不是外挂脚本——键盘钩子跑在 SW 自己进程里，按键会被真正**吞掉**，不会和 SW 的快捷键打架；而且只在「SW 是前台窗口」且「焦点不在输入框」时才拦截，你在对话框里打字照常。

```
F9    开关
W / S 上 / 下平移
A / D 左 / 右平移
Q / E 缩小 / 放大
Shift 加速
```

---

## 安装（三步）

需要：Windows + SolidWorks（2018 及以上，x64）+ .NET Framework 4.x（Win10/11 自带）。

```powershell
# 1. 构建（不用装 Visual Studio）
powershell -ExecutionPolicy Bypass -File scripts\build.ps1

# 2. 安装（会弹一次 UAC）
powershell -ExecutionPolicy Bypass -File scripts\install.ps1

# 3. 重启 SolidWorks → 工具 → 插件 → 勾选「WASD View」
```

装完打开任意零件，按 **F9** 就能用。

> `install.ps1` 做三件事：把 SolidWorks 的 interop DLL 拷到 `bin\`、用 RegAsm 注册 COM 类、在 `HKLM\SOFTWARE\SolidWorks\Addins` 下登记插件。

---

## 配置

参数在 `bin\swwasd.ini`，**改完不用重编译、不用重启 SW**——按两次 F9（关→开）就热加载：

```ini
# 平移速度 = 视野包围盒对角线 × 这个系数（每帧）
pan=0.0001

# 缩放倍率，每帧。1.03 ≈ 60fps 下每秒放大约 5 倍
zoom=1.03

# 刷新间隔（毫秒），16 ≈ 60fps
frame=16
```

手感调法：嫌慢把 `pan` 加到 `0.0002`，嫌快减到 `0.00005`。`pan` 是按**当前视野大小**自适应的，所以放大看细节时平移会自动变慢，不用两套参数。

---

## 卸载

```powershell
powershell -ExecutionPolicy Bypass -File scripts\uninstall.ps1
```

（会反注册 COM、删掉插件登记项。`bin\` 目录自己删。）

---

## 构建细节

`scripts\build.ps1` 干的事，就一条命令：

```
csc -target:library -platform:x64 -out:bin\SwWasd.dll ^
    -r:<SW安装目录>\SolidWorks.Interop.sldworks.dll ^
    -r:<SW安装目录>\SolidWorks.Interop.swpublished.dll ^
    -r:System.Windows.Forms.dll -r:System.Drawing.dll ^
    src\SwWasdAddin.cs
```

编译器用的是 Windows 自带的 `.NET Framework 4.x` 的 csc，所以**不需要 Visual Studio**。

SolidWorks 安装目录按这个顺序找：`-SwDir` 参数 → 环境变量 `SWDIR` → `C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS` → 在 `Program Files` 下扫。

---

## 工作原理

| 做什么 | 用什么 |
|---|---|
| 拦按键 | `SetWindowsHookEx(WH_KEYBOARD_LL)`，装在 SW 进程内，返回 1 吞掉 |
| 判断该不该拦 | 前台窗口 PID == 本进程 PID，且焦点控件 class 不是 Edit / RichEdit |
| 平移 | `IModelView.TranslateBy(x, y)`，单位是**模型单位（米）**，所以按视野对角线自适应 |
| 缩放 | `IModelView.ZoomByFactor(f)` |
| 不卡顿 | `IModelView.StartDynamics()` / `StopDynamics()` |
| 每帧驱动 | `System.Windows.Forms.Timer`（跑在 SW 主线程，COM 调用不用跨线程 marshal） |

---

## 已知限制

- **只支持 Windows + SolidWorks**。API 是 SW 专有的。
- **正交视图下"前后移动"没有意义**——SolidWorks 默认正交投影，往前推和放大在屏幕上是同一个效果。所以这里 W/S 做的是平移而不是推进。想要真正的第一人称漫游，得开透视（`AddPerspective`）再自己算相机位姿，那是另一个量级的工作。
- 平移的**绝对速度**依赖当前视图范围：视图里东西越小，平移越慢。这是刻意的（缩放后自动变慢），但如果你想要恒定速度，把 `GetPanStep()` 里的自适应改成常数。
- 已经测过 SolidWorks 2026。更老的版本没实测，`SetAddinCallbackInfo2` 之类的 API 在各版本间有细微差别，遇到问题请提 issue 并附 `bin\addin.log`。

---

## 排错

插件加载后不工作，先看 **`bin\addin.log`**，里面有每一步的记录：

```
=== ConnectToSW 进入, ThisSW=... Cookie=1
钩子句柄=140728... err=0
配置已读: pan=0.0001 zoom=1.03
F9 -> 开
执行运动 k=1 W=True A=False S=False D=False
```

常见情况：

| 日志表现 | 原因 |
|---|---|
| 日志文件都不存在 | 插件没被加载。检查 工具→插件 里有没有勾上；`bin\` 下有没有 `SolidWorks.Interop.*.dll` |
| 有 `ConnectToSW` 但按 F9 没 `F9 ->` | 键盘钩子没装上（权限 / 安全软件拦截） |
| 有 `F9 ->` 但没 `执行运动` | 焦点判定把你挡了，或者 `held` 状态没记上 |
| 有 `执行运动` 但画面不动 | `TranslateBy` 单位问题，调 `pan` |
| 按键完全没反应，SW 里也没有 | 插件列表里没勾，或需要重启 SW |

---

## 法律声明（重要）

**本仓库不包含、也不分发任何 SolidWorks 的文件。**

`SolidWorks.Interop.sldworks.dll`、`SolidWorks.Interop.swpublished.dll` 等是 Dassault Systèmes 的专有文件，受其许可协议约束。构建时从**你自己的 SolidWorks 安装目录**引用，安装时复制到你自己的 `bin\`，这些文件都在 `.gitignore` 里，不会被提交。

使用本插件需要你自己持有合法的 SolidWorks 授权。

---

## License

MIT — 见 [LICENSE](LICENSE)。
