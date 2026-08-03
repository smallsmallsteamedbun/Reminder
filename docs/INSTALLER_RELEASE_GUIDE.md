# Reminder 安装程序发布说明

## 1. 首轮发布范围

- 应用版本：`0.7.1`
- 目标体系结构：`win-x64`
- 发布方式：.NET 自包含发布
- 安装器：Inno Setup 7 生成的 `.exe`
- 安装包名称：`Reminder-Setup-0.7.1-win-x64.exe`

首轮不生成 `win-x86`、`win-arm64` 或便携压缩包。

## 2. 项目目录

```text
Reminder\
├─ installer\
│  └─ Reminder.iss
├─ scripts\
│  └─ Publish-Installer.ps1
└─ artifacts\                    # 自动生成并被 Git 忽略
   ├─ publish\
   │  └─ win-x64\                # .NET 自包含发布目录
   └─ installer\
      └─ Reminder-Setup-0.7.1-win-x64.exe
```

源代码、安装器脚本和发布脚本属于项目内容；`artifacts\` 中的发布中间文件和最终安装包不进入源码提交。需要公开发布时，把最终 `.exe` 单独上传到同一 GitHub 仓库的 Releases。

## 3. 生成安装程序

在 `D:\Reminder` 打开 PowerShell 后执行：

```powershell
.\scripts\Publish-Installer.ps1
```

脚本会依次：

1. 从 `Reminder.App.csproj` 读取当前版本号；
2. 清理项目内旧的 `artifacts\publish\win-x64` 和 `artifacts\installer`；
3. 执行 `Release`、`win-x64`、自包含发布；
4. 调用已安装的 Inno Setup 7 编译器；
5. 输出安装包路径、文件大小和 SHA-256。

如果 Inno Setup 安装到了其他目录，可以显式传入编译器路径：

```powershell
.\scripts\Publish-Installer.ps1 -IsccPath "其他路径\ISCC.exe"
```

脚本默认先执行一次对应运行时的 NuGet 还原。已经完成 `win-x64` 还原且当前处于离线环境时，可以使用：

```powershell
.\scripts\Publish-Installer.ps1 -SkipRestore
```

## 4. 安装与覆盖安装行为

- 安装器允许用户自行选择安装目录，包括 D 盘。
- 默认安装位置采用 Windows 的程序安装目录。
- 安装器创建开始菜单快捷方式，并允许用户选择是否创建桌面快捷方式。
- 程序文件保持只读边界；只有相对 `Data` 子目录获得普通用户运行所需的写权限。
- 安装或覆盖安装前，如果 Reminder 正在运行，用户必须先从系统托盘右键菜单正常退出。
- 使用相同安装器或更高版本安装器覆盖安装时保留完整的 `Data` 目录。
- 安装完成后可以直接启动 Reminder。

## 5. 卸载行为

交互卸载开始前，卸载器会使用 Windows 原生 Task Dialog 要求用户选择：

- `保留 Data`：移除程序文件、快捷方式、卸载入口和当前用户的 Reminder 开机启动项，保留完整数据目录；
- `删除 Data`：执行上述清理，并删除完整的 `Data` 目录；
- `取消卸载`：不进行卸载。

无人值守卸载默认保留 `Data`。仅供自动验证或明确的数据清理流程使用时，可以传入 `/DELETEDATA=1` 删除数据。

## 6. 当前发布边界

当前安装包尚未配置代码签名证书，因此 Windows 可能显示“未知发布者”或 SmartScreen 提示。安装包可以用于本机与受信任测试环境；面向公开用户正式分发前，仍需配置代码签名和可信更新校验。
