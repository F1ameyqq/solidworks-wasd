# Changelog

## v0.1.0 — 未发布

首个可用版本。

- WASD 平移、Q/E 缩放、Shift 加速、F9 开关
- 键盘钩子装在 SolidWorks 进程内，按键被吞掉，不与 SW 快捷键冲突
- 仅在「SW 前台 + 焦点不在文本框」时拦截
- 平移速度按当前视野包围盒自适应
- 参数外置 `swwasd.ini`，按两次 F9 热加载，无需重编译
- 构建零依赖（用 Windows 自带的 csc，不需要 Visual Studio）
- 安装/卸载脚本自带 UAC 提权，安装前检查 SW 是否在运行

### 已知问题

- 只在 SolidWorks 2026 上实测
- 正交视图下没有第一人称效果（这是 SW 的限制，不是 bug）
