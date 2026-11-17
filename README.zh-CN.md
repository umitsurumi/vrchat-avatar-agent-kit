# vrchat-avatar-agent-kit

[English](README.md)

和 AI 编程 agent（Claude Code、Codex 等）一起改 VRChat Avatar 的起步工具箱。把它装进用 [ALCOM](https://vrc-get.anatawa12.com/alcom/) 建好的 Unity Avatar 项目，agent 就有了：

- **规则**（`AGENTS.md`）：先检查环境，不碰厂商原文件，一次提交一件能单独撤销的事，上传前先问。
- **Skills**（`.claude/skills/`）：怎样烘焙并检查 VRChat 实际收到的头像，穿衣服，改贴图，做表情，冲 PC Good，在 Booth 上调研商品。
- **工具**：环境检查；每次都从原图重新生成贴图的 texture kit；几个 Unity 编辑器小工具。

Git 记录每一步，agent 做的任何事都能撤销。

这个工具箱是 **agent-first** 的：你说想要什么，agent 通过 [MCP for Unity](https://github.com/CoplayDev/unity-mcp) 在打开着的 Unity 里动手，并渲染给你看。每个工具也能在命令行里用普通参数直接运行，你也可以自己调用。

## 快速开始

1. 装好[准备软件](#准备软件)。
2. 用 ALCOM 创建 Avatar 项目，导入基础模型，安装需要的[包](#vpm-包)。
3. 在这个项目里安装 [MCP for Unity](#mcp-for-unity)。
4. 在项目目录里打开 agent，告诉它：

   > 按 https://github.com/umitsurumi/vrchat-avatar-agent-kit （或它的本地克隆）里的 `INSTALL.md`，把 vrchat-avatar-agent-kit 装进这个项目。

   也可以自己运行：`uv run install.py <项目路径>`。安装脚本不会覆盖已经存在的文件，只会把它们列出来。
5. 开始改模。第一次会话时，agent 会运行环境检查，和你一起填好 `PROJECT.md`，并做第一次提交。

## 准备软件

在终端里运行「检查」一列的命令，能打印出版本号就说明装好了。

| 软件 | 用途 | 检查 |
| --- | --- | --- |
| [Git](https://git-scm.com/) | 记录每一步；Unity 用 git URL 安装包时也要用到 | `git --version` |
| [Git LFS](https://git-lfs.com/) | 用 Git 存模型、贴图这类大文件 | `git lfs version` |
| [uv](https://docs.astral.sh/uv/) | 运行工具箱的 Python 工具和 MCP for Unity；Python 和依赖库由它自动准备 | `uv --version` |
| [ALCOM](https://vrc-get.anatawa12.com/alcom/) | 创建 VRChat 项目、安装 VPM 包 | 能打开即可 |
| Unity 2022.3 | VRChat 要求的版本，按 ALCOM 的提示安装 | ALCOM 能打开项目即可 |
| agent | Claude Code、Codex 或其他能用 MCP 的 agent | |

安装方法：

- **Windows**：[Git for Windows](https://git-scm.com/download/win)（自带 Git LFS），或 `winget install --id Git.Git -e`；uv 用 `winget install --id=astral-sh.uv -e`。
- **Linux**：Git 和 Git LFS 用发行版的包管理器（`sudo apt install git git-lfs`、`sudo pacman -S git git-lfs`、`sudo dnf install git git-lfs`）；uv 用 `curl -LsSf https://astral.sh/uv/install.sh | sh`。
- **macOS**（尽力支持，未测试）：`brew install git git-lfs uv`。

## VPM 包

基础模型和衣服说明里要求的包优先。下面是 skills 会用到的。在 ALCOM 的 设置 → 包 → 添加仓库（Settings → Packages → Add Repository）里添加仓库地址，再到项目里安装：

| 包 | 用途 | VPM 仓库 |
| --- | --- | --- |
| VRChat SDK - Avatars | 必需，ALCOM 新建 Avatar 项目时自带 | 官方 |
| Modular Avatar（`nadena.dev.modular-avatar`） | 穿衣服、菜单、开关；会一起装上 NDMF | `https://vpm.nadena.dev/vpm.json` |
| AAO: Avatar Optimizer（`com.anatawa12.avatar-optimizer`） | 构建时优化 | `https://vpm.anatawa12.com/vpm.json` |
| lilToon（`jp.lilxyzw.liltoon`） | 多数 Booth 模型用的着色器 | `https://lilxyzw.github.io/vpm-repos/vpm.json` |
| Meshia Mesh Simplification（`com.ramtype0.meshia.mesh-simplification`） | 减面，冲 PC Good 时用 | `https://RamType0.github.io/VpmRepository/index.json` |
| FaceEmo（`jp.suzuryg.face-emo`） | 表情，可选 | `https://suzuryg.github.io/vpm-repos/vpm.json` |

## MCP for Unity

agent 通过 [MCP for Unity](https://github.com/CoplayDev/unity-mcp) 操作打开着的 Unity。

1. Unity → Window → Package Manager → `+` → Add package from git URL：
   `https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#main`
   想固定版本，就把 `#main` 换成[发布页](https://github.com/CoplayDev/unity-mcp/releases)上的 tag，例如 `#v10.3.0`。
2. Unity → Window → MCP for Unity → Configure All Detected Clients。
3. 完全重启 agent，让它读到新的 MCP 配置。

安装会改 `Packages/manifest.json`，单独提交一次，例如 `chore: 安装 MCP for Unity`。同时开着多个 Unity 项目时，agent 可能连到别的项目；`AGENTS.md` 要求它开工前先确认。

## 装进项目的内容

| 路径 | 内容 |
| --- | --- |
| `AGENTS.md`、`CLAUDE.md` | agent 的规则；`CLAUDE.md` 只有一行 `@AGENTS.md`，让 Claude Code 读同一份 |
| `PROJECT.md` | 项目的基本事实（基础模型、头像、蓝图 ID、方向）；第一次会话时和你一起填 |
| `.claude/skills/`、`.agents/skills` | skills；`.agents/skills` 是指向 `.claude/skills` 的链接，给 Codex 用 |
| `Tools/avatar-kit/` | `doctor.py`（环境检查）和 texture kit |
| `Assets/AvatarAgentKit/Editor/` | Unity 菜单 `Tools/Avatar Agent Kit` |
| `Assets/_USER/` | 你自己改过的资源；厂商文件保持原样 |
| `TextureSources/` | 贴图配方和它们的输入（在 `Assets/` 外面，Unity 不会导入） |
| `.gitignore`、`.gitattributes` | Unity 和 ALCOM 的忽略规则；模型和贴图走 LFS |
| `.avatar-kit-version` | 项目是用哪个版本的工具箱建立的 |

工具箱分两层：

- **工具**（`doctor`、`install.py`、texture kit、Unity 菜单项）：命令行接口稳定，有文档和测试。你只传参数，不用改代码。
- **参考**（skill 附带的脚本，例如 PC Good 脚本）：实际做过的例子，写得"略微通用"：和模型相关的值放在开头的配置块里，文件头写明在什么上验证过、哪里可能不适用。让你的 agent 读懂后按你的头像改着用。不承诺接口稳定。

## Skills

| skill | 用途 |
| --- | --- |
| `vrchat-avatar-unity` | 基础：通过 MCP for Unity 在打开着的 Unity 里工作：烘焙检查、渲染、测试、菜单和开关、上传 |
| `vrchat-outfits` | 用 Modular Avatar 穿衣服，以及穿上后的收尾 |
| `vrchat-textures` | 贴图配方、妆容、调色、用生图模型重画贴图、lilToon 材质 |
| `vrchat-expressions` | 捏脸、FaceEmo 表情、MMD 世界兼容 |
| `vrchat-pc-good` | 把头像做到 PC Good（绿模，一种性能评级），附参考脚本 |
| `booth-research` | Booth 商品调研，看过每张图再推荐 |

`booth-research` 只读公开的商品页面和图片，不下载商品，每次请求间隔一秒，符合 [BOOTH 的爬取方针](https://booth.pm/announcements/863)（2025-10-10）。

## 工具

- **环境检查**：`uv run Tools/avatar-kit/doctor.py` 列出装了什么、缺的怎么装（给 agent 用 `--json`）。它自己不安装任何东西。
- **texture kit**：一张贴图由 `TextureSources/<贴图名>/recipe.json` 里的配方描述：原图、输出和处理步骤（UV 遮罩、妆容 PSD、重新调色、改色、为生图模型重画准备的 crop 和 place）。每次运行都从原图开始，输出永远不会被再次加工，瑕疵不会累积。详见 skill `vrchat-textures`。
- **Unity 菜单** `Tools/Avatar Agent Kit`：导出 UV 三角形，用来做贴图遮罩；把选中的 Animator Controller 里所有状态的 Write Defaults 设为 ON。

## 在改模项目里用 Git

一次提交记录一件能单独撤销的改动。提交说明用 `类型: 改动说明`：

| 类型 | 用途 |
| --- | --- |
| `import` | 导入第三方资源、插件、模型 |
| `asset` | 生成、转换或修改实际资产 |
| `config` | Avatar、MA、菜单、参数、Animator 配置 |
| `tweak` | 外观、材质、位置、体型微调 |
| `fix` | 修 bug、穿模、骨骼、动画问题 |
| `refactor` | 结构整理但不改变最终效果 |
| `chore` | Git、Unity 工程设置、依赖升级 |
| `checkpoint` | 大阶段存档 |
| `docs` | 文档 |

先在 Unity 中保存场景和资源，再暂存、检查并提交：

```sh
git add .
git status
git diff --cached --stat         # 查看暂存区的改动统计
git diff --cached --name-status  # 查看新增、修改和删除
git lfs ls-files                 # 模型和贴图应该出现在这里
git commit -m "tweak: 调整衣服材质"
```

- 资源和它的 `.meta` 一起提交，工程设置和依赖清单也一起提交。
- Unity 缓存和 ALCOM 安装的包目录会被忽略。回退涉及依赖版本时，要在 ALCOM 里恢复对应版本的包。
- 从历史中恢复文件前先关闭 Unity，完成后重新打开，让 Unity 重新导入。

## 不覆盖的范围

- Quest / Android 头像。
- 用 VRCFury 配置的衣服：本工具箱假设使用 Modular Avatar。
- lilToon 以外的着色器（如 Poiyomi）的材质设置。texture kit 不在乎着色器，但 skills 里的材质说明只写了 lilToon。

## 许可

[MIT](LICENSE)
