using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>
    /// 光标荧光拖尾：深蓝色，跟着鼠标走，尾部渐渐扩散、变淡。
    ///
    /// 几个实现上的取舍：
    /// - 用 IMGUI（GUI.DrawTexture）画，不依赖任何材质/Shader。项目是内置管线 + 全部界面都是
    ///   OnGUI 手写的，这样不用为了一个特效引入新的渲染资产。
    /// - 光晕贴图是运行时生成的一张 64×64 白色径向渐变（RGB 全白、只有 alpha 渐变），
    ///   颜色靠 GUI.color 染。这样一张贴图能画出深蓝拖尾 + 亮蓝内芯两层。
    /// - 不挂 OnGUI，改成由界面脚本在自己 OnGUI 的结尾调 <see cref="DrawLast"/>：
    ///   「拖尾必须在所有面板之上」这件事，靠同一段 OnGUI 里的代码顺序来保证，
    ///   而不是靠 GUI.depth 的跨脚本层级排序（那个实测不可靠，见 DrawLast 的注释）。
    /// - 用 Time.unscaledDeltaTime：暂停时 timeScale = 0，用 deltaTime 拖尾会僵在原地。
    /// - 采样放在 Update（每帧一次），绘制只认 Repaint 事件——OnGUI 一帧会被调用多次
    ///   （Layout / Repaint），在 OnGUI 里采样会让拖尾点数忽多忽少、形状抖。
    /// </summary>
    public class CursorTrail : MonoBehaviour
    {
        /// <summary>拖尾最多保留多少个采样点（约等于「尾巴有多长」）。</summary>
        const int MaxNodes = 36;

        /// <summary>一个采样点存活多久（秒）。</summary>
        const float Life = 0.28f;

        struct Node
        {
            public Vector2 pos;    // GUI 坐标（左上角原点）
            public float age;
        }

        readonly Node[] _nodes = new Node[MaxNodes];
        int _count;

        Texture2D _glow;
        float _time;

        /// <summary>拖尾本体的颜色：深蓝。</summary>
        static readonly Color TrailColor = new Color(0.10f, 0.32f, 1f, 1f);

        /// <summary>靠近光标那一段偏亮蓝白，看起来像「发光」而不是「一团蓝墨水」。</summary>
        static readonly Color HotColor = new Color(0.58f, 0.80f, 1f, 1f);

        static CursorTrail _instance;
        int _drawnFrame = -1;

        /// <summary>
        /// 鼠标有没有真的进过窗口。没进过之前 Input.mousePosition 一直是 (0, 0)，
        /// 也就是屏幕左下角 —— 系统光标藏掉之后，在那儿凭空画一颗光斑会让人以为
        /// 指针卡在角落里，所以没见过真鼠标就先不画。
        /// </summary>
        bool _seenMouse;

        /// <summary>
        /// 让界面脚本在自己 OnGUI 的**最后**调一次，拖尾就会压在这一帧所画的所有东西之上。
        ///
        /// 为什么不用 GUI.depth：那是跨 MonoBehaviour 的层级排序，行为不直观也不稳定——
        /// 实测设成 -1000 后拖尾仍然被暂停面板的全屏压暗盖住。同一段 OnGUI 里代码顺序
        /// 就是绘制顺序，这条是确定的，所以改成「谁最后画谁来喊一声」。
        /// </summary>
        public static void DrawLast()
        {
            if (_instance != null)
                _instance.Draw();
        }

        void Awake()
        {
            _instance = this;
            _glow = MakeGlowTexture(64);
        }

        void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
            if (_glow != null)
                Destroy(_glow);
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _time += dt;

            // 老化的点从队头开始（队头最老），一次挪掉
            for (int i = 0; i < _count; i++)
                _nodes[i].age += dt;

            int drop = 0;
            while (drop < _count && _nodes[drop].age >= Life)
                drop++;
            if (drop > 0)
            {
                for (int i = drop; i < _count; i++)
                    _nodes[i - drop] = _nodes[i];
                _count -= drop;
            }

            Vector2 p = MouseGui();
            if (p.x > 0.5f || p.y > 0.5f)
                _seenMouse = true;

            if (_count > 0 && (p - _nodes[_count - 1].pos).sqrMagnitude < 4f)
                return;   // 没怎么动就不加新点，尾巴会自然淡掉，不会在原地堆成一坨

            if (_count >= MaxNodes)
            {
                for (int i = 1; i < MaxNodes; i++)
                    _nodes[i - 1] = _nodes[i];
                _count = MaxNodes - 1;
            }

            _nodes[_count].pos = p;
            _nodes[_count].age = 0f;
            _count++;
        }

        static Vector2 MouseGui()
        {
            Vector3 m = Input.mousePosition;
            return new Vector2(m.x, Screen.height - m.y);
        }

        void Draw()
        {
            if (Event.current == null || Event.current.type != EventType.Repaint)
                return;

            // 界面脚本可能不止一个（HUD、标题界面），同一帧只画一次，免得叠两层发白
            if (_drawnFrame == Time.frameCount)
                return;

            Vector2 mouse = MouseGui();
            if (mouse.x < -20f || mouse.y < -20f || mouse.x > Screen.width + 20f || mouse.y > Screen.height + 20f)
                return;
            if (!_seenMouse)
                return;
            // 拖窗口那几帧先不画：尾巴最多能拆出几百个光斑（每个都是一趟 DrawTexture），
            // 那几帧正好最挤。反正这时鼠标压在窗口边框上，拖尾本来也看不清。
            if (ResizeGuard.Busy)
                return;

            _drawnFrame = Time.frameCount;

            Color prevColor = GUI.color;

            // 半径按屏高走，换分辨率时观感一致
            float unit = Mathf.Max(16f, Screen.height * 0.030f);

            // 相邻两个采样点之间按弧长补点。鼠标一甩能在一帧里跑上百像素，
            // 直接按点画就是一颗颗珠子；按 0.45 个光斑的间距插值后才是连续的彗尾。
            // 上限 8 段：再快也只是尾巴略稀，不会让 IMGUI 的绘制批次爆掉。
            if (_count > 0)
            {
                Vector2 prevPos = _nodes[0].pos;
                float prevAge = _nodes[0].age;
                DrawNode(prevPos, prevAge, unit);

                for (int i = 1; i < _count; i++)
                {
                    Vector2 pos = _nodes[i].pos;
                    float age = _nodes[i].age;

                    float dist = Vector2.Distance(prevPos, pos);
                    int steps = Mathf.Clamp(Mathf.CeilToInt(dist / (unit * 0.45f)), 1, 8);
                    for (int k = 1; k <= steps; k++)
                    {
                        float t = (float)k / steps;
                        DrawNode(Vector2.Lerp(prevPos, pos, t), Mathf.Lerp(prevAge, age, t), unit);
                    }

                    prevPos = pos;
                    prevAge = age;
                }
            }

            // 光标本身：外圈呼吸 + 内芯。
            // 鼠标停住一会儿尾巴就淡光了（_count 归零），这时这三颗光斑是场上唯一的指针
            // —— 系统光标已经藏掉，所以中心要画得更实一点，不然在浅色方块上找不着自己。
            float idle = _count == 0 ? 1f : 0f;
            float coreSize = Mathf.Lerp(0.75f, 0.62f, idle);
            float coreAlpha = Mathf.Lerp(0.55f, 0.92f, idle);

            float pulse = 0.85f + 0.15f * Mathf.Sin(_time * 3.4f);
            Blob(mouse, unit * 3.4f * pulse, new Color(TrailColor.r, TrailColor.g, TrailColor.b, 0.18f));
            Blob(mouse, unit * 1.7f * pulse, new Color(TrailColor.r, TrailColor.g, TrailColor.b, 0.34f));
            Blob(mouse, unit * coreSize, new Color(HotColor.r, HotColor.g, HotColor.b, coreAlpha));

            GUI.color = prevColor;
        }

        /// <summary>画一个拖尾光斑：k = 1 是刚生成（小而亮），k = 0 是快消失（大而淡）。</summary>
        void DrawNode(Vector2 pos, float age, float unit)
        {
            float k = 1f - age / Life;
            if (k <= 0f)
                return;

            float size = Mathf.Lerp(unit * 2.8f, unit * 0.85f, k);   // 越老越大，像在扩散
            float a = Mathf.Pow(k, 1.7f) * 0.55f;

            Color c = Color.Lerp(TrailColor, HotColor, k * k);
            Blob(pos, size, new Color(c.r, c.g, c.b, a));
        }

        void Blob(Vector2 center, float size, Color color)
        {
            GUI.color = color;
            GUI.DrawTexture(
                new Rect(center.x - size * 0.5f, center.y - size * 0.5f, size, size),
                _glow, ScaleMode.StretchToFill, true);
        }

        /// <summary>生成一张白色径向渐变（只有 alpha 有变化），用作光斑。</summary>
        static Texture2D MakeGlowTexture(int n)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            tex.name = "CursorGlow";
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            var px = new Color32[n * n];
            float half = n * 0.5f;
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f - half) / half;
                    float dy = (y + 0.5f - half) / half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);

                    float a = Mathf.Clamp01(1f - r);
                    a = a * a * (3f - 2f * a);   // smoothstep：边缘更柔，不会出现硬圈
                    px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }

            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }
    }
}
