# AGENTS.md

这是一个 VRChat Avatar 改模工程：ALCOM 管理项目和依赖，Git 记录改模过程。初始化步骤、需要安装的软件和提交类型见 [README.md](README.md)，这里只写给 agent 的规则。

## 开工前

1. **读 [PROJECT.md](PROJECT.md)。** 如果还是空模板，只有占位说明，先和用户一起填好再动手：基础模型、场景、头像根物体、方向和禁忌。能从项目里查到的自己查，查不到的再问。
2. **检查环境。** 缺什么就告诉用户 README「准备」一节里的装法。系统软件不自己装，ALCOM 的包清单不自己改。
   - `git --version`、`git lfs version` 都能用；
   - 项目是 Git 仓库，并且执行过 `git lfs install --local`（`.git/hooks/pre-push` 里有 `git lfs`）。还没有初始存档的话，问过用户后按 README 第 4 步做；
   - Unity 连上了：MCP for Unity 的工具可用，而且当前打开的场景属于这个项目。连着别的项目就停下，告诉用户，什么都不在那边执行；
   - 这次要用的 VPM 包在 `Packages/vpm-manifest.json` 里。缺的请用户在 ALCOM 里装。
3. **读进行中的 `HANDOFF-*.md`**，如果有的话。

## 资产

- 厂商资源（购买的模型、衣服、头发、插件）只读。要改就复制到 `Assets/_USER/`，改副本，再让 Avatar 引用副本。场景里预制体实例的 override 可以直接改。
- 贴图每次都用脚本从原图重新生成，不在上一版输出上继续改，反复改会积累瑕疵。生成脚本放在仓库里，不放临时目录。
- 生成的网格、遮罩、材质放在 `Assets/_USER/`。覆盖已有资源时保持原 GUID。

## 提交

- 用户说提交才提交。一次提交只做一件能单独撤销的事，说明用 `类型: 改动说明`，类型见 README。
- 资源和 `.meta` 一起提交。提交前看 `git diff --cached --stat` 和 `git lfs ls-files`，确认模型和贴图走了 LFS。
- 工具或构建自动改写的文件不提交，除非用户要。例如被插件切换的 `ProjectSettings/` 项、lilToon 构建时改写的 `ProjectSettings/lilToonSetting.json`。用户自己没有跟踪的目录（参考图之类）也不碰。
- 推送到远程前先问。

## 分支和 tag

- `main` 是基础形象。要另做一个头像（换整套衣服、换风格），就开一个以头像简称命名的分支，从一个 tag 拉出来，不从半成品拉。
- 新分支第一件事：清空根物体 `PipelineManager` 上的蓝图 ID，否则上传会覆盖原来的头像。
- 上传后用户认可的版本，打 annotated tag（如 `<分支>-v1`），说明里写头像名、蓝图 ID 和关键数字（评级、面数）。
- 分支之间共用的脚本和资源，用 `git checkout <分支> -- <路径>` 拿过来。场景文件不能 cherry-pick（对象 ID 对不上），要在目标分支上重新运行工具。
- Unity 开着时切分支：先打开一个空场景，切换，刷新，再打开场景。

## Unity

- 用户可能同时在改同一个场景。保存前先检查 `scene.isDirty`，有不是自己做的改动就先问。
- 判断改动的效果，看 NDMF 烘焙后的副本，不看场景里的原物体。做法见 skill `vrchat-avatar-unity`。
- 看起来对不对，要渲染出来看过才算数。游戏里才能确认的（物理手感、别人眼里的样子），交给用户在游戏里测。

## 上传

- 每次上传都先问：新建一个头像，还是覆盖哪一个；名字；保持私有。

## 交接文档

- 长期不变的事实（模型、头像和蓝图 ID、服装来源、方向和禁忌）写进 `PROJECT.md`，有变化就更新。
- 每个主题一份 `HANDOFF-<主题>.md`，放在仓库根目录，用用户的语言写：数字、用户的决定、踩过的坑。每完成一步就更新。

## Skills

skills 在 `.claude/skills/` 下（`.agents/skills` 是指向它的链接，给 Codex 用）：

- `vrchat-avatar-unity`：在 Unity 里操作头像的基础。烘焙检查、渲染、测试、上传。
- `vrchat-pc-good`：把头像做到 PC Good（绿模）。
- `vrchat-expressions`：捏脸、FaceEmo 表情、妆容和 MMD 世界兼容。
- `booth-research`：Booth 选购调研，看过图再推荐。

不支持 skill 的 agent：任务对上某个 skill 的 `description` 时，读它的 `SKILL.md` 照做。
