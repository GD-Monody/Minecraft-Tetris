using System;
using System.Collections.Generic;
using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>
    /// 新手教程。
    ///
    /// 第一次进游戏先问一句「游戏小白还是游戏达人」：达人直接跳过，小白走一遍完整流程。
    /// 教学方式是**遮罩挖洞 + 说明面板**：把要讲的那块界面亮出来，其余压暗，
    /// 底部（或顶部）给一块说明板；需要玩家动手的步骤不给按钮，等他真的做到那件事
    /// （开始游戏、打开游戏规则、把 z 调到 2…）就自动进下一步。
    ///
    /// 为什么进度存 PlayerPrefs 而不是字段：场地尺寸点「应用」会重载场景，
    /// 组件会被重建，只有 PlayerPrefs 能跨过去（静态字段在下一次编辑器重编译时也会丢）。
    ///
    /// 调试参数：-tutorial 强制重新问一遍；-tutostep N 直接跳到第 N 步（截图核对用）。
    /// </summary>
    public class Tutorial : MonoBehaviour
    {
        const string PrefAsked = "tutorial.asked";
        const string PrefDone = "tutorial.done";
        const string PrefStep = "tutorial.step";

        /// <summary>一步：说什么、亮哪块、什么时候算完成。</summary>
        class Step
        {
            public string Title;
            public string Body;
            /// <summary>高亮的矩形（GUI 坐标）；返回零矩形表示不挖洞、整屏压暗。</summary>
            public Func<Rect> Hole;
            /// <summary>满足就自动进下一步；null 表示只能点按钮。</summary>
            public Func<bool> Auto;
            /// <summary>进入这一步时要做的事（比如自动暂停、切页面）。</summary>
            public Action Enter;
            /// <summary>
            /// 说明板强制放底部。默认是「挡住高亮就翻到顶部」，但按键设置页的要切的那个
            /// 「初级 / 高级」在最上面，面板压上去就没法操作了，这种步骤单独指定。
            /// </summary>
            public bool PanelBottom;
            /// <summary>说明板贴屏幕左上角（讲整页时用：放中间会压住页面标题和页签）。</summary>
            public bool PanelCorner;
            /// <summary>
            /// 贴页面**最底边**，不让开底部那行按钮。创意工坊页内容长，
            /// 让开 80px 会把说明板顶到三维建造台上面去。
            /// </summary>
            public bool BottomEdge;
            /// <summary>这一步不给「上一步」——前面那块屏幕已经回不去了（比如从游戏退回主界面之后）。</summary>
            public bool NoBack;
            /// <summary>说明板上除了「下一步」之外的自定义动作（比如「退出到主界面」）。</summary>
            public Action Action;
            public string ActionLabel;
            /// <summary>
            /// 这一步允许点击的按钮文字。null = 什么都不许点（默认把界面锁死，防止穿帮）；
            /// 只有需要玩家自己操作的按钮才放进白名单。
            /// </summary>
            public string[] Allow;
            /// <summary>
            /// 允许「返回」。只给带子页面的那几组用：音乐页点进「音乐音量」之后，
            /// 返回得能退回音乐页（还在同一步的讲解范围里，不会穿帮）。
            /// 其它页面返回会跳到别的页，仍然锁着。
            /// </summary>
            public bool AllowBack;
            public string Button = "下一步";
        }

        Bootstrap _game;
        TitleScreen _title;
        Hud _hud;
        MobileControls _mobile;
        LayerMap _layerMap;

        readonly List<Step> _steps = new List<Step>();
        bool _asking;
        bool _running;
        int _step = -1;
        float _stepT;

        /// <summary>
        /// 进入这一步时自动条件就已经满足了（点「上一步」退回来的情况）。
        /// 这时候不要立刻往下跳，否则退回去的那一步会「弹」回来，看着像点不动。
        /// 等条件先变成不满足（玩家真的重做一遍），再放行。
        /// </summary>
        bool _autoWas;

        /// <summary>教程里点过「应用」（场地尺寸那一步）。</summary>
        bool _sizeApplied;

        /// <summary>
        /// 这一步进入时打开的是哪一页（-1 = 主界面）。
        /// 「返回」要不要锁，就看玩家是不是还停在这一页上：
        /// 还在 → 锁住（点回去会离开讲解范围）；已经点进子页 → 放行（退回上一层）。
        /// </summary>
        static int _enterPage = -1;

        GUIStyle _hStyle, _bStyle, _btnStyle, _skipStyle, _optTitle, _optBody, _btnLabel, _btnLabelAlt;
        GUIStyle _hintStyle, _toastStyle;
        bool _styles;

        /// <summary>「下一步」的渐变底：亮色主题浅蓝、暗色主题浅灰。</summary>
        Texture2D _btnTex;
        bool _btnTexLight;

        Rect _hole;
        bool _hasHole;

        public bool Running { get { return _asking || _running; } }

        /// <summary>当前步骤（给「按钮锁」查白名单用）。</summary>
        static Step _cur;

        /// <summary>
        /// 这个按钮现在能不能点。教程没跑 = 全放行；跑着的时候只有白名单里的能点，
        /// 其余一律锁死 —— 否则玩家能在教程讲 A 的时候跑去点「返回」，界面和文字就对不上了。
        /// </summary>
        /// <summary>
        /// 教程期间一律锁掉的「导航键」：点它们会离开当前页面，文字和界面对不上。
        /// 页面里的开关、滑条、页签都不在这个名单里 —— 那些让玩家随便点才是对的。
        /// </summary>
        static readonly string[] NavLabels =
        {
            "返回", "新手教程", "关闭", "继续游戏", "重新开始",
            "退出到主界面", "退出", "保存", "取消",
        };

        public static bool Allows(string label)
        {
            if (_cur == null)
                return true;

            // 步骤自己声明了白名单：只有名单里的能点（开始游戏 / 应用 / 键位：初级·高级）
            if (_cur.Allow != null)
            {
                for (int i = 0; i < _cur.Allow.Length; i++)
                    if (_cur.Allow[i] == label)
                        return true;
                return false;
            }

            // 「返回」单独判断：玩家已经点进子页（页面变了）才放行，
            // 让他在子页里能退回上一层；还停在这一步讲的那一页上就锁住。
            if (label == "返回")
            {
                if (_enterPage < 0 || _titleRef == null)
                    return false;
                return _titleRef.CurrentPage != _enterPage;
            }

            // 否则只挡导航键，页面里的控件照常能点
            for (int i = 0; i < NavLabels.Length; i++)
                if (NavLabels[i] == label)
                    return false;
            return true;
        }

        /// <summary>被锁的按钮被点了：弹一条提示，别让玩家以为自己点坏了。</summary>
        public static void NotifyLocked()
        {
            if (_instance == null)
                return;
            _instance.Toast("先按提示操作，这一步的其他按钮先别点");
        }

        static Tutorial _instance;

        /// <summary>标题页的静态引用：Allows 是静态方法，查「当前在哪一页」要用它。</summary>
        static TitleScreen _titleRef;

        string _toast;
        float _toastUntil;

        public void Bind(Bootstrap game, TitleScreen title, Hud hud,
            MobileControls mobile, LayerMap layerMap)
        {
            _game = game;
            _title = title;
            _titleRef = title;
            _hud = hud;
            _mobile = mobile;
            _layerMap = layerMap;
            BuildSteps();

            // -dumptuto：把步骤表打出来（核对每一步会切到哪个界面、能不能后退）
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-dumptuto") >= 0)
            {
                for (int i = 0; i < _steps.Count; i++)
                {
                    Step st = _steps[i];
                    Debug.Log("[tuto] " + i + " 「" + st.Title + "」"
                        + (st.Enter != null ? " enter" : " —")
                        + (st.Auto != null ? " auto" : " —")
                        + (st.NoBack ? " noback" : "")
                        + (st.PanelCorner ? " corner" : "")
                        + (st.Action != null ? " action=" + st.ActionLabel : ""));
                }
            }
        }

        void Start()
        {
            string[] argv = System.Environment.GetCommandLineArgs();

            // -notutorial：自动化截图 / 跑测试时别让教程挡着
            if (System.Array.IndexOf(argv, "-notutorial") >= 0)
            {
                PlayerPrefs.SetInt(PrefAsked, 1);
                PlayerPrefs.SetInt(PrefDone, 1);
                PlayerPrefs.Save();
                return;
            }

            // -tutorial：清掉标记，重新问一遍（自己调试 / 录屏用）
            // 网页版没有命令行，用 URL 上的 ?resettutorial=1 重置。
            // 参数特意叫得这么长：之前叫 tutorial=1，结果那一页每次刷新都会重置，
            // 玩家会以为「每次进来都问一遍」。
            bool reset = System.Array.IndexOf(argv, "-tutorial") >= 0;
            string url = Application.absoluteURL;
            if (!reset && !string.IsNullOrEmpty(url) && url.Contains("resettutorial=1"))
                reset = true;

            if (reset)
            {
                PlayerPrefs.DeleteKey(PrefAsked);
                PlayerPrefs.DeleteKey(PrefDone);
                PlayerPrefs.DeleteKey(PrefStep);
                PlayerPrefs.Save();
            }

            // -tutostep N：跳过询问，直接落到第 N 步（必须排在「首次询问」之前，
            // 否则新机器上问询分支直接 return，压根跳不过去）
            int at = System.Array.IndexOf(argv, "-tutostep");
            if (at >= 0 && at + 1 < argv.Length && int.TryParse(argv[at + 1], out int n))
            {
                _asking = false;
                _running = true;
                PlayerPrefs.SetInt(PrefAsked, 1);
                PlayerPrefs.SetInt(PrefDone, 0);
                int target = Mathf.Clamp(n, 0, _steps.Count - 1);
                // 把前面每一步的 Enter 依次跑一遍：不跑的话界面还停在初始状态
                // （比如第 7 步的规则页是第 6 步打开的），截图核对会以为界面错了
                for (int i = 0; i <= target; i++)
                {
                    if (i < target && _steps[i].Enter != null)
                        _steps[i].Enter();
                }
                EnterStep(target);
                return;
            }

            if (PlayerPrefs.GetInt(PrefAsked, 0) == 0)
            {
                _asking = true;
                return;
            }
            if (PlayerPrefs.GetInt(PrefDone, 0) != 0)
                return;

            int saved = PlayerPrefs.GetInt(PrefStep, -1);
            if (saved >= 0 && saved < _steps.Count)
            {
                _running = true;
                EnterStep(saved);
            }
        }

        void Awake()
        {
            _instance = this;
        }

        void Update()
        {
            // 教程期间暂停不弹「已暂停」那块面板：讲解时它盖住半个场地，
            // 而且四颗按钮都能点，玩家一按就穿帮
            if (_hud != null)
                _hud.HidePausePanel = Running;

            // 拖尾要在教程的遮罩之上：每帧先声明一次，HUD / 标题页那些早先的调用就都会让开，
            // 最后由本脚本 OnGUI 末尾的 DrawTop 统一画一遍（见 CursorTrail.DeferNextDraw）
            CursorTrail.DeferNextDraw(Running);

            if (!Running)
            {
                TutorialBlocker.ClearRects();   // 教程结束 / 没在跑：别留着挡板吃点击
                return;
            }
            if (!_running || _step < 0 || _step >= _steps.Count)
                return;

            Step s = _steps[_step];
            if (s.Auto == null)
                return;

            bool ok = s.Auto();
            if (ok && !_autoWas)
                Advance();
            else if (!ok)
                _autoWas = false;
        }

        // ── 步骤表 ────────────────────────────────────────────────────

        void BuildSteps()
        {
            _steps.Clear();

            // 1. 开始游戏
            _steps.Add(new Step
            {
                Title = "第一步：开始游戏",
                Body = "点「开始游戏」进场地。\n方块从上面一块块落下来，目标是把一整排横向填满。",
                Hole = () => MenuRect("开始游戏"),
                Allow = new[] { "开始游戏" },
                // 从第 2 步退回来时人还在场地里，这一步讲的却是主界面上的按钮：
                // 先把人送回主界面，界面才和文字对得上
                Enter = () =>
                {
                    if (_game != null && _game.InGame)
                        _game.GameToTitle();
                    GoMenu();
                },
                Auto = () => _game != null && _game.InGame,
            });

            // 2. 基础玩法（进来先自动暂停，方便看字）
            _steps.Add(new Step
            {
                Title = "基础玩法：和普通俄罗斯方块一样",
                Body = MobileMode.Enabled
                    ? "左下角是方向键：按住左右移动方块；右下角是旋转和硬降。\n"
                        + "一整排填满就消掉，上面的方块会落下来。\n"
                        + "现在场地只有一格深（z = 1），先只管左右，和经典俄罗斯方块一样。"
                    : "A / D 左右移动，E 顺时针旋转、Q 逆时针旋转。\n"
                        + "空格直接落到底，左 Shift 加速下落。\n"
                        + "一整排填满就消掉，上面的方块会落下来。\n"
                        + "现在场地只有一格深（z = 1），先只管左右，和经典俄罗斯方块一模一样。",
                Enter = () => _game.SetPaused(true),   // 讲的时候把局面停住，别一边看一边掉
            });

            // 3. 分数界面
            _steps.Add(new Step
            {
                Title = "分数界面",
                Body = MobileMode.Enabled
                    ? "顶部那一条是分数、已消除的行数和最高分。\n"
                        + "一次消掉越多排，分数涨得越快。\n最右边那颗是暂停键，随时能停下来。"
                    : "左上角是分数、已消除的行数和最高分。\n"
                        + "一次消掉越多排，分数涨得越快。\n按 Esc 暂停，随时能停下来。",
                Hole = ScoreRect,
                Enter = () => _game.SetPaused(true),   // 回来时确保还是暂停
            });

            // 4. 操作界面
            _steps.Add(new Step
            {
                Title = "操作界面",
                Body = MobileMode.Enabled
                    ? "底部这一条就是操作区：右下角是旋转和硬降，左下角按住可以拖动方块。\n"
                        + "键位可以在「设置 → 操作」里切初级（四键）和高级（九键）。"
                    : "电脑上直接用键盘：A / D 左右，W / S 前后，\n"
                        + "E 顺时针旋转、Q 逆时针旋转，空格硬降、左 Shift 软降。\n"
                        + "键位可以在「设置 → 操作」里改，也能一键恢复默认。",
                Hole = ControlsRect,
                // 讲操作时把暂停放开：方块正常下落，玩家可以当场试
                Enter = () => _game.SetPaused(false),
            });

            // 5. 退出到主界面
            _steps.Add(new Step
            {
                Title = "先回主界面",
                // 教程里的暂停不弹「已暂停」那块面板（用户要求：教程期间不出现那个界面），
                // 所以退出这一步由说明板上这颗按钮代劳
                Body = "规则和设置都在主界面上，先回主界面。\n点下面这颗按钮就行。",
                Enter = () => _game.SetPaused(true),
                Action = () => _game.GameToTitle(),
                ActionLabel = "退出到主界面",
                Auto = () => _game != null && !_game.InGame && _title != null && _title.Visible,
            });

            // 6. 游戏规则（教程直接把页面切过来，界面和文字必须对得上）
            _steps.Add(new Step
            {
                Title = "游戏规则",
                Body = "上面三个标签是「玩法 / 按键 / 特殊方块」，内容多的时候右侧可以滚动。\n"
                    + "特殊方块那页尤其值得翻一遍；不用全记住，知道去哪儿查就行。",
                Hole = () => PageRect(),
                Enter = () => GoRules(0),
                NoBack = true,      // 前一步在游戏里，回不去了，别给「上一步」
                PanelCorner = true,
                Button = "看完了，下一步",
            });

            // 8. 设置主页（同样由教程切过去）
            _steps.Add(new Step
            {
                Title = "进设置看看",
                Body = "设置分四组：音乐、操作、屏幕、玩法。下面一组一组看，"
                    + "跟着「下一步」走就行。",
                Hole = () => PageRect(),
                PanelCorner = true,
                Allow = new string[0],   // 四个入口全锁：点进去会打乱讲解顺序
                Enter = () => GoPage(TitleScreen.PageMain),
            });

            // 9~12. 四个分组逐个介绍
            _steps.Add(new Step
            {
                Title = "设置 · 音乐",
                Body = "这里是背景音乐和音效的音量，拖滑条就能调，左边那颗是静音。",
                Hole = () => PageRect(),
                PanelCorner = true,
                Enter = () => GoPage(TitleScreen.PageSound),
            });
            _steps.Add(new Step
            {
                Title = "设置 · 操作",
                Body = MobileMode.Enabled
                    ? "这里是键位布局：初级四个键，高级九宫格（带前后移动和俯视旋转）；\n"
                        + "下面还能切「移动」用滑动还是十字键。"
                    : "这里可以给每个动作单独改键，也能一键恢复默认。\n"
                        + "下面那行是落点提示开关。",
                Hole = () => PageRect(),
                PanelCorner = true,
                Enter = () => GoPage(TitleScreen.PageControl),
            });
            _steps.Add(new Step
            {
                Title = "设置 · 屏幕",
                Body = "亮色 / 暗色主题，以及画面相关的开关，挑顺眼的就行。",
                Hole = () => PageRect(),
                PanelCorner = true,
                Enter = () => GoPage(TitleScreen.PageScreen),
            });
            _steps.Add(new Step
            {
                Title = "设置 · 玩法",
                Body = "场地尺寸、下落速度、消除方式（行消 / 层消）、特殊方块机制开关都在这里。\n"
                    + "刚上手时机制是关着的，熟了再一项项打开。",
                Hole = () => PageRect(),
                PanelCorner = true,
                Enter = () => GoPage(TitleScreen.PagePlay),
            });

            // 13. 创意工坊（切过去）
            _steps.Add(new Step
            {
                Title = "最后是创意工坊",
                BottomEdge = true,      // 工坊内容长，说明板直接贴页面底边
                Body = "这里可以自己做方块模具，做好的模具会混进平时的出块池。",
                Hole = () => PageRect(),
                PanelCorner = true,
                Enter = () => GoPage(TitleScreen.PageWorkshop),
            });

            // 14. 工坊说明
            _steps.Add(new Step
            {
                Title = "创意工坊怎么用",
                BottomEdge = true,
                Body = "左边是模具列表，中间是三维建造台：点一格放一块，再点一下拿走。\n"
                    + "右边可以试拼自己的模具，满意了保存、启用，它就会进游戏。",
                Hole = () => PageRect(),
                PanelCorner = true,
                Enter = () => GoPage(TitleScreen.PageWorkshop),
            });

            // 15. 场地调深
            _steps.Add(new Step
            {
                Title = "把场地调深一点",
                Body = "把「深度 Z」拖到 2，然后点「应用」。\n"
                    + "两层之后才有前后移动、层消，也才有层次图。",
                Hole = () => PageRect(),
                PanelCorner = true,
                Allow = new[] { "应用" },      // 只有「应用」能点，返回之类的全锁
                Enter = () =>
                {
                    GoPage(TitleScreen.PageSize);
                    // 刚点过「应用」→ 场景重载回来，这一步已经完成了
                    if (PlayerPrefs.GetInt("tutorial.sizeApplied", 0) != 0)
                    {
                        PlayerPrefs.DeleteKey("tutorial.sizeApplied");
                        _sizeApplied = true;
                        _autoWas = false;
                    }
                },
                // 用「点过应用」判断而不是 z 的值：玩家的场地可能本来就是两层以上，
                // 按值判断会在进入这一步的瞬间自动跳过（用户遇到的就是这个）
                Auto = () => _sizeApplied,
            });

            // 16. 高级键位（手机上这开关在「操作」页；电脑上没有这个概念）
            if (MobileMode.Enabled)
            {
                _steps.Add(new Step
                {
                    Title = "开高级键位",
                    Body = "应用之后场地会重建成两层（会先回到主界面）。\n"
                        + "把「键位」这一行点成「高级」—— 高级键位多出前后移动和俯视旋转，"
                        + "两层的场地才玩得转。",
                    Hole = () => PageRect(),
                    PanelCorner = true,
                    PanelBottom = true,     // 开关在页面上面几行，别盖住
                    Allow = new[] { "键位：初级", "键位：高级" },
                    Enter = () => GoPage(TitleScreen.PageControl),
                    Auto = () => GameSettings.KeyLayout == GameSettings.KeysAdvanced,
                });
            }
            else
            {
                _steps.Add(new Step
                {
                    Title = "按键设置",
                    Body = "电脑上的操作都在「按键设置」里：A / D 左右、W / S 前后、空格硬降。\n"
                        + "想改就进去改，不想动直接下一步。",
                    Hole = () => PageRect(),
                    PanelCorner = true,
                    // 停在「操作」页而不是按键页：下一页要讲按键页，
                    // 两步都停在同一个屏幕上，点「上一步」会像卡住了
                    Enter = () => GoPage(TitleScreen.PageControl),
                });
            }

            // 17. 高级键位怎么用
            _steps.Add(new Step
            {
                Title = "高级键位怎么用",
                // 点「上一步」回来时要回到键位页（不然停在上一步的页面上）
                Enter = () => GoPage(MobileMode.Enabled
                    ? TitleScreen.PageControl : TitleScreen.PageKeys),
                Body = MobileMode.Enabled
                    ? "九宫格三排：\n"
                        + "上排 ← ↑ → 是俯视转（从上往下看转一格）和上翻；\n"
                        + "中排左边面内左转、右边面内右转，正中空着；\n"
                        + "下排是软降、下翻、硬降。\n"
                        + "切到「移动：十字键」之后，左下角还会多出四颗方向键：左右移动、前后推拉。"
                    : "电脑上是全功能键位：\n"
                        + "A / D 左右，W / S 前后；E / Q 面内顺逆时针；空格硬降、左 Shift 软降。\n"
                        + "C / Z 是从上往下看顺时针 / 逆时针旋转；R / V 是把方块往上 / 往下翻面。\n"
                        + "两层场地要靠 W / S、C / Z 或 R / V 才玩得开。",
            });

            // 18. 回主界面、开局
            _steps.Add(new Step
            {
                Title = "进场地看看",
                Body = "两层深的场地长什么样，进去看看。\n点「开始游戏」。",
                Hole = () => MenuRect("开始游戏"),
                Allow = new[] { "开始游戏" },
                // 这一步讲的是主界面上的「开始游戏」，从下一步退回来时人还在场地里，
                // 必须真的退到主界面，界面才和文字对得上（用户要求）
                Enter = () => GoMenu(),
                Auto = () => _game != null && _game.InGame,
            });

            // 19. 层次图
            _steps.Add(new Step
            {
                Title = "层次图",
                Body = MobileMode.Enabled
                    ? "场地两层深以后，右上角会多一个「层次图」折叠键。点开它：能看到每一层的填充情况，"
                        + "滑条选层，缺口一目了然。\n在层次图上拖动不会带动视角，放心滑。"
                    : "右上角这块就是「层次图」：每一层的填充情况一目了然，滑条选层。\n"
                        + "它一直开着，不影响操作；想看哪一层就拖滑条。",
                Hole = () =>
                {
                    if (_layerMap == null)
                        return ZeroRect();
                    if (_layerMap.TabRect.width > 4f)
                        return _layerMap.TabRect;          // 手机：折叠键
                    return _layerMap.PanelRect;             // 电脑：面板本身
                },
                // 电脑上没有折叠键，教程直接把它打开，给一颗「下一步」往下走；
                // 手机必须自己点开折叠键，所以留 Auto
                Enter = () =>
                {
                    if (!MobileMode.Enabled && _layerMap != null)
                        _layerMap.Open = true;
                },
                Auto = MobileMode.Enabled
                    ? (Func<bool>)(() => _layerMap != null && _layerMap.IsOpen)
                    : null,
            });

            // 18. 收尾
            _steps.Add(new Step
            {
                Title = "教程到这里",
                Body = "剩下的就自己摸索吧。特殊方块机制在「设置 → 玩法 → 特殊方块机制」里，"
                    + "十四项随时可以开。\n玩得开心。",
                Button = "开始玩",
            });
        }

        // ── 界面复原 ─────────────────────────────────────────────────
        //
        // 「上一步」只回退步骤是不够的 —— 步骤讲的是某一块界面，回退时必须把界面也复原，
        // 否则人停在 A 页面、文字讲 B 页面，或者根本还留在游戏里（用户反复踩的坑）。
        // 所以每一步都声明 Enter，由下面这几个助手统一负责「把界面摆成这一步要的样子」。

        /// <summary>摆到「主界面」（不带任何设置面板），并跳过开场动画。</summary>
        void GoMenu()
        {
            _enterPage = -1;
            if (_title == null)
                return;
            _title.Show(true);
            _title.SkipIntro();
        }

        /// <summary>摆到某个设置页（会先退出游戏界面回到主界面，再打开那一页）。</summary>
        void GoPage(int page)
        {
            _enterPage = page;
            if (_title == null)
                return;
            _title.Show(true);
            _title.SkipIntro();
            _title.OpenSettingsPage(page);
        }

        /// <summary>摆到游戏规则的某一栏。</summary>
        void GoRules(int tab)
        {
            _enterPage = TitleScreen.PageGuide;
            if (_title == null)
                return;
            _title.Show(true);
            _title.SkipIntro();
            _title.OpenRulesPage(tab);
        }

        // ── 状态推进 ─────────────────────────────────────────────────

        void EnterStep(int index)
        {
            _step = index;
            _sizeApplied = false;
            // 进来时条件就成立的话先别跳：说明玩家早就做过这件事（老存档 / 重播教程），
            // 这一步该讲的话还没讲。等他真的做一遍（条件变成成立）再放行。
            _autoWas = _steps[index].Auto != null && _steps[index].Auto();
            _cur = index >= 0 && index < _steps.Count ? _steps[index] : null;
            _stepT = Time.time;
            PlayerPrefs.SetInt(PrefStep, index);
            PlayerPrefs.Save();

            if (_steps[index].Enter != null)
                _steps[index].Enter();
        }

        void Advance()
        {
            if (_step + 1 >= _steps.Count)
            {
                Finish();
                return;
            }
            EnterStep(_step + 1);
        }

        void Finish()
        {
            _running = false;
            _step = -1;
            _cur = null;
            if (_hud != null)
                _hud.HidePausePanel = false;
            TutorialBlocker.ClearRects();
            PlayerPrefs.SetInt(PrefDone, 1);
            PlayerPrefs.DeleteKey(PrefStep);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// 重新走一遍教程（「游戏规则」页里的「新手教程」按钮走这里）。
        /// 不清 asked 标记：这只是重播，不算第一次进来的询问。
        /// </summary>
        public void Restart()
        {
            PlayerPrefs.SetInt(PrefAsked, 1);
            PlayerPrefs.SetInt(PrefDone, 0);
            PlayerPrefs.DeleteKey(PrefStep);
            PlayerPrefs.Save();

            _asking = false;
            _running = true;
            if (_title != null)
            {
                _title.Show(true);      // 收起设置，让第 1 步的「开始游戏」露出来
                _title.SkipIntro();     // 已经看过一次了，别再播开场
            }
            EnterStep(0);
        }

        void Choose(bool beginner)
        {
            _asking = false;
            PlayerPrefs.SetInt(PrefAsked, 1);
            if (!beginner)
            {
                PlayerPrefs.SetInt(PrefDone, 1);
                PlayerPrefs.Save();
                return;
            }
            PlayerPrefs.SetInt(PrefDone, 0);
            _running = true;
            EnterStep(0);
        }

        // ── 高亮区域 ─────────────────────────────────────────────────

        static Rect ZeroRect()
        {
            return new Rect(0f, 0f, 0f, 0f);
        }

        Rect MenuRect(string label)
        {
            // 只在主菜单真的画着的时候算数：设置一开，菜单就不画了，
            // 里面存的是上一帧的旧矩形，照着框会指到空白处
            if (_title == null || !_title.Visible || _title.ShowSettings)
                return ZeroRect();

            Rect r;
            return _title.TryMenuRect(label, out r) ? r : ZeroRect();
        }

        Rect PageRect()
        {
            if (_title == null || !_title.ShowSettings)
                return ZeroRect();
            return _title.PageRect;
        }

        /// <summary>设置主页那四个入口按钮（音乐 / 操作 / 屏幕 / 玩法）。</summary>
        Rect SettingsEntryRect(string name)
        {
            if (_title == null || !_title.ShowSettings || _title.CurrentPage != TitleScreen.PageMain)
                return ZeroRect();
            Rect r;
            return _title.TryMenuRect("设置/" + name, out r) ? r : ZeroRect();
        }

        /// <summary>左上角的统计面板（分数 / 消除 / 最高），用 HUD 实际画出来的那块。</summary>
        Rect ScoreRect()
        {
            // 不在游戏里就没有这两块界面，别框一片空白
            if (_game != null && !_game.InGame)
                return ZeroRect();

            if (_hud != null && _hud.StatsRect.width > 4f)
                return _hud.StatsRect;

            return new Rect(UiSkin.P(14f), UiSkin.P(14f), UiSkin.P(246f), UiSkin.P(158f));
        }

        /// <summary>底部操作区：手机是整条操作带，电脑没有屏幕按键，框住右下角示意。</summary>
        Rect ControlsRect()
        {
            if (_game != null && !_game.InGame)
                return ZeroRect();

            if (_mobile != null && _mobile.StripHeight > 0f)
                return new Rect(0f, Screen.height - _mobile.StripHeight, Screen.width, _mobile.StripHeight);

            // 电脑上操作全在键盘上，屏幕上没有对应的一块：不框，留给文字说明
            return ZeroRect();
        }

        // ── 绘制 ─────────────────────────────────────────────────────

        void EnsureStyles()
        {
            if (_styles)
                return;
            _styles = true;

            _hStyle = new GUIStyle();
            _hStyle.font = UiSkin.Font;
            _hStyle.fontSize = UiSkin.Size(21);
            _hStyle.fontStyle = FontStyle.Bold;
            _hStyle.alignment = TextAnchor.UpperLeft;
            _hStyle.wordWrap = true;    // 窄屏（手机竖屏）标题要能折成两行，不能裁掉
            _hStyle.normal.textColor = UiSkin.TextPrimary;

            _bStyle = new GUIStyle();
            _bStyle.font = UiSkin.Font;
            _bStyle.fontSize = UiSkin.Size(16);
            _bStyle.alignment = TextAnchor.UpperLeft;
            _bStyle.wordWrap = true;
            _bStyle.normal.textColor = UiSkin.TextSecondary;

            _btnStyle = new GUIStyle(GUI.skin.button);
            _btnStyle.font = UiSkin.Font;
            _btnStyle.fontSize = UiSkin.Size(17);
            _btnStyle.alignment = TextAnchor.MiddleCenter;

            _skipStyle = new GUIStyle();
            _skipStyle.font = UiSkin.Font;
            _skipStyle.fontSize = UiSkin.Size(14);
            _skipStyle.alignment = TextAnchor.MiddleCenter;
            _skipStyle.normal.textColor = new Color(1f, 1f, 1f, 0.75f);

            _optTitle = new GUIStyle();
            _optTitle.font = UiSkin.Font;
            _optTitle.fontSize = UiSkin.Size(21);
            _optTitle.fontStyle = FontStyle.Bold;
            _optTitle.alignment = TextAnchor.MiddleCenter;
            _optTitle.normal.textColor = UiSkin.TextPrimary;

            _hintStyle = new GUIStyle();
            _hintStyle.font = UiSkin.Font;
            _hintStyle.fontSize = UiSkin.Size(15);
            _hintStyle.alignment = TextAnchor.MiddleCenter;
            _hintStyle.wordWrap = true;
            _hintStyle.normal.textColor = new Color(UiSkin.Accent.r, UiSkin.Accent.g, UiSkin.Accent.b, 0.95f);

            _toastStyle = new GUIStyle();
            _toastStyle.font = UiSkin.Font;
            _toastStyle.fontSize = UiSkin.Size(15);
            _toastStyle.alignment = TextAnchor.MiddleCenter;
            _toastStyle.normal.textColor = Color.white;

            _btnLabel = new GUIStyle();
            _btnLabel.font = UiSkin.Font;
            _btnLabel.fontSize = UiSkin.Size(18);
            _btnLabel.alignment = TextAnchor.MiddleCenter;
            _btnLabel.normal.textColor = new Color(0.09f, 0.13f, 0.18f);

            // 次要按钮是半透明底，暗色主题下得用白字
            _btnLabelAlt = new GUIStyle(_btnLabel);
            _btnLabelAlt.normal.textColor = Color.white;

            _optBody = new GUIStyle();
            _optBody.font = UiSkin.Font;
            _optBody.fontSize = UiSkin.Size(15);
            _optBody.alignment = TextAnchor.UpperCenter;
            _optBody.wordWrap = true;
            _optBody.normal.textColor = UiSkin.TextSecondary;
        }

        /// <summary>
        /// 「下一步」的底：竖着的渐变。
        ///
        /// 亮色主题是浅蓝（接近白 → 浅蓝），暗色主题是浅灰（接近白 → 浅灰）——
        /// 对应的文字都用近黑色：浅底上再放白字就糊了。贴图两套各建一次，
        /// 换主题时才重建（每帧 new Texture2D 会一直漏显存）。
        /// </summary>
        Texture2D ButtonTexture()
        {
            bool light = UiSkin.Light;
            if (_btnTex != null && _btnTexLight == light)
                return _btnTex;

            if (_btnTex != null)
                Destroy(_btnTex);

            const int h = 64;
            _btnTex = new Texture2D(4, h, TextureFormat.RGBA32, false);
            _btnTex.wrapMode = TextureWrapMode.Clamp;
            _btnTex.filterMode = FilterMode.Bilinear;

            Color top, bottom;
            if (light)
            {
                top = new Color(0.93f, 0.97f, 1f);      // 近白的浅蓝
                bottom = new Color(0.71f, 0.84f, 1f);   // 浅蓝
            }
            else
            {
                top = new Color(0.97f, 0.97f, 0.98f);   // 近白
                bottom = new Color(0.79f, 0.81f, 0.85f); // 浅灰
            }

            for (int y = 0; y < h; y++)
            {
                Color c = Color.Lerp(bottom, top, y / (float)(h - 1));   // 贴图 y 向上，所以反着插值
                for (int x = 0; x < 4; x++)
                    _btnTex.SetPixel(x, y, c);
            }
            _btnTex.Apply();
            _btnTexLight = light;
            return _btnTex;
        }

        /// <summary>画一颗「浅色渐变 + 深色文字」的按钮，返回有没有被点。</summary>
        bool DrawActionButton(Rect r, string label)
        {
            GUI.DrawTexture(r, ButtonTexture(), ScaleMode.StretchToFill, false);

            Color line = UiSkin.Light
                ? new Color(UiSkin.Accent.r, UiSkin.Accent.g, UiSkin.Accent.b, 0.55f)
                : new Color(1f, 1f, 1f, 0.45f);
            float t = UiSkin.P(1.5f);
            Fill(new Rect(r.x, r.y, r.width, t), line);
            Fill(new Rect(r.x, r.yMax - t, r.width, t), line);
            Fill(new Rect(r.x, r.y, t, r.height), line);
            Fill(new Rect(r.xMax - t, r.y, t, r.height), line);

            GUI.Label(r, label, _btnLabel);
            return GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        /// <summary>
        /// 圆角矩形：中间一块 + 上下按行切出来的圆角。
        ///
        /// 最早用「四个角的 1/4 圆贴图」，踩了两次坑：贴图的 y 轴和 GUI 是反的，
        /// 一开始四个角变成四个白括号，反过来之后又变成缺角。这种一像素级的对齐
        /// 不值得再赌一次，索性按行切：每行按圆的方程收一个 inset，纯 Fill，方向不会错。
        /// </summary>
        static void RoundedFill(Rect r, Color c, float radius)
        {
            float rad = Mathf.Clamp(radius, 0f, Mathf.Min(r.width, r.height) * 0.5f);
            if (rad < 1.5f)
            {
                Fill(r, c);
                return;
            }

            // 中间整块（上下各让出 rad）
            Fill(new Rect(r.x, r.y + rad, r.width, r.height - rad * 2f), c);

            int steps = Mathf.Max(3, Mathf.CeilToInt(rad));
            float band = rad / steps;
            for (int i = 0; i < steps; i++)
            {
                float dy = rad - (i + 0.5f) * band;                     // 这一行离圆心多高
                float inset = rad - Mathf.Sqrt(Mathf.Max(0f, rad * rad - dy * dy));
                float yTop = r.y + i * band;
                float yBot = r.yMax - (i + 1) * band;
                Fill(new Rect(r.x + inset, yTop, r.width - inset * 2f, band + 0.5f), c);
                Fill(new Rect(r.x + inset, yBot, r.width - inset * 2f, band + 0.5f), c);
            }
        }

        /// <summary>教程面板/卡片的底色：半透明，后面压着的游戏画面能透出来。</summary>
        static Color GlassFill
        {
            get
            {
                return UiSkin.Light
                    ? new Color(1f, 1f, 1f, 0.88f)
                    : new Color(0.12f, 0.14f, 0.18f, 0.90f);
            }
        }

        static void Fill(Rect r, Color c)
        {
            Color prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = prev;
        }

        void OnGUI()
        {
            if (!Running)
                return;

            EnsureStyles();
            UiSkin.Apply();
            GUI.depth = -1000;      // 压在标题页 / HUD 上面，按钮也优先吃到点击

            if (_asking)
            {
                DrawAsk();
                CursorTrail.DrawTop();
                return;
            }

            DrawStep();
            CursorTrail.DrawTop();     // 光斑压在遮罩和说明板之上
        }

        /// <summary>
        /// 开场的二选一：标题居中一行，下面左右两张卡片（小白在左、达人在右）。
        ///
        /// 卡片自己画底 + 一颗透明按钮收点击：直接用 GUI.Button 的灰底会让说明字
        /// （次要文字色）糊在灰上，几乎看不清。
        /// </summary>
        void DrawAsk()
        {
            bool portrait = MobileMode.Enabled && Screen.width <= Screen.height;

            Fill(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.55f));

            float pad = UiSkin.P(portrait ? 22f : 28f);
            float gap = UiSkin.P(portrait ? 14f : 18f);
            // 竖屏占满、横屏稍窄、电脑最窄（一行字太长反而难读）
            float wFrac = portrait ? 0.96f : (MobileMode.Enabled ? 0.86f : 0.68f);
            float pw = Mathf.Min(Screen.width * wFrac, UiSkin.P(portrait ? 640f : 820f));
            float innerW = pw - pad * 2f;
            // 竖屏：两个选项上下排；横屏 / 电脑：左右并排
            float cardW = portrait ? innerW : (innerW - gap) * 0.5f;

            string[] titles = { "游戏小白", "游戏达人" };
            string[] bodies =
            {
                "玩法、界面、设置、创意工坊\n从头走一遍",
                "跳过教学，直接开始\n（以后可在规则页重开）",
            };

            float titleH = UiSkin.P(portrait ? 34f : 38f);
            float bodyPad = UiSkin.P(portrait ? 16f : 20f);
            var wrapped = new string[2];
            var bodyW = new float[2];
            var bodyH = new float[2];
            var cardH = new float[2];
            for (int i = 0; i < 2; i++)
            {
                float innerCard = cardW - bodyPad * 2f;
                wrapped[i] = CjkWrap.Wrap(bodies[i], _optBody, innerCard, false);
                bodyW[i] = Mathf.Min(innerCard,
                    Mathf.Max(UiSkin.P(40f), CjkWrap.MaxLineWidth(wrapped[i], _optBody)) + UiSkin.P(4f));
                bodyH[i] = _optBody.CalcHeight(new GUIContent(wrapped[i]), bodyW[i]);
                // +8 给字形上下留余量：只按 CalcHeight 给，最后一行的下缘会贴边
                cardH[i] = titleH + UiSkin.P(14f) + bodyH[i] + bodyPad * 2f + UiSkin.P(14f);
            }

            string question = innerW < UiSkin.P(460f)
                ? "你是游戏小白，\n还是游戏达人？" : "你是游戏小白，还是游戏达人？";
            float qH = Mathf.Max(UiSkin.P(40f),
                _hStyle.CalcHeight(new GUIContent(question), innerW));

            float cardsH = portrait ? cardH[0] + gap + cardH[1] : Mathf.Max(cardH[0], cardH[1]);
            float ph = pad * 2f + qH + UiSkin.P(16f) + cardsH;

            var panel = new Rect((Screen.width - pw) * 0.5f, (Screen.height - ph) * 0.5f, pw, ph);

            float rad = UiSkin.P(16f);
            RoundedFill(new Rect(panel.x + UiSkin.P(2f), panel.y + UiSkin.P(5f), panel.width, panel.height),
                new Color(0f, 0f, 0f, 0.22f), rad);
            RoundedFill(panel, GlassFill, rad);

            var qStyle = _hStyle;
            qStyle.alignment = TextAnchor.MiddleCenter;
            GUI.Label(new Rect(panel.x + pad, panel.y + pad, innerW, qH), question, qStyle);
            qStyle.alignment = TextAnchor.UpperLeft;

            float top = panel.y + pad + qH + UiSkin.P(16f);
            // 先把两张卡的位置算出来（悬停判断要用）
            var cardA = portrait
                ? new Rect(panel.x + pad, top, cardW, cardH[0])
                : new Rect(panel.x + pad, top, cardW, cardsH);
            var cardB = portrait
                ? new Rect(panel.x + pad, top + cardH[0] + gap, cardW, cardH[1])
                : new Rect(panel.x + pad + cardW + gap, top, cardW, cardsH);

            // 悬停时亮一点：两张卡并排，没有反馈显得死板
            bool hot0 = cardA.Contains(Event.current.mousePosition);
            bool hot1 = cardB.Contains(Event.current.mousePosition);
            Color cardFillA = UiSkin.Light
                ? new Color(1f, 1f, 1f, hot0 ? 0.95f : 0.72f)
                : new Color(0.22f, 0.26f, 0.33f, hot0 ? 0.95f : 0.78f);
            Color cardFillB = UiSkin.Light
                ? new Color(1f, 1f, 1f, hot1 ? 0.95f : 0.72f)
                : new Color(0.22f, 0.26f, 0.33f, hot1 ? 0.95f : 0.78f);
            Color cardLine = new Color(UiSkin.Accent.r, UiSkin.Accent.g, UiSkin.Accent.b, 0.50f);

            for (int i = 0; i < 2; i++)
            {
                var card = i == 0 ? cardA : cardB;

                float cRad = UiSkin.P(12f);
                RoundedFill(new Rect(card.x - UiSkin.P(2f), card.y + UiSkin.P(2f),
                        card.width + UiSkin.P(4f), card.height + UiSkin.P(4f)),
                    new Color(0f, 0f, 0f, 0.12f), cRad + UiSkin.P(2f));
                // 描边用「大一圈的圆角矩形垫在下面」画：四条直边在圆角处会断成四个括号，
                // 双色叠一层才是一圈完整的边
                RoundedFill(new Rect(card.x - UiSkin.P(1.5f), card.y - UiSkin.P(1.5f),
                    card.width + UiSkin.P(3f), card.height + UiSkin.P(3f)), cardLine, cRad + UiSkin.P(1.5f));
                RoundedFill(card, i == 0 ? cardFillA : cardFillB, cRad);

                float ty = portrait ? UiSkin.P(16f) : UiSkin.P(18f);
                GUI.Label(new Rect(card.x, card.y + ty, cardW, titleH), titles[i], _optTitle);
                GUI.Label(new Rect(card.x + bodyPad, card.y + ty + titleH + UiSkin.P(10f),
                    bodyW[i], bodyH[i] + UiSkin.P(8f)), wrapped[i], _optBody);

                if (GUI.Button(card, GUIContent.none, GUIStyle.none))
                    Choose(i == 0);
            }

            // 面板以外全挡掉：不然点卡片会顺带点到下面对应位置的主菜单按钮
            //（IMGUI 先画的控件先吃点击，标题页在教程之前）
            UpdateBlockers(ZeroRect(), panel);
        }

        void DrawStep()
        {
            if (_step < 0 || _step >= _steps.Count)
                return;

            Step s = _steps[_step];

            // 高亮区域：这一步有就挖洞，没有就整屏压暗
            _hole = s.Hole != null ? s.Hole() : ZeroRect();
            _hasHole = _hole.width > 4f && _hole.height > 4f;

            float fade = Mathf.Clamp01((Time.time - _stepT) / 0.25f);
            Color scrim = new Color(0f, 0f, 0f, 0.45f * fade);

            // ── 先定说明板宽度，再排版 ────────────────────────────────
            // 原来顺序反了：先按一个「假宽度」换行算高度，之后又为了贴角把宽度改小，
            // 结果文字和按钮都按旧宽度摆，直接溢出面板（用户截图里那样）。
            // 现在宽度只有一处来源，后面全都用 panel.width。
            bool portrait = MobileMode.Enabled && Screen.width <= Screen.height;
            float pad = UiSkin.P(22f);

            float wFrac, wMax;
            if (s.PanelCorner)
            {
                // 讲整页的步骤：面板窄一点、贴左下角（页面下半基本是空的）
                wFrac = portrait ? 0.96f : 0.46f;
                wMax = portrait ? 720f : 620f;
            }
            else
            {
                wFrac = portrait ? 0.96f : (MobileMode.Enabled ? 0.86f : 0.70f);
                wMax = portrait ? 720f : 780f;
            }
            float pw = Mathf.Min(Screen.width * wFrac, UiSkin.P(wMax));

            // 竖屏讲设置页时：面板不能比页面宽，也不能压住页面底部那颗「返回」
            Rect pageR = (_title != null && _title.ShowSettings) ? _title.PageRect : ZeroRect();
            bool sitInPage = portrait && pageR.width > UiSkin.P(120f);
            if (sitInPage)
                pw = Mathf.Min(pw, pageR.width - UiSkin.P(16f));

            float innerW = pw - pad * 2f;

            string body = s.Body ?? "";
            string wrapped = CjkWrap.Wrap(body, _bStyle, innerW, false);
            float bw = Mathf.Min(innerW,
                Mathf.Max(UiSkin.P(40f), CjkWrap.MaxLineWidth(wrapped, _bStyle)) + UiSkin.P(4f));
            float bodyH = _bStyle.CalcHeight(new GUIContent(wrapped), bw);
            float btnH = UiSkin.P(48f);
            float titleH = Mathf.Max(UiSkin.P(32f),
                _hStyle.CalcHeight(new GUIContent(s.Title ?? ""), innerW));
            bool canBack = _step > 0 && !s.NoBack;
            float ph = pad * 2f + titleH + UiSkin.P(10f) + bodyH + UiSkin.P(16f) + btnH;

            float margin = UiSkin.P(18f);
            // 可用高度不够时把内边距压掉一些，别让面板顶出屏幕
            float maxH = Screen.height - margin * 2f;
            if (ph > maxH)
            {
                float over = ph - maxH;
                pad = Mathf.Max(UiSkin.P(10f), pad - over * 0.5f);
                ph = maxH;
            }

            // ── 定位 ──────────────────────────────────────────────────
            Rect panel;
            if (s.PanelCorner)
            {
                panel = new Rect(margin, Screen.height - ph - margin, pw, ph);
                if (panel.y < margin)
                    panel = new Rect(panel.x, margin, pw, ph);
            }
            else
            {
                var atBottom = new Rect((Screen.width - pw) * 0.5f, Screen.height - ph - margin, pw, ph);
                var atTop = new Rect((Screen.width - pw) * 0.5f, margin, pw, ph);
                bool overlaps = _hasHole && atBottom.Overlaps(_hole);
                bool holeLow = _hasHole && _hole.yMax > Screen.height * 0.62f;
                panel = (overlaps || holeLow) && !s.PanelBottom ? atTop : atBottom;
                if (panel.y < margin)
                    panel = new Rect(panel.x, margin, pw, ph);
            }

            // 高亮区让位：整页高亮时面板必然压住其中一段，
            // 干脆把被压住的那一条从高亮里剪掉 —— 露出来的永远是真界面，不是面板叠着界面
            if (_hasHole && !s.PanelCorner && panel.Overlaps(_hole))
            {
                bool panelOnTop = panel.y < _hole.y + _hole.height * 0.5f;
                if (panelOnTop)
                {
                    float ny = Mathf.Min(_hole.yMax, panel.yMax + UiSkin.P(6f));
                    float nh = _hole.yMax - ny;
                    _hole = new Rect(_hole.x, ny, _hole.width, Mathf.Max(0f, nh));
                }
                else
                {
                    float ny2 = Mathf.Max(_hole.y, panel.y - UiSkin.P(6f));
                    _hole = new Rect(_hole.x, _hole.y, _hole.width, Mathf.Max(0f, ny2 - _hole.y));
                }
                _hasHole = _hole.height > 4f;
            }

            // 遮罩要按最终的高亮区画，所以挪到让位之后
            DrawScrim(scrim);

            // 竖屏设置页：底边让开页面底部那行按钮（返回 / 新手教程）
            if (sitInPage)
            {
                float bottomGap = s.BottomEdge ? UiSkin.P(8f) : UiSkin.P(80f);
                float py2 = pageR.yMax - bottomGap - ph;
                if (py2 < pageR.y + UiSkin.P(8f))
                    py2 = pageR.y + UiSkin.P(8f);
                panel = new Rect(pageR.x + (pageR.width - pw) * 0.5f, py2, pw, ph);
            }

            float rad = UiSkin.P(14f);
            // 投影画得比面板大一圈：面板是半透明的，同尺寸的深色垫在下面会把整块染成灰的
            RoundedFill(new Rect(panel.x - UiSkin.P(3f), panel.y + UiSkin.P(3f),
                    panel.width + UiSkin.P(6f), panel.height + UiSkin.P(6f)),
                new Color(0f, 0f, 0f, 0.16f), rad + UiSkin.P(3f));
            // 整页讲解时后面是密集文字，透上来很花；这种步骤把面板做「实」一点
            Color fill = GlassFill;
            if (s.PanelCorner)
                fill.a = UiSkin.Light ? 0.96f : 0.95f;
            RoundedFill(panel, fill, rad);
            // 顶边一条细高光，用来交代「这是一块板」而不是糊在地上的颜色
            Fill(new Rect(panel.x + rad, panel.y, panel.width - rad * 2f, UiSkin.P(2f)),
                new Color(UiSkin.Accent.r, UiSkin.Accent.g, UiSkin.Accent.b, 0.55f));

            GUI.Label(new Rect(panel.x + pad, panel.y + pad, innerW, titleH), s.Title, _hStyle);

            float ty = panel.y + pad + titleH + UiSkin.P(10f);
            GUI.Label(new Rect(panel.x + pad, ty, bw, bodyH + UiSkin.P(6f)), wrapped, _bStyle);

            // 按钮：「上一步」在左、主按钮在右；第一步没有上一步。
            //
            // 有 Auto 条件的步骤是「要玩家自己动手」的（点开始游戏、退出到主界面、
            // 把 z 调到 2、切高级键位、点开层次图）—— 这种不给「下一步」：
            // 给了就能不做事直接跳过，教程会跑到一个还没打开的界面上讲（用户踩过）。
            // 有 Auto 条件 + 进来时条件就已经满足（老存档 / 重播）→ 给一颗「下一步」当出口，
            // 否则这一步既不会自动跳、又没有按钮，直接卡住
            bool alreadyDone = s.Auto != null && _autoWas;
            bool hasPrimary = s.Auto == null || s.Action != null || alreadyDone;
            float by = panel.yMax - pad - btnH;
            float gapBtn = UiSkin.P(16f);

            // 有主按钮：上一步 + 主按钮成对居中；
            // 没有主按钮（要玩家自己动手）：上一步靠左收窄，提示文字占右边剩下的宽度
            // —— 之前两者各按「撑满」算宽度，窄屏上提示会被挤出面板右边（截图里那样）
            float btw = hasPrimary
                ? Mathf.Min(UiSkin.P(240f), (innerW - gapBtn) * 0.5f)
                : Mathf.Min(UiSkin.P(140f), innerW * 0.42f);

            if (hasPrimary)
            {
                float totalW = canBack ? btw * 2f + gapBtn : btw;
                float bx = panel.x + (pw - totalW) * 0.5f;
                if (canBack)
                {
                    if (DrawGhostButton(new Rect(bx, by, btw, btnH), "上一步"))
                        Back();
                    bx += btw + gapBtn;
                }

                bool isAction = s.Action != null;
                if (DrawActionButton(new Rect(bx, by, btw, btnH),
                        isAction ? (s.ActionLabel ?? "继续") : s.Button))
                {
                    if (isAction)
                        s.Action();
                    else
                        Advance();
                }
            }
            else
            {
                float hintX = panel.x + pad;
                if (canBack)
                {
                    if (DrawGhostButton(new Rect(hintX, by, btw, btnH), "上一步"))
                        Back();
                    hintX += btw + gapBtn;
                }
                float hintW = Mathf.Max(UiSkin.P(60f), panel.x + pw - pad - hintX);
                GUI.Label(new Rect(hintX, by, hintW, btnH),
                    "按提示操作即可继续", _hintStyle);
            }

            // 右上角：随时可以放弃教程。先垫一层暗底 —— 下面可能是层次图面板，
            // 白字直接压上去会糊成一片。
            // 跳过键 + 进度合成一颗小胶囊：之前两块叠一起，又宽又挡页面标题
            float skipW = UiSkin.P(150f);
            var skipBox = new Rect(Screen.width - skipW - UiSkin.P(10f), UiSkin.P(8f),
                skipW, UiSkin.P(38f));
            RoundedFill(skipBox, new Color(0f, 0f, 0f, 0.46f), skipBox.height * 0.5f);
            var skip = new Rect(skipBox.x + UiSkin.P(10f), skipBox.y,
                skipBox.width - UiSkin.P(20f), skipBox.height);
            if (GUI.Button(skip, "跳过教程  " + (_step + 1) + "/" + _steps.Count, _skipStyle))
                Finish();

            // 挡板：除了高亮那块、说明板、跳过键，其余区域的点击都吃掉
            UpdateBlockers(_hasHole ? _hole : ZeroRect(), panel, skipBox);

            DrawToast();
        }

        /// <summary>压暗全屏，只把当前高亮那块留出来（带一圈呼吸的框线）。</summary>
        void DrawScrim(Color scrim)
        {
            if (!_hasHole)
            {
                Fill(new Rect(0f, 0f, Screen.width, Screen.height), scrim);
                return;
            }

            // 边界取整：小数坐标下相邻两块遮罩会各盖住同一个像素 40%，
            // 叠出来的 alpha 比周围深，屏幕上就是一条横贯全屏的黑线
            Rect hole = new Rect(Mathf.Round(_hole.x), Mathf.Round(_hole.y),
                Mathf.Round(_hole.width), Mathf.Round(_hole.height));
            Fill(new Rect(0f, 0f, Screen.width, Mathf.Max(0f, hole.y)), scrim);
            Fill(new Rect(0f, hole.yMax, Screen.width, Mathf.Max(0f, Screen.height - hole.yMax)), scrim);
            Fill(new Rect(0f, hole.y, Mathf.Max(0f, hole.x), hole.height), scrim);
            Fill(new Rect(hole.xMax, hole.y, Mathf.Max(0f, Screen.width - hole.xMax), hole.height), scrim);

            float a = 0.65f + 0.25f * Mathf.Sin(Time.time * 3f);
            Color line = new Color(UiSkin.Accent.r, UiSkin.Accent.g, UiSkin.Accent.b, a);
            float t = UiSkin.P(2.5f);
            Fill(new Rect(hole.x, hole.y, hole.width, t), line);
            Fill(new Rect(hole.x, hole.yMax - t, hole.width, t), line);
            Fill(new Rect(hole.x, hole.y, t, hole.height), line);
            Fill(new Rect(hole.xMax - t, hole.y, t, hole.height), line);
        }

        void Back()
        {
            if (_step <= 0)
                return;
            EnterStep(_step - 1);
            // 退回来的这一步，自动条件大概率已经满足（比如场地已经是 z=2），
            // 不挡一下就会立刻弹回原步骤，看起来像「上一步没反应」
            _autoWas = true;
        }

        void Toast(string text)
        {
            _toast = text;
            _toastUntil = Time.unscaledTime + 1.6f;
        }

        void DrawToast()
        {
            if (_toast == null || Time.unscaledTime > _toastUntil)
                return;

            float w = Mathf.Min(Screen.width * 0.8f, UiSkin.P(520f));
            float h = UiSkin.P(44f);
            float a = Mathf.Clamp01((_toastUntil - Time.unscaledTime) / 0.4f);
            var r = new Rect((Screen.width - w) * 0.5f, Screen.height * 0.12f, w, h);
            RoundedFill(r, new Color(0.06f, 0.08f, 0.11f, 0.82f * a), h * 0.5f);
            GUI.Label(r, _toast, _toastStyle);
            if (_toastStyle.normal.textColor.a != a)
            {
                Color c = Color.white;
                c.a = a;
                _toastStyle.normal.textColor = c;
            }
        }

        /// <summary>次要按钮（上一步）：透明底 + 细边框。</summary>
        bool DrawGhostButton(Rect r, string label)
        {
            Fill(r, UiSkin.Light ? new Color(1f, 1f, 1f, 0.55f) : new Color(1f, 1f, 1f, 0.10f));
            Color line = new Color(UiSkin.Accent.r, UiSkin.Accent.g, UiSkin.Accent.b, 0.55f);
            float t = UiSkin.P(1.5f);
            Fill(new Rect(r.x, r.y, r.width, t), line);
            Fill(new Rect(r.x, r.yMax - t, r.width, t), line);
            Fill(new Rect(r.x, r.y, t, r.height), line);
            Fill(new Rect(r.xMax - t, r.y, t, r.height), line);

            GUI.Label(r, label, UiSkin.Light ? _btnLabel : _btnLabelAlt);
            return GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        // ── 点击挡板 ─────────────────────────────────────────────────

        static readonly List<Rect> _blockers = new List<Rect>();

        /// <summary>
        /// 算出「哪些区域要吃掉点击」：整屏减去要高亮的、说明板、跳过键。
        /// 结果交给 TutorialBlocker（它比标题页先画，所以能抢在菜单按钮之前吃掉事件）。
        /// </summary>
        void UpdateBlockers(params Rect[] keep)
        {
            _blockers.Clear();

            var ys = new List<float> { 0f, Screen.height };
            for (int i = 0; i < keep.Length; i++)
            {
                if (keep[i].width < 4f || keep[i].height < 4f)
                    continue;
                ys.Add(Mathf.Clamp(keep[i].y, 0f, Screen.height));
                ys.Add(Mathf.Clamp(keep[i].yMax, 0f, Screen.height));
            }
            ys.Sort();

            for (int i = 0; i + 1 < ys.Count; i++)
            {
                float y0 = ys[i];
                float y1 = ys[i + 1];
                if (y1 - y0 < 1f)
                    continue;

                // 这一条横向带子被哪些「保留区」占着（按左边界排序）
                var spans = new List<Rect>();
                for (int k = 0; k < keep.Length; k++)
                {
                    Rect r = keep[k];
                    if (r.width < 4f || r.height < 4f)
                        continue;
                    if (r.y <= y0 + 0.5f && r.yMax >= y1 - 0.5f)
                        spans.Add(r);
                }
                spans.Sort((a, b) => a.x.CompareTo(b.x));

                float cursor = 0f;
                for (int k = 0; k < spans.Count; k++)
                {
                    float left = Mathf.Max(0f, spans[k].x);
                    if (left - cursor > 1f)
                        _blockers.Add(new Rect(cursor, y0, left - cursor, y1 - y0));
                    cursor = Mathf.Max(cursor, Mathf.Min(Screen.width, spans[k].xMax));
                }
                if (Screen.width - cursor > 1f)
                    _blockers.Add(new Rect(cursor, y0, Screen.width - cursor, y1 - y0));
            }

            TutorialBlocker.SetRects(_blockers);
        }

    }
}
