# MinecraftTetris 项目长期备忘

## 我的角色定位（用户明确要求）

- 用户希望我作为**项目 leader / 导师**，带他理解这个项目、解读代码、教他使用 Unity。
- **只教不改**：不要主动修改项目文件内容。项目由另一个 agent 负责推进开发，我只负责讲解。
- **渐进式教学**：从简入难，一次只讲一个小主题，不要一次全教完。
- 用户说"不用现在就开始"，等他主动发起学习请求即可。

## 项目概况

- Unity 2022.3.62f3c1，内置渲染管线，Windows 平台，单场景（Assets/Scenes/Game.unity，由 Editor 脚本自动生成）。
- 项目：我的世界俄罗斯方块 3D 版（致敬 B 站马克杯的 TetrisM）。
- 2026-10-25 已完成 v0.1 可玩版：z=1 的平面俄罗斯方块，能放置/消除/音效/HUD/开始界面/最高分，Windows 构建已验证（Build/MinecraftTetris.exe）。
- 代码在 Assets/Scripts/{Core,Input,Camera,UI} + Assets/Editor，约 25 个 .cs。
- 后续阶段：3D 深度(z≥2) → 相机/设置 → MC 方块效果（沙/TNT/玻璃/水岩浆）→ 内容扩充 → 打磨 → 跨平台。
- 项目会实时更新（另一个 agent 在推进），每次讲解前应先看看最新代码状态。

## 教学线索备忘（从简入难的切入点）

- 入门：Unity 界面/概念（Scene、GameObject、Component、Prefab）→ 读 Bootstrap.cs 看场景如何组装
- 进阶：Grid.cs（三维数组+碰撞）、Tetromino.cs（形状）、GameLoop.cs（状态机与主循环）
- 更难：BlockMesh/图集 UV 渲染、GridView、粒子、音效系统
- 任务清单里有"当天踩过的坑"，是很好的教学素材（NPOT 贴图、UV padding、BlockId 混用等）
