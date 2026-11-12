# avatar-template

VRChat Avatar 改模用的 Git 配置、agent 规则和个人工具。[ALCOM](https://vrc-get.anatawa12.com/alcom/) 负责创建项目和管理依赖，Git 用来记录改模过程，作为“高级撤销”。

使用方式是：先用 ALCOM 创建 Avatar 项目，再把这里的文件手动复制进去。

## 准备

需要先装好这几样。在终端里执行最后一列的命令，能打印出版本号就说明已经装好。

| 软件 | 用途 | 检查 |
| --- | --- | --- |
| [Git](https://git-scm.com/) | 记录改模过程；Unity 用 git URL 安装包时也要用到 | `git --version` |
| [Git LFS](https://git-lfs.com/) | 用 Git 存模型、贴图这类大文件 | `git lfs version` |
| [ALCOM](https://vrc-get.anatawa12.com/alcom/) | 创建 VRChat 项目、安装 VPM 包 | 打开能用即可 |
| Unity | 用 VRChat 要求的版本（目前是 2022.3），按 ALCOM 的提示安装 | ALCOM 能打开项目即可 |

Git 和 Git LFS 的装法：

- **Windows**：安装 [Git for Windows](https://git-scm.com/download/win)，它自带 Git LFS。也可以用 `winget install --id Git.Git -e`。
- **macOS**：`brew install git git-lfs`。
- **Linux**：用发行版的包管理器，例如 `sudo apt install git git-lfs`、`sudo pacman -S git git-lfs`、`sudo dnf install git git-lfs`。

### 常用的 VPM 包

基础模型和衣服的说明里要求的包优先。下面是这个模板的 skills 会用到的。在 ALCOM 的 设置 → 包 → 添加仓库（Settings → Packages → Add Repository）里添加仓库地址，再到项目里安装：

| 包 | 用途 | VPM 仓库 |
| --- | --- | --- |
| VRChat SDK - Avatars | 必需，ALCOM 新建 Avatar 项目时自带 | 官方 |
| Modular Avatar（`nadena.dev.modular-avatar`） | 穿衣服、菜单、开关；会一起装上 NDMF | `https://vpm.nadena.dev/vpm.json` |
| AAO: Avatar Optimizer（`com.anatawa12.avatar-optimizer`） | 构建时优化 | `https://vpm.anatawa12.com/vpm.json` |
| lilToon（`jp.lilxyzw.liltoon`） | 多数 Booth 模型用的着色器 | `https://lilxyzw.github.io/vpm-repos/vpm.json` |
| Meshia Mesh Simplification（`com.ramtype0.meshia.mesh-simplification`） | 减面，冲 PC Good 时用 | `https://RamType0.github.io/VpmRepository/index.json` |
| FaceEmo（`jp.suzuryg.face-emo`） | 表情，可选 | `https://suzuryg.github.io/vpm-repos/vpm.json` |

## 新建改模项目

1. 用 ALCOM 创建 Avatar 项目，安装需要的包（见上）。
2. 将以下内容复制到项目根目录，也就是与 `Assets/`、`Packages/`、`ProjectSettings/` 同级的位置：

   ```text
   .gitignore
   .gitattributes
   AGENTS.md
   CLAUDE.md
   PROJECT.md
   .claude/skills/
   .agents/skills            （指向 .claude/skills 的链接）
   Assets/Editor/SetAllWriteDefaultsOn.cs
   Assets/_USER/.gitkeep
   ```

   `Assets/` 按目录合并；如果已有 Git 配置，合并对应规则。注意显示隐藏文件，以便复制点开头的文件和目录。`.agents/skills` 是软链接，用 `cp -a` 复制才能保留；丢了也不影响 Claude Code，AGENTS.md 里写了 skills 的位置。本仓库的 `.git/` 不复制，每个改模项目单独建立 Git 仓库。

3. 在 Unity 中打开项目，导入基础模型，等待导入完成并保存场景。Unity 会自动生成 `.meta`，与资源一起保留。
4. 在新项目根目录执行，建立改模前的存档：

   ```sh
   git init
   git lfs install --local
   git add .
   git status
   git commit -m "checkpoint: 初始改模存档"
   ```

## 提交

一次提交记录一件能够单独撤销的改动。提交说明使用 `类型: 改动说明`：

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

日常改模时，先在 Unity 中保存场景和资源，再暂存、检查并提交。以下示例适用于当前所有改动都属于同一件事的情况；如果有多件无关改动，按文件选择暂存并分别提交，带上相关 `.meta`。

```sh
git add .
git status
git diff --cached --stat         # 查看暂存区的改动统计
git diff --cached --name-status  # 查看暂存区文件的新增、修改和删除
git lfs ls-files                 # 查看 Git LFS 文件列表
git commit -m "tweak: 调整衣服材质"
```

首次提交也建议执行上述检查。确认暂存内容只包含本次改动，资源和 `.meta` 配套，模型、贴图等应由 LFS 管理的文件出现在 LFS 列表中。

- `Assets/_USER/` 用来集中放个人修改的资源，可按习惯调整。
- 资源及其 `.meta`、工程设置和依赖清单一起提交；模型、贴图等按配置进入 LFS。
- Unity 缓存和 ALCOM 安装的包目录会被忽略。回退涉及依赖版本时，需要再通过 ALCOM 恢复对应版本的包。
- 回退文件前先关闭 Unity，完成后重新打开，让 Unity 重新导入。

## 配合 agent 使用

- `AGENTS.md`：给 Codex、Claude Code 等 agent 的工作规则：开工前检查什么、资产怎么改、怎么提交、分支和 tag、上传前确认。按自己的习惯改。
- `CLAUDE.md`：只有一行 `@AGENTS.md`，让 Claude Code 读到同一份规则。
- `PROJECT.md`：这个项目的基本事实（基础模型、场景、头像和蓝图 ID、方向和禁忌）。第一次让 agent 开工时，它会和你一起填。

### 让 agent 操作 Unity：MCP for Unity

agent 要通过 [MCP for Unity](https://github.com/CoplayDev/unity-mcp) 才能操作打开着的 Unity。它需要 Python 3.10+，由 [uv](https://docs.astral.sh/uv/) 提供（检查：`uv --version`）。

1. Unity → Window → Package Manager → `+` → Add package from git URL：
   `https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#main`
   想固定版本，就把 `#main` 换成[发布页](https://github.com/CoplayDev/unity-mcp/releases)上的 tag，例如 `#v10.3.0`。
2. Unity → Window → MCP for Unity → Configure All Detected Clients。
3. 完全重启 agent（Claude Code、Codex 等），让它读到新的 MCP 配置。

安装会改 `Packages/manifest.json`，单独提交一次，例如 `chore: 安装 MCP for Unity`。同时开着多个 Unity 项目时，agent 可能连到别的项目；AGENTS.md 要求它开工前先确认。

### skills

`.claude/skills/` 下的 skills：

| skill | 用途 |
| --- | --- |
| `vrchat-avatar-unity` | 通过 MCP for Unity 操作打开着的 Unity：烘焙检查、渲染截图、测试、上传 |
| `vrchat-pc-good` | 把头像做到 PC Good（绿模），附参考脚本 |
| `booth-research` | Booth 选购调研，附 `booth.py`（搜索、商品信息、图片拼图；需要 Python 和 ImageMagick） |

## 附带工具

附带的 WD 工具按需手动使用：在 Project 窗口选中 Animator Controller，点击 `Tools > Animator > Set ALL Write Defaults ON`，将该控制器内所有状态的 Write Defaults 设为 ON。
