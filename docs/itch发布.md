# Substrata · itch.io 发布清单

> 打包产物：`BuildInstaller\Substrata-web-itch.zip`（`index.html` 在压缩包根目录，可以直接当 HTML5 项目传）
> 游戏是中文界面，页面文字按「中文为主 + 英文一句」写。

---

## 一、上传步骤

1. 注册 / 登录 <https://itch.io>（邮箱即可，不用实名）。
2. 右上角头像 → **Upload new project**。
3. **Kind of project** 选 **HTML**。
4. **Uploads** 里传 zip，传完勾上 **“This file will be played in the browser”**。
5. **Embed options**：
   - Viewport dimensions 填 `960 x 650`（宽 = 游戏宽，高多留 50 给 itch 页脚；填死尺寸会把画面切掉）
   - 勾 **Fullscreen button**（手机上竖屏玩必须靠这个）
   - 勾 **Mobile friendly**
6. 填下面的标题 / 简介 / 描述 / 标签。
7. Pricing 选 **No payment**，Visibility 先选 **Draft**，自己预览确认能跑，再改 **Public**。

---

## 二、可以照抄的页面内容

### Title

```
Substrata
```

### Short description / tagline（显示在卡片上，一句话）

```
分层下落的 3D 方块解谜：沙会塌、水会流、火会烧，整个场地一层层往下收。
```

### Description（正文，直接粘）

```
一个把下落式方块玩法搬进「有深度的场地」的解谜游戏。

这里的场地不是一块平面：除了左右，方块还能往深处挪；除了面内旋转，
还能俯视转 90°、前后翻 90°，一共 24 种朝向。消除分两种——整行消，
或者按整列消。

【会动的方块】
· 沙、粉尘、砾石、盐：下面是空的就塌下来
· 水、岩浆、酸、燃料：会流动、会填满空腔；岩浆遇水凝成黑曜石
· 纸、木、叶、燃料：挨着火就烧，火会顺着蔓延过去
· 炸药：被点着后连锁爆炸，炸掉的坑会被上方的方块填平
· 结构方块：落定时记住整块场地，被消掉时先把场上收干净，再按记忆掉回来

【自己造方块】
创意工坊里有一个 5×5×5 的建造盒，可以像搭积木一样拼出自己想要的形状，
最多 8 格、必须连在一起。搭好的模具会混进平时的出块池，也能绕三个轴翻转。

【操作】
· 电脑：A/D 左右，W/S 前后，Q/E 旋转，空格硬降，Shift 软降，Esc 暂停
· 手机：左下拖动移动，右下按键旋转与硬降，右上角暂停

【说明】
· 单人、免费、无内购、无广告
· 最高分和存档存在本地
· 第一次打开要下载一段时间（贴图和音频），之后浏览器会缓存

A 3D falling-block puzzler with depth: sand collapses, water flows, fire spreads,
and the whole field sinks layer by layer.
```

### Tags

```
puzzle
falling-blocks
3d
voxel
sandbox
mobile
unity
chinese
```

### 封面图

用 `docs/icon/app_icon_512.png`（三层地层 + 落下的方块），和图标同一套视觉。

---

## 三、上传前自检

| 项 | 怎么验 |
|---|---|
| 能进标题页，logo 完整（不是被切成几段） | 打开即看 |
| 手机竖屏能玩 | 点 Fullscreen 后转竖屏操作 |
| 最高分能存住 | 玩一局 → 刷新 → 看最高分还在不在 |
| 有声音 | 检查音量；浏览器会拦自动播放，首次需点一下页面 |
| 方块贴图没有粉色块 | 粉色块 = 材质丢了，说明图集或材质配置不对 |
| 旧存档不会报错 | 清缓存后首次进入应正常开局，不弹错误 |

## 四、技术备注

- 构建走 **Gzip + 解压兜底**（`.unityweb` 后缀），浏览器自己解压，不依赖服务器返回
  `Content-Encoding` 头——静态托管（itch、GitHub Pages）常常不设这个头，
  那些「卡在加载条不动」的 Unity 游戏基本都是这里出的问题。
- zip 里 `index.html` 必须位于根目录。如果加载条卡住，按 F12 看 `Build/*.data.unityweb`
  是不是 404，通常是压缩包多套了一层目录。
- 页面标题与描述来自 `Assets/WebGLTemplates/MinecraftMobile/index.html`，改文案改那里。

## 五、素材授权

本作不含任何第三方游戏的美术或音频：方块贴图、图标、主菜单 logo 都由
`tools/` 下的脚本程序化生成；背景音乐取自 OpenGameArt 的 CC0 曲目。
放置音效与字体仍在替换中，发布前确认 [README](../README.md) 的素材表已经全部落到最终状态。
