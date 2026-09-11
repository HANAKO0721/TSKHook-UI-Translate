# GitHub 重新发布步骤

目标仓库：[HANAKO0721/TSKHook-UI-Translate](https://github.com/HANAKO0721/TSKHook-UI-Translate)。

## 1. 设置路径

在 PowerShell 中进入工作区根目录 `TSKTranslateTool`，执行：

```powershell
$workspace = (Get-Location).Path
$source = Join-Path $workspace 'dist/TSKHook_UI_v0.1.0'
$zip = Join-Path $workspace 'dist/TSKHook_UI_v0.1.0.zip'
$publish = Join-Path $workspace '.publish/original-preserved-20260911'
$repo = Join-Path $publish 'repo'
$remote = 'https://github.com/HANAKO0721/TSKHook-UI-Translate.git'
```

原目录和 ZIP 作为只读输入。所有 Git 操作和新增文档均在独立发布副本中完成。

## 2. 检查上传范围和隐私

枚举原目录及 ZIP 的全部文件，检查文本、DLL 字符串、API 地址、API Key、访问令牌及私钥标记。ZIP 包含 `.git`，因此还需解压读取 Git 对象并检查历史文本。只报告文件位置与结论，不输出疑似凭据原文。

本次检查了原目录的 406 个文件及 ZIP 的 406 个文件；每份均包含 238 个 Git 元数据文件，其中各有 212 个压缩 Git 对象。命中的 API 配置为示例域名及占位值，未发现真实 API 地址、API Key 或 SSH 私钥。ZIP 大小为 91,839,494 字节。

如果发现真实凭据，应停止上传相应内容，先确定处理方式。`.gitignore` 无法清理已经存在于 ZIP 或 Git 历史中的凭据。

## 3. 使用指定链接连接仓库

```powershell
git -c core.autocrlf=false clone --no-tags --single-branch --branch main $remote $repo
git -C $repo remote -v
```

`git clone` 初始化本地仓库并把上述 HTTPS 链接添加为 `origin`，无需重复执行 `git remote add origin`。若从空目录手动初始化，等价连接步骤如下：

```powershell
git init
git remote add origin 'https://github.com/HANAKO0721/TSKHook-UI-Translate.git'
git fetch origin main
git switch -c main --track origin/main
```

本次开始时，远程 `main` 的最新提交为空目录；恢复提交会接在它之后。

## 4. 复制原项目文件

```powershell
Get-ChildItem -LiteralPath $source -Force |
    Where-Object { $_.Name -ne '.git' } |
    ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $repo -Recurse -Force }
```

复制 168 个项目文件，包括原 `README.md`、`BUILDING.md`、`LICENSE` 和 `.gitignore`，保持字节内容一致。源目录中的 `.git` 是本地仓库元数据；发布副本使用克隆得到的 `.git`。

## 5. 补齐对应源码

原目录可见文件中已没有 `src/` 和 `build.ps1`，它们仍保存在原目录的 Git 提交 `81c1b306cd7d78ca0efe018c5a32c4cc7f19d648` 中。该提交的发布 DLL 与本次原目录 DLL 逐字节相同。

将该提交中的源码和构建脚本仅导出到发布副本：

```powershell
$sourceArchive = Join-Path $publish 'source-from-original-git.tar'
git -C $source archive --output=$sourceArchive 81c1b306cd7d78ca0efe018c5a32c4cc7f19d648 src build.ps1
tar -xf $sourceArchive -C $repo
```

本次实际从内存中的 Git archive 流恢复同一批 196 个文件。对应源码和构建脚本随仓库一起提供，原目录和 ZIP 保持不变。

## 6. 添加学习与非商业使用倡议

在发布副本新增 [NONCOMMERCIAL-NOTICE.md](NONCOMMERCIAL-NOTICE.md)，保留原 `LICENSE`。本文件是另一份新增文档，用来记录完整发布步骤。

项目继续采用 GPL-2.0。“仅供学习、请勿商用”是作者倡议，不限制 GPL 已授予的权利。依据见 [GNU GPLv2](https://www.gnu.org/licenses/old-licenses/gpl-2.0.en.html) 和 [GNU 关于非商业附加限制的说明](https://www.gnu.org/licenses/gpl-faq.en.html#NoMilitary)。

## 7. 核对并提交

```powershell
git -C $repo config user.name 'HANAKO0721'
git -C $repo config user.email '85883922+HANAKO0721@users.noreply.github.com'
git -C $repo config core.autocrlf false
git -C $repo add -- .gitignore BepInEx BUILDING.md LICENSE README.md '使用说明.txt' src build.ps1 NONCOMMERCIAL-NOTICE.md PUBLISHING.md
git -C $repo diff --cached --stat
git -C $repo commit -m 'Restore v0.1.0 package and corresponding source with GPL notice'
git -C $repo push origin main
```

暂存前逐字节比较发布副本和原目录的全部 168 个项目文件，检查恢复源码来源和待提交路径。ZIP、私密配置、SSH 私钥及本地发布工具不加入 Git 提交。无需重新编译或修改程序行为。

## 8. 发布原 ZIP

新标签：`v0.1.0-republish-20260911`。标题：`TSKHook UI v0.1.0 — 原包重新发布（2026-09-11）`。标签指向本次恢复提交。

手动操作步骤：

1. 打开仓库右侧 **Releases**，点击 **Draft a new release**。
2. 在 **Choose a tag** 输入上述标签，选择 **Create new tag**；目标选择本次恢复提交。
3. 填写上述标题，并说明原包、对应源码、GPL 许可及作者倡议。
4. 将原路径 `$zip` 指向的 `TSKHook_UI_v0.1.0.zip` 拖入附件区域，等待上传完成。
5. 勾选 **Set as the latest release**，点击 **Publish release**。

本次自动操作使用 GitHub Releases API：先创建草稿、上传原 ZIP，确认附件大小后发布。认证由本机 Git 凭据管理器提供，仅保留在进程内存中；不在命令、脚本、远程地址或文档中写入令牌。

使用独立的重新发布标签可保留已有 `v0.1.0` Release 和附件。ZIP 从原路径直接读取上传，没有修改、重新压缩或插入说明文件。

## 9. 发布后确认

核对远程 `main` 与本次提交一致，新 Release 的附件名为 `TSKHook_UI_v0.1.0.zip`、状态为已上传、大小为 91,839,494 字节。下载新附件并与本地原 ZIP 按字节流比较，确认一致。

核对原目录、原 Git 元数据与原 ZIP 的文件集合、大小和修改时间保持不变，并确认原项目文件与提交内容的字节一致性。

## 已知问题

- 原 ZIP 带有 `.git` 元数据；安装游戏插件只需要其中的 `BepInEx` 内容。
- 原 ZIP 的可见目录没有 `src/` 和 `build.ps1`；对应源码和构建脚本在本次仓库及标签下提供。
- 原说明中的“仅供学习、请勿商用”是作者倡议。GPL-2.0 允许商业使用，无法在保留 GPL 权利的同时对整个包有效禁止商用。
