# 2026-07-30 - 静默启动说明与开机自启动测试

- 会话 ID：`2026-07-30-001-silent-start-autostart-test`
- 近原始记录：`memory/raw/2026-07/2026-07-30-001-silent-start-autostart-test.user.md`
- 用户消息范围：U001-U001
- 状态：已完成

## 摘要

按照用户要求取消静默启动旁的小问号和悬停提示，把后台模式说明直接显示为静默启动下方的常驻小字；同步更新需求、设置设计、阶段计划和验证文档。确认当前开机启动实现能够直接注册 Visual Studio 的 Debug 输出，并整理了无需安装包的真实 Windows 登录测试流程。

## 关键要点

- 静默启动下方显示“程序启动时以后台模式运行，不显示程序面板”。
- 设置项不再显示问号，也不再依赖鼠标悬停才能查看说明。
- 开机启动通过当前用户 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 注册当前实际程序入口。
- Visual Studio 调试阶段可以先开启设置、退出旧实例，再注销并登录 Windows 验证 Debug 产物自动启动。
- 启用静默启动时，登录后没有主窗口属于正常结果，应通过系统托盘确认程序已经运行。

## 变更

- 修改 `src/Reminder.App/UI/Views/MainWindow.xaml`。
- 更新 `docs/REQUIREMENTS.md`、`docs/SETTINGS_PAGE_DESIGN.md`、`docs/NEXT_STAGE_THEME_STARTUP_PLAN.md`。
- 在 `docs/versions/UNRELEASED_THEME_STARTUP_VALIDATION.md` 增加 Visual Studio 调试阶段的开机启动复核步骤。
- Debug 与 Release 构建均通过，0 警告、0 错误。

## 决策

- 静默启动说明采用常驻小字，不再使用问号悬停交互。来源：U001。

## 后续事项

- 用户在普通 Windows 登录流程中复核开机自动启动、静默托盘和关闭启动项后的实际效果。

## 不确定项

- 无。
