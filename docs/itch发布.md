# itch.io 发布清单

> 打包产物：`BuildInstaller\MinecraftTetris-web-itch.zip`（约 55 MB，`index.html` 在压缩包根目录，可以直接当 HTML5 项目传）
> 游戏是中文界面，页面文字按「中文为主 + 英文一句」写，够用。

---

## 一、上传步骤

1. 注册 / 登录 <https://itch.io>（邮箱即可，不用实名）。
2. 右上角头像 → **Upload new project**。
3. **Kind of project** 选 **HTML**。
4. **Uploads** 里传 `MinecraftTetris-web-itch.zip`，传完勾上
   **“This file will be played in the browser”**。
5. **Embed options**：
   - Viewport dimensions 填 `960 x 650`（宽 = 游戏宽，高多留 50 给 itch 的页脚；填死尺寸会把画面切掉）
   - 勾 **Fullscreen button**（手机上竖屏玩必须靠这个）
   - 勾 **Mobile friendly**
   - 勾 **Automatically start on page load**（可选，体验更顺）
6. 填下面的标题 / 简介 / 描述 / 标签。
7. Pricing 选 **No payment**，Visibility 先选 **Draft**（草稿），自己用 itch 预览确认能跑，再改 **Public**。
8. 发布后把链接发我，我把它加进 README。

---

## 二、可以照抄的页面内容

### Title

```
我的世界俄罗斯方块 3D
```

### Short description / tagline（一句话，显示在卡片上）

```
把俄罗斯方块放进我的世界：方块会掉落、会流动，还能一列一列往深处搭。
```

### Description（正文，直接粘）

```
一个把俄罗斯方块搬进 3D 方块世界的实验作品。

不是把场地竖起来那么简单——这里的场地有「深度」：方块可以从前往后一层层堆，
沙子会塌、水会流、岩浆会烧成黑曜石，TNT 会炸掉一整片。消除也分两种：
整行消（传统那套）和整列消（每一格单独消）。

我在原版《我的世界俄罗斯方块》的基础上做了个 3D 版本，加了关卡式的深度机制、
层次俯视图、手机端的拖拽操作。

【操作】
· 手机：屏幕下半区拖动移动，点按旋转，长按加速下落
· 电脑：方向键移动，↑ 旋转，空格落底，Esc 暂停，退格撤销

【说明】
· 单人、免费、无内购、无广告
· 进度和最高分存在你本地浏览器里
· 第一次打开要下载约 55 MB（音效和贴图），之后浏览器会缓存

【声明】
这是一个非商业的同人 / 学习作品（fan project），与 Mojang Studios、Microsoft
及 The Tetris Company 均无关联。方块贴图与部分音效来自《我的世界》，版权归
Mojang Studios 所有；界面字体版权归方正字库。这些素材仅用于学习交流，不用于
任何商业用途。若权利人提出异议，作者会立即移除相关内容。

【English】
A 3D take on Tetris inside a Minecraft-style voxel world. Blocks fall, sand
collapses, water flows, lava turns to obsidian, TNT clears a chunk. Two clear
modes: classic row clears and per-block column clears. Free, no ads, no IAP.
Non-commercial fan project, not affiliated with Mojang, Microsoft or The Tetris
Company.
```

### Genre / Tags

- Genre：**Puzzle**
- Tags：`tetris`、`minecraft`、`3d`、`voxel`、`puzzle`、`mobile`、`unity`、`chinese`
- Platforms：HTML5

---

## 三、上传前的自检（传之前先自己过一遍）

| 检查项 | 怎么看 |
|---|---|
| 能不能进游戏 | 打开链接，看到标题页就算通 |
| 第一次加载多久 | 55 MB，宽带下 5～15 秒；超过 30 秒说明源站慢，不是包的问题 |
| 手机上能不能玩 | 用手机浏览器打开 itch 页面，点 Fullscreen 再玩（竖屏布局是为手机做的） |
| 存档在不在 | 玩一局 → 刷新页面 → 最高分还在（存在 localStorage） |
| 声音有没有 | 点一下方块，听到放置音 |

**如果卡在加载条不动**：打开浏览器控制台（F12），看 Network 里
`Build/BuildWebGL.data.unityweb` 是不是 404。404 一般是压缩包里多套了一层目录
（zip 里必须是 `index.html` 在最外层，不能再套一层 `BuildWebGL/`）。

---

## 四、这份包的技术说明（给以后的我留底）

- Unity 2022.3 WebGL，**Gzip + Decompression Fallback**。
  代价是文件叫 `.unityweb`、首帧前要在 JS 里解一遍；收益是**不依赖服务器给
  `Content-Encoding` 头**，itch、GitHub Pages、任何静态托管都能跑。
- 未压缩时整包 75 MB，压完 55 MB。大头是 `BuildWebGL.data`（51 MB，音效和贴图）。
  真要再瘦，得从曲库下手（现在游戏内 14 首 + 菜单 2 首），这个另说。
- 模板用的是项目自带 `MinecraftMobile`：满屏画布 + 禁双指缩放。
- 存档在 `localStorage`，换设备不同步。

---

## 五、风险提醒（重要）

这份包里的方块贴图和部分音效来自《我的世界》，**没有授权**。

- itch.io 对同人作品相对宽松，注明 fan project、非商业，实践上普遍能过；
- 但它不等于合法授权，被投诉就得下架；
- 如果哪天想正经上架（4399 / 应用商店 / 收费），必须先把素材全部换掉：
  自绘方块贴图、CC0 音效、思源黑体或阿里巴巴普惠体、改掉「俄罗斯方块」这个名字。

现在这一版定位是**学习交流 + 收集反馈**，别挂价格、别投广告，风险最小。
