# Reminder GitHub 上传操作指南

- 项目目录：`D:\Reminder`
- 当前分支：`main`
- 远程仓库：`origin`
- 仓库地址：`https://github.com/smallsmallsteamedbun/Reminder.git`

本文档用于以后由用户自行把本地 Reminder 项目上传到 GitHub。以下命令应在 `D:\Reminder` 文件夹中打开的 PowerShell 或 Visual Studio 终端里逐行执行。

## 1. 上传前准备

先保存 Visual Studio 中的全部文件，然后打开项目终端并确认当前目录：

```powershell
cd D:\Reminder
git status
```

`git status` 只检查当前改动，不会上传文件。

## 2. 提交本地修改

如果本次发布消息为“mmmmm”，依次执行：

```powershell
git add -A
git commit -m "mmmmm"
```

- `git add -A` 把项目中的新增、修改和删除文件加入本次提交。
- `git commit -m "mmmmm"` 在本地创建提交，提交消息为“mmmmm”。
- 这两条命令都不会把项目上传到 GitHub。
- 如果显示 `nothing to commit`，表示当前没有尚未提交的修改。

## 3. 同步远程变化

在上传前执行：

```powershell
git pull --rebase origin main
```

该命令先获取 GitHub 上的 `main` 分支，并把刚才的本地提交接到远程最新提交之后。

如果出现冲突，不要执行强制推送，也不要删除冲突文件。先停止后续步骤并处理冲突；不确定时可以把终端输出交给 Codex 检查。

## 4. 上传到 GitHub

确认前面的命令成功后执行：

```powershell
git push origin main
```

这一条命令才会真正把本地提交上传到 GitHub。首次在当前电脑操作时，GitHub 可能要求通过浏览器登录或确认权限，按提示完成即可。

## 5. 上传后检查

```powershell
git status
git log -1 --oneline
```

正常情况下：

- `git status` 显示工作区没有未提交修改；
- `git log -1 --oneline` 显示刚才的“mmmmm”提交；
- 打开 GitHub 仓库页面后可以看到相同提交。

## 6. 完整命令汇总

```powershell
cd D:\Reminder
git status
git add -A
git commit -m "mmmmm"
git pull --rebase origin main
git push origin main
git status
git log -1 --oneline
```

需要更换发布消息时，只修改 `git commit -m` 后面双引号中的内容。例如：

```powershell
git commit -m "发布0.2.0"
```

## 7. 注意事项

- 不要使用 `git push --force` 或 `git push -f`，除非已经明确理解会覆盖远程历史。
- 不要在 `git pull --rebase` 出现冲突后继续执行 `git push`。
- 每次版本号发生变化时，应先同步程序显示版本、项目元数据、`docs/REQUIREMENTS.md` 和 `docs/versions/VERSION_HISTORY.md`，再创建发布提交。
- `git add -A` 会包含项目内的 `memory/` 记录；这符合 Reminder 当前的项目记忆版本控制规则。
