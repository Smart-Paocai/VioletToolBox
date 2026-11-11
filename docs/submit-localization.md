# 日语翻译的一键提交

在 Windows 上双击仓库根目录的 `submit-localization.cmd`。脚本将依次执行 Release 构建、翻译回归检查、暂存本次翻译相关文件、创建提交、推送到 origin 指向的 fork，并向原仓库创建 PR。相同分支已有未关闭 PR 时会复用该 PR。

需要安装 Git 和 .NET 8 SDK，并配置 `git config user.name`、`git config user.email`。Git 推送使用 Git Credential Manager 中已登录的账号或已配置的 SSH 密钥。创建 PR 的 API 认证使用 Git Credential Manager 中的令牌，也可使用环境变量 `GH_TOKEN` 或 `GITHUB_TOKEN`，令牌需要具有创建 PR 的权限。环境变量只用于 API 请求，不替代 Git 推送认证。令牌不会写入文件。

从 PowerShell 运行：

```powershell
.\scripts\submit-localization.ps1
```

只构建和检查，不执行 commit、push、创建 PR，也不更改暂存区：

```powershell
.\scripts\submit-localization.ps1 -CheckOnly
```

只验证本机已有 GitHub 凭据及账号，不构建、不提交、不推送、不创建 PR：

```powershell
.\scripts\submit-localization.ps1 -CheckAuthentication
```

凭据请求通过进程的标准输入按 ASCII 原始字节发送，避免 Windows PowerShell 的管道或终端编码损坏 Git 的 `protocol` 字段。不会输出或保存令牌；缺少凭据时会直接提示登录，不会弹出交互登录窗口。

自定义提交信息和 PR 标题：

```powershell
.\scripts\submit-localization.ps1 -Message "fix: refine Japanese terminology" -Title "Refine Japanese translations"
```

脚本只暂存其中列出的翻译、相关逻辑修复、图标、文档和检查文件；不包含构建产物。发现已暂存的其他文件时停止，便于先整理提交范围。若当前位于默认分支，脚本会创建 `codex/japanese-localization-时间戳` 分支；否则使用当前分支。脚本不执行强制推送、合并或自动同步上游。

构建日志位于 `artifacts/submit-build.log`，检查日志位于 `artifacts/submit-tests.log`。如果推送成功而创建 PR 失败，解决认证或网络问题后重新运行即可。PR 说明正文取自 `docs/pull-request.ja.md`，提交前可自行修改。

只有输出包含完整 PR 链接时，才表示已确认 PR 存在。若先前版本仅显示空白的 `Updated existing PR:`，请使用修正后的脚本重新运行。

脚本流程自身的回归检查（模拟 Git 和 HTTP，不执行真实提交、推送或创建 PR）：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/SubmissionScriptChecks.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/GitHubAuthenticationChecks.ps1
```
