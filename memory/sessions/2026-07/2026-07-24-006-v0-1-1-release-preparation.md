# 2026-07-24 - 准备 0.1.1 版本文档

- 会话 ID：`2026-07-24-006-v0-1-1-release-preparation`
- 近原始记录：`memory/raw/2026-07/2026-07-24-006-v0-1-1-release-preparation.user.md`
- 用户消息范围：U001-U001
- 状态：已完成

## 摘要

用户准备自行把 Reminder `0.1.1` 上传到 GitHub，要求助手只在本地整理版本文档，并在完成后恢复下一阶段实施范围。本次将版本历史和各版本实施、验证文件集中迁入 `docs/versions/`，补充 `0.1.1` 更新内容，同步项目与界面版本号并重新完成 Debug、Release 构建。没有创建提交，也没有上传远程仓库。

## 关键要点

- `0.1.1` 记录 `0.1.0` 发布后完成的平滑滚动与定位、删除确认与重排、通知中心正文重新展示和版本号位置调整。
- 动画刷新率自适应尚未写入程序，不作为 `0.1.1` 已完成功能；它仍是下一阶段的第一项。
- 版本文件以后集中存放在 `docs/versions/`，用户可见总记录位于 `docs/versions/VERSION_HISTORY.md`。
- 当前应用版本、项目版本、需求文档和版本记录均已同步为 `0.1.1`。
- GitHub 上传继续由用户自己执行。

## 变更

- 新建 `docs/versions/README.md`。
- 迁移 `VERSION_HISTORY.md`、`V0.0.0_IMPLEMENTATION_PLAN.md`、`V0.0.0_VALIDATION.md`。
- 保留独立的 `V0.1.0_VALIDATION.md`，新增整理后的 `V0.1.1_VALIDATION.md`。
- 更新 `AGENTS.md`、`docs/REQUIREMENTS.md`、`docs/NEXT_IMPLEMENTATION_PLAN.md` 和 `docs/GITHUB_UPLOAD_GUIDE.md` 中的版本及路径。
- 将 `Reminder.App.csproj` 和 `AppMetadata.Version` 更新为 `0.1.1`。
- Debug、Release 构建均为 0 个警告、0 个错误；Release 文件版本为 `0.1.1.0`。
- 本次未创建 Git 提交，未执行 `git push`。

## 决策

- 版本文档集中存放在 `docs/versions/`；见 `memory/decisions.md#D-2026-07-24-014---版本文档集中存放在-docsversions`。

## 后续事项

- 等待用户自行上传 `0.1.1` 后明确通知开始下一阶段。
- 下一阶段先移除动画固定 60 帧上限，再实现固定间隔和指定时间事件的共同结构、动态表单、循环计划、终止次数、共享调度、输入校验和日历边界回归。
- 完整退出、数据保存和启动恢复仍延后到事件模型完成以后。

## 不确定项

- 无。
