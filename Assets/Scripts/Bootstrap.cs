using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>
    /// 场景里唯一的入口：建逻辑、建视图、接相机。
    /// 字段是 Inspector 里可调的场地尺寸。
    /// </summary>
    public class Bootstrap : MonoBehaviour
    {
        public int SizeX = 10;
        public int SizeY = 20;
        public int SizeZ = 1;

        GameLoop _loop;
        GridView _view;
        KeyboardInput _input;
        SfxPlayer _sfx;
        MusicPlayer _music;
        Hud _hud;
        LayerMap _layerMap;
        TitleScreen _title;
        NextPreview _nextPreview;
        MoldWorkbench _workbench;
        ParticleBurst _particles;
        bool _paused;
        float _autoShotTimer = 6f;
        float _forceGameOverAt = -1f;
        bool _autoShotDone;
        bool _demoMode;
        bool _demoBurst;
        bool _titleMode;
        int _titleShotIndex;
        bool _structureDemo;
        // -resizetest：自动把窗口来回改大小，验证缩放冻结链路（日志里看 [resize] 冻住/恢复）
        bool _resizeTest;
        int _resizeStep;
        float _resizeT;
        bool _resizeDone;
        float _resizeDelay = 4f;      // 等 Unity 启动画面走完、进到游戏界面再开始切
        static readonly float[] TitleShotTimes = { 0.4f, 1.3f, 2.5f, 3.4f, 4.2f };
        Camera _mainCam;
        CameraRig _rig;
        bool _camLogged;
        int _normalCullMask;

        /// <summary>场外那些会走动的傀儡，重开一局时整批销毁。</summary>
        readonly System.Collections.Generic.List<GolemPet> _golems
            = new System.Collections.Generic.List<GolemPet>();

        void Start()
        {
            SizeX = GameSettings.SizeX;
            SizeY = GameSettings.SizeY;
            SizeZ = GameSettings.SizeZ;
            ApplyDebugSizeArgs();

            Fluids.Init();

            // 水不写进网格，但攒满一格要能凑成一行；消行时那一格的水也一起清掉。
            Grid.CountsAsFilled = (x, y, z) => Fluids.AmountAt(x, y, z) >= Fluids.MaxAmount;
            Grid.OnCellCleared = (x, y, z) => Fluids.ClearCell(x, y, z);

            _loop = new GameLoop(SizeX, SizeY, SizeZ, (int)System.DateTime.Now.Ticks);
            _loop.FallSpeed = GameSettings.FallSpeed;

            // 结构方块的还原动画：消失阶段一格一格擦掉，重建阶段一格一格从上面掉回来
            Structure.CellVanished += OnStructureCellVanished;
            Structure.CellDropped += OnStructureCellDropped;

            _view = gameObject.AddComponent<GridView>();
            _view.Setup(_loop.Grid);

            _input = gameObject.AddComponent<KeyboardInput>();
            _sfx = gameObject.AddComponent<SfxPlayer>();
            _music = gameObject.AddComponent<MusicPlayer>();
            _music.SetVolume(GameSettings.MusicVolume);

            _hud = gameObject.AddComponent<Hud>();
            _hud.Loop = _loop;
            _hud.OnRestartClick = RestartGame;
            _hud.OnResumeClick = OnResumeClick;
            _hud.OnQuitToTitleClick = QuitToTitle;

            _nextPreview = gameObject.AddComponent<NextPreview>();
            _nextPreview.Init(_view);
            _hud.Preview = _nextPreview;

            // 创意工坊的三维建造台：自带一台相机渲到贴图里，只有打开那一页时才开着
            _workbench = gameObject.AddComponent<MoldWorkbench>();
            _workbench.Init();
            _title.Workbench = _workbench;
            _workbench.OnChanged += _title.SaveMoldEdit;

            // 场地三格以上才有必要挂俯视图：中间列和后排从正面看不见，
            // 层消差在哪一格只能靠它。两格深不建，省得白占一块屏幕。
            if (LayerMap.UsefulFor(SizeZ))
            {
                _layerMap = gameObject.AddComponent<LayerMap>();
                _layerMap.Loop = _loop;
                _layerMap.Hidden = true;    // 开始界面盖着的时候不画
            }

            _particles = gameObject.AddComponent<ParticleBurst>();
            _particles.Init(_view);

            // 光标深蓝荧光拖尾（纯 IMGUI 绘制，不参与游戏逻辑）
            gameObject.AddComponent<CursorTrail>();

            _title = gameObject.AddComponent<TitleScreen>();
            _title.HasContinue = GameSave.HasSave;
            _title.OnStartGame += StartNewGame;
            _title.OnContinue += ContinueGame;
            _title.OnQuit += QuitGame;
            _title.OnSfxChanged += OnSfxChanged;
            _title.OnKeysChanged += OnKeysChanged;
            _title.OnSizeApplied += OnSizeApplied;
            _title.OnFallSpeedChanged += OnFallSpeedChanged;
            _title.OnMusicChanged += OnMusicChanged;
            _sfx.Enabled = _title.SfxEnabled;

            // 命令行带 -auto 时跳过开始界面，方便自动化截图与调试
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-auto") >= 0)
                _title.Visible = false;
            _demoMode = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-demo") >= 0;
            _titleMode = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-title") >= 0;
            if (_titleMode)
                _title.Visible = true;
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-settings") >= 0)
            {
                _title.Visible = true;
                _title.ShowSettings = true;
            }
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-settingskeys") >= 0)
            {
                _title.Visible = true;
                _title.OpenSettingsPage(1);
            }
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-settingssize") >= 0)
            {
                _title.Visible = true;
                _title.OpenSettingsPage(2);
            }
            // -settingsspeed / -settingsmusic：直接开下落速度、音乐音量页（都是滑条），截图核对用
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-settingsspeed") >= 0)
            {
                _title.Visible = true;
                _title.OpenSettingsPage(4);
            }
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-settingsmusic") >= 0)
            {
                _title.Visible = true;
                _title.OpenSettingsPage(5);
            }
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-workshop") >= 0)
            {
                _title.Visible = true;
                _title.OpenSettingsPage(3);
            }
            // -guide：直接打开「游戏规则」页的玩法栏，截图核对用
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-guide") >= 0)
            {
                _title.Visible = true;
                _title.OpenRulesPage(0);
            }
            // -rules N：打开规则页的第 N 栏（0 玩法 / 1 按键 / 2 特殊方块），截图核对用
            int rulesArg = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-rules");
            if (rulesArg >= 0)
            {
                int ruleTab = 0;
                string[] ruleArgs = System.Environment.GetCommandLineArgs();
                if (rulesArg + 1 < ruleArgs.Length)
                    int.TryParse(ruleArgs[rulesArg + 1], out ruleTab);
                _title.Visible = true;
                _title.OpenRulesPage(ruleTab);
            }
            // -mechsettings：直接打开「特殊方块机制」开关页，截图核对用
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-mechsettings") >= 0)
            {
                _title.Visible = true;
                _title.OpenSettingsPage(TitleScreen.PageMech);
            }
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-pause") >= 0)
            {
                _title.Visible = false;
                _paused = true;
            }
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-gameover") >= 0)
                _forceGameOverAt = 1.5f;
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-sandtest") >= 0)
                RunSandTest();
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-reacttest") >= 0)
                RunReactTest();
            // -tnttest：场上摆一颗 TNT + 一块红石，看引信闪白、爆炸范围和连锁
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-tnttest") >= 0)
                RunTntTest();
            // -leaftest：摆一块原木并让它长树叶
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-leaftest") >= 0)
                RunLeafTest();
            // -bedrocktest：底层铺满，左列中间夹基岩、右列同样高度作对照
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-bedrocktest") >= 0)
                RunBedrockTest();
            // -structuretest：摆一场结构方块的演示，F5 可以再放一遍
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-structuretest") >= 0)
            {
                _structureDemo = true;
                RunStructureTest();
            }

            // -resizetest：每 0.2 秒把窗口在两档尺寸之间来回切，共 8 次。
            // 切换比冻结窗口（0.35 秒）更密，所以整段测试会一直保持冻结，最后
            // 在日志里吐出一条「冻住期间」的帧耗时统计。
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-resizetest") >= 0)
                _resizeTest = true;

            // -resizebench：配合 -resizetest。冻结期不做限速，让每帧耗时反映真实
            // 的缩放成本（限速状态下每帧都被压成 125ms，什么都看不出来）。
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-resizebench") >= 0)
                ResizeGuard.Bench = true;

            // -glasstest：玻璃柱上压沙，看玻璃被压碎、沙落下来
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-glasstest") >= 0)
                RunGlassTest();
            // -glassdroptest：三根柱子对比「谁先在那儿」，看玻璃落下不碎、被后压的才碎
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-glassdroptest") >= 0)
                RunGlassDropTest();
            // -melttest：雪块 / 冰立在石槽上，一秒半后往槽里灌岩浆，看它化水再被烫成黑曜石
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-melttest") >= 0)
                RunMeltTest();
            // -firetest：两根「木板 + 羊毛 + 树叶」柱子，一秒半后往左边灌岩浆，看火往上烧
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-firetest") >= 0)
                RunFireTest();
            // -spongetest：干海绵挨着一汪水会吸干并变湿，湿海绵旁边的水吸不动
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-spongetest") >= 0)
                RunSpongeTest();
            // -golemtest：南瓜头压在雪块 / 铁块 / 铜块 / 石头上，看前三个召出傀儡、第四个没反应
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-golemtest") >= 0)
                RunGolemTest();
            // -golempose：场地中央摆三个傀儡站着不动，专门用来核对模型和贴图
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-golempose") >= 0)
                RunGolemPose();
            // -blocktest <方块英文名>：在场地高处摆三个该方块，用来肉眼核对六面贴图
            var btArgv = System.Environment.GetCommandLineArgs();
            int btAt = System.Array.IndexOf(btArgv, "-blocktest");
            if (btAt >= 0 && btAt + 1 < btArgv.Length)
                RunBlockTest(btArgv[btAt + 1]);

            // -fluidtest water|lava：在半空放一汪水/岩浆，看它流下来再漫开
            int ftAt = System.Array.IndexOf(btArgv, "-fluidtest");
            if (ftAt >= 0 && ftAt + 1 < btArgv.Length)
                RunFluidTest(btArgv[ftAt + 1]);

            // -yawtest：摆一个 I 型，一份不转、一份俯视转 90°，看画面里形状对不对
            if (System.Array.IndexOf(btArgv, "-yawtest") >= 0)
                RunYawTest();

            // -pitchtest：摆三个 T 型，分别是不转 / 往背面翻 / 往正面翻，看前后翻对不对
            if (System.Array.IndexOf(btArgv, "-pitchtest") >= 0)
                RunPitchTest();

            // 凡是「摆一个场面给你看」的开关，都自动跳过标题界面。
            // 不这么做的话：标题显示时相机的 cullingMask 是 0（画面里只有菜单，场地是空的），
            // 而按空格开始游戏又会 Grid.Clear() 把刚摆好的场面一起清掉 —— 单给 -tnttest 什么都看不见。
            string[] sceneTests =
            {
                "-sandtest", "-reacttest", "-tnttest", "-leaftest", "-bedrocktest", "-structuretest",
                "-blocktest", "-fluidtest", "-yawtest", "-pitchtest", "-glasstest", "-glassdroptest",
                "-melttest", "-golemtest", "-golempose", "-firetest", "-spongetest",
            };
            for (int i = 0; i < sceneTests.Length; i++)
            {
                if (System.Array.IndexOf(btArgv, sceneTests[i]) >= 0)
                {
                    _title.Visible = false;
                    break;
                }
            }

            // -shotat N：把自动截图的时间点从默认 6 秒改成 N 秒（验证落定方块等需要跑够时间）
            var argv = System.Environment.GetCommandLineArgs();
            int shotArg = System.Array.IndexOf(argv, "-shotat");
            if (shotArg >= 0 && shotArg + 1 < argv.Length && float.TryParse(argv[shotArg + 1], out float shotAt))
                _autoShotTimer = shotAt;

            _loop.PieceLocked += OnPieceLocked;
            _loop.LinesClearedEvent += OnLinesCleared;
            _loop.RowsCleared += OnRowsCleared;
            _loop.Spawned += OnSpawned;
            _loop.PieceRotated += OnPieceRotated;
            _loop.GravitySettled += OnGravitySettled;
            _loop.FluidChanged += OnFluidChanged;
            _loop.FluidReacted += OnFluidReacted;
            _loop.TntPrimed += OnTntPrimed;
            _loop.TntExploded += OnTntExploded;
            _loop.FoliageGrew += OnFoliageGrew;
            _loop.GlassCrushed += OnGlassCrushed;
            _loop.SnowMelted += OnSnowMelted;
            _loop.GolemSummoned += OnGolemSummoned;
            _loop.FireIgnited += OnFireIgnited;
            _loop.BlocksBurned += OnBlocksBurned;
            _loop.WaterSoaked += OnWaterSoaked;

            _view.RefreshAll();
            _view.SnapPiece(_loop.Current);
            _nextPreview.Show(_loop.Next, _loop.NextBlockId);

            SetupScene();
        }

        void Update()
        {
            // 放在最前面：窗口一变尺寸就让各系统进入降载状态，这一帧它们才吃得上。
            ResizeGuard.Tick();

            if (_resizeTest)
                TickResizeTest();

            // 拖窗口期间整个游戏停摆：动画冻结、界面不画，输入也不读。
            // 松手 0.35 秒后 ResizeGuard 自己还原，这里不用管恢复。
            if (ResizeGuard.Frozen)
                return;

            HideSystemCursor();

            if (_loop == null)
                return;

            if (Input.GetKeyDown(KeyCode.F12))
                CaptureShot("manual_shot.png");

            // -structuretest 下按 F5 重新摆一遍，方便反复看那一波扫回去
            if (_structureDemo && Input.GetKeyDown(KeyCode.F5))
                RunStructureTest();

            if (!_camLogged && Time.time > 3f && _mainCam != null)
            {
                _camLogged = true;
                Transform ct = _mainCam.transform;
                Debug.Log("[cam] pos=" + ct.position + " euler=" + ct.eulerAngles
                    + " up=" + ct.up + " fov=" + _mainCam.fieldOfView
                    + " rigYaw=" + (_rig != null ? _rig.Yaw : -999f)
                    + " rigPitch=" + (_rig != null ? _rig.Pitch : -999f)
                    + " | bootEuler=" + transform.eulerAngles
                    + " viewEuler=" + (_view != null ? _view.transform.eulerAngles : Vector3.zero)
                    + " pieceRootEuler=" + (_view != null && _view.PieceRoot != null ? _view.PieceRoot.eulerAngles : Vector3.zero));
            }

            if (_forceGameOverAt > 0f)
            {
                _forceGameOverAt -= Time.deltaTime;
                if (_forceGameOverAt <= 0f)
                {
                    _loop.Score = 880;
                    _loop.GameOver = true;
                }
            }

            // 开始界面动画分阶段截图（调试用 -title）
            if (_titleMode && _titleShotIndex < TitleShotTimes.Length && Time.time >= TitleShotTimes[_titleShotIndex])
            {
                CaptureShot("title_" + _titleShotIndex + ".png");
                _titleShotIndex++;
            }

            if (!_autoShotDone)
            {
                _autoShotTimer -= Time.deltaTime;
                if (_autoShotTimer <= 0f)
                {
                    _autoShotDone = true;
                    CaptureShot("auto_shot.png");
                }
            }

            if (_workbench != null && _title != null)
                _workbench.Active = _title.Visible && _title.WorkshopOpen;

            // 建造台里右键拖动是转盒子、滚轮是缩放，主相机的轨道脚本读的是同一套输入。
            // 不把它关掉的话，转完建造台回到游戏，场地的视角也跟着被转走了。
            if (_rig != null)
                _rig.enabled = !(_title != null && _title.Visible && _title.WorkshopOpen);

            if (_title != null && _title.Visible)
            {
                _title.HasContinue = GameSave.HasSave;
                SetHudHidden(true);
                if (_view != null)
                    _view.HideGhost();
                if (_mainCam != null)
                    _mainCam.cullingMask = 0;
                if (_music != null)
                {
                    // 标题界面永远不该是「暂停」状态：从暂停里退回标题时，
                    // 这之后 Update 会一路 return，没人再去解暂停，音乐会一直卡着。
                    _music.SetPaused(false);
                    _music.SetMenuMode(true);
                }
                if (_title.AnyInputPressed())
                    StartNewGame();
                return;
            }
            SetHudHidden(false);
            if (_music != null)
                _music.SetMenuMode(false);
            if (_mainCam != null && _mainCam.cullingMask != _normalCullMask)
                _mainCam.cullingMask = _normalCullMask;

            if (Input.GetKeyDown(KeyCode.Escape))
                _paused = !_paused;

            if (_hud != null)
                _hud.Paused = _paused;
            if (_music != null)
                _music.SetPaused(_paused);

            if (_loop.GameOver)
            {
                GameSave.Clear();
                if (_view != null)
                    _view.HideGhost();   // 都结束了还指着落点没意义，而且会一直亮在那儿
                if (Input.GetKeyDown(KeyCode.R))
                    StartNewGame();
                return;
            }

            if (_paused)
                return;

            _loop.Tick(Time.deltaTime, _input.Read());
            _view.RefreshPiece(_loop.Current);
            SyncTntFlash();
            SyncFire();
            SyncGhost();

            // 调试用：-demo 启动时在自动截图前炸一把粒子，方便截图看效果
            if (_demoMode && !_demoBurst && Time.time > 5.2f)
            {
                _demoBurst = true;
                var rows = new System.Collections.Generic.List<int> { 2, 3, 4 };
                var blocks = new System.Collections.Generic.List<int> { 12, 150, 260 };
                OnRowsCleared(rows, blocks);
            }
        }

        /// <summary>
        /// 把系统光标藏掉，只留蓝色荧光拖尾当指针。拖尾自己就画了「光标那一颗」
        /// （CursorTrail.Draw 结尾的三层光斑，鼠标不动时也在），两个一起出现反而像有两个鼠标。
        ///
        /// 每帧设一次而不是只设一次：切窗口焦点、切全屏时 Unity 会把可见性改回去。
        /// </summary>
        static void HideSystemCursor()
        {
            if (Cursor.visible)
                Cursor.visible = false;
        }

        /// <summary>
        /// -resizetest 的驱动：每 0.6 秒换一档窗口尺寸。用 unscaled 计时，
        /// 冻结期 deltaTime 是 0，用它就永远走不到下一步。
        /// </summary>
        void TickResizeTest()
        {
            if (_resizeDone)
                return;

            if (_resizeDelay > 0f)
            {
                _resizeDelay -= Time.unscaledDeltaTime;
                return;
            }

            // 间隔压到 0.2 秒：比 ResizeGuard 的 0.35 秒窗口短，整段测试会一直处于
            // 冻结状态，最后吐出一条完整的「冻住期间」统计。
            _resizeT += Time.unscaledDeltaTime;
            if (_resizeT < 0.2f)
                return;
            _resizeT = 0f;

            _resizeStep++;
            if (_resizeStep > 8)
            {
                _resizeDone = true;
                Debug.Log("[resizetest] 完成，共切了 8 档");
                return;
            }

            bool odd = (_resizeStep & 1) == 1;
            int w = odd ? 920 : 760;
            int h = odd ? 680 : 560;
            Screen.SetResolution(w, h, false);
            Debug.Log("[resizetest] 第 " + _resizeStep + " 档 " + w + "x" + h);

            // 这一步必定处在冻结期（每一步都比 ResizeGuard 的 0.35 秒窗口短），
            // 截图用来确认「拖窗口时 UI 是不是还在」。
            if (_resizeStep == 4)
                CaptureShot("resize_frozen.png");
        }

        void OnDestroy()
        {
            // 还回去：编辑器里停掉 Play 之后要是没鼠标就麻烦了
            Cursor.visible = true;
        }

        void CaptureShot(string fileName)
        {
            string path = System.IO.Path.Combine(Application.persistentDataPath, fileName);
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log("[MinecraftTetris] 截图: " + path);
        }

        void RestartGame()
        {
            StartNewGame();
        }

        void StartNewGame()
        {
            GameSave.Clear();
            Fluids.Reset();
            Fluids.Prepare(_loop.Grid);
            _loop.Restart();
            // 傀儡是「收藏」，新开一局要清空，不然上一局的雕像还摆在场上
            Golem.Reset();
            ClearGolemModels();
            _loop.FallSpeed = GameSettings.FallSpeed;
            _particles.Clear();
            _paused = false;
            _view.RefreshAll();
            _view.ClearFlash();
            _view.ClearFire();
            _lastBurning.Clear();
            _view.SnapPiece(_loop.Current);
            _nextPreview.Show(_loop.Next, _loop.NextBlockId);
            if (_title != null)
                _title.Show(false);
            SetHudHidden(false);
            GameSave.Save(_loop);
        }

        /// <summary>开始界面盖着的时候 HUD 和俯视图一起收起来，别露在标题画面上。</summary>
        void SetHudHidden(bool hidden)
        {
            if (_hud != null)
                _hud.Hidden = hidden;
            if (_layerMap != null)
                _layerMap.Hidden = hidden;
        }

        void ContinueGame()
        {
            if (!GameSave.TryLoad(_loop))
            {
                StartNewGame();
                return;
            }

            _particles.Clear();
            Tnt.Reset();
            _paused = false;
            _view.RefreshAll();
            _view.ClearFlash();
            _view.SnapPiece(_loop.Current);
            _nextPreview.Show(_loop.Next, _loop.NextBlockId);
            if (_title != null)
                _title.Show(false);
            SetHudHidden(false);
            GameSave.Save(_loop);
        }

        void QuitGame()
        {
            GameSave.Save(_loop);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>调试用：-size X Y Z 只覆盖本次运行的场地尺寸，不写盘。</summary>
        void ApplyDebugSizeArgs()
        {
            string[] args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-size");
            if (i < 0 || i + 3 >= args.Length)
                return;

            int sx, sy, sz;
            if (int.TryParse(args[i + 1], out sx) && int.TryParse(args[i + 2], out sy) &&
                int.TryParse(args[i + 3], out sz))
            {
                SizeX = Mathf.Clamp(sx, GameSettings.MinSizeX, GameSettings.MaxSizeX);
                SizeY = Mathf.Clamp(sy, GameSettings.MinSizeY, GameSettings.MaxSizeY);
                SizeZ = Mathf.Clamp(sz, GameSettings.MinSizeZ, GameSettings.MaxSizeZ);
            }
        }

        void OnSfxChanged(bool enabled)
        {
            if (_sfx != null)
                _sfx.Enabled = enabled;
        }

        /// <summary>下落速度改完立刻生效，不用重开一局。</summary>
        void OnFallSpeedChanged()
        {
            if (_loop != null)
                _loop.FallSpeed = GameSettings.FallSpeed;
        }

        /// <summary>音乐音量改完立刻生效（0 = 关）。</summary>
        void OnMusicChanged()
        {
            if (_music != null)
                _music.SetVolume(GameSettings.MusicVolume);
        }

        void OnKeysChanged()
        {
            if (_input != null)
                _input.Reload();
        }

        void OnSizeApplied()
        {
            GameSave.Clear();
            UnityEngine.SceneManagement.SceneManager.LoadScene(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
        }

        void OnResumeClick()
        {
            _paused = false;
        }

        void QuitToTitle()
        {
            _paused = false;
            GameSave.Save(_loop);
            if (_particles != null)
                _particles.Clear();
            if (_title != null)
            {
                _title.Show(true);
                _title.SkipIntro();
            }
        }

        void RunSandTest()
        {
            int sandId = -1;
            for (int i = 0; i < BlockCatalog.Blocks.Length; i++)
            {
                if (BlockCatalog.Blocks[i].Name == "sand")
                {
                    sandId = i + 1;
                    break;
                }
            }
            if (sandId <= 0 || _loop == null || _view == null)
            {
                Debug.Log("[sandtest] 找不到沙子方块");
                return;
            }

            int cx = _loop.Grid.SizeX / 2;
            int top = _loop.Grid.SizeY - 4;
            _loop.Grid.Set(cx, top, 0, sandId);
            _loop.Grid.Set(cx + 2, top, 0, sandId);
            _loop.Grid.Set(cx + 3, top, 0, sandId);
            _loop.Grid.Set(cx + 2, top + 1, 0, sandId);
            _view.RefreshAll();

            bool moved = Gravity.Settle(_loop.Grid);
            _view.RefreshAll();
            Debug.Log("[sandtest] 重力结算=" + moved);
        }

        /// <summary>
        /// 玻璃被压碎：三根玻璃柱上各压两块沙，跑一次「压碎 + 重力」，
        /// 看玻璃一层层碎掉、沙落到柱脚原来的位置。
        /// </summary>
        void RunGlassTest()
        {
            if (_loop == null || _view == null)
                return;

            int glassId = BlockCatalog.GlassId;
            int sandId = 0, stoneId = 0;
            for (int i = 0; i < BlockCatalog.Blocks.Length; i++)
            {
                if (BlockCatalog.Blocks[i].Name == "sand")
                    sandId = i + 1;
                else if (BlockCatalog.Blocks[i].Name == "stone")
                    stoneId = i + 1;
            }
            if (glassId <= 0 || sandId <= 0)
            {
                Debug.Log("[glasstest] 方块表里缺玻璃或沙子");
                return;
            }

            _loop.Restart();
            Fluids.Reset();
            Tnt.Reset();
            Structure.Reset();

            Grid grid = _loop.Grid;
            int cx = grid.SizeX / 2;
            int cz = grid.SizeZ / 2;
            int yBase = Mathf.Clamp(grid.SizeY / 6, 2, 4);

            // 底下一层石头当基座，三根玻璃柱立在上面，柱顶各压两块沙。
            // 有基座才看得出「玻璃碎完，沙落回地面」；不然沙会停在半空（下面本来就是空的）。
            for (int i = -2; i <= 2; i += 2)
            {
                Put(grid, cx + i, 0, cz, stoneId);
                Put(grid, cx + i, yBase, cz, glassId);
                Put(grid, cx + i, yBase + 1, cz, glassId);
                Put(grid, cx + i, yBase + 2, cz, sandId);
                Put(grid, cx + i, yBase + 3, cz, sandId);
            }

            _view.SnapPiece(_loop.Current);
            _view.RefreshAll();

            // 先让玩家看清「玻璃上压着沙」，一秒半后再压碎，不然一进场就是结果
            StartCoroutine(CrushGlassSoon(1.5f));
        }

        /// <summary>
        /// -glassdroptest：专门演「玻璃落下来不碎，之后被压上来才碎」。
        ///
        /// 三根柱子都立在同一层石头上，区别只在写入先后（也就是「谁先在那儿」）：
        ///   左：玻璃先、石头后 —— 石头是新压上来的，玻璃碎；
        ///   中：石头先、玻璃后 —— 玻璃是落进早就有的方块底下，不碎；
        ///   右：玻璃、玻璃    —— 上方那块也是玻璃，不算压（连放两块玻璃不会自己炸）。
        /// 一秒半后才判定，先让人看清摆的是什么。
        /// </summary>
        void RunGlassDropTest()
        {
            if (_loop == null || _view == null)
                return;

            int glassId = BlockCatalog.GlassId;
            int stoneId = 0;
            for (int i = 0; i < BlockCatalog.Blocks.Length; i++)
                if (BlockCatalog.Blocks[i].Name == "stone")
                    stoneId = i + 1;
            if (glassId <= 0 || stoneId <= 0)
            {
                Debug.Log("[glassdroptest] 方块表里缺玻璃或石头");
                return;
            }

            _loop.Restart();
            Fluids.Reset();
            Tnt.Reset();
            Structure.Reset();

            Grid grid = _loop.Grid;
            int cx = grid.SizeX / 2;
            int cz = grid.SizeZ / 2;
            int left = Mathf.Max(0, cx - 3);
            int right = Mathf.Min(grid.SizeX - 1, cx + 3);

            // 三根柱子底下都铺一块石头，玻璃碎完才有东西接着
            Put(grid, left, 0, cz, stoneId);
            Put(grid, cx, 0, cz, stoneId);
            Put(grid, right, 0, cz, stoneId);

            // 左：玻璃先落，石头后压上来
            Put(grid, left, 2, cz, glassId);
            Put(grid, left, 3, cz, stoneId);

            // 中：石头先在那儿，玻璃后落进它下面
            Put(grid, cx, 3, cz, stoneId);
            Put(grid, cx, 2, cz, glassId);

            // 右：两块玻璃叠着，上面那块也是玻璃
            Put(grid, right, 2, cz, glassId);
            Put(grid, right, 3, cz, glassId);

            _view.SnapPiece(_loop.Current);
            _view.RefreshAll();

            StartCoroutine(CrushGlassSoon(1.5f));
        }

        /// <summary>
        /// -golempose：场地中央一字排开三个傀儡、站着不动，用来核对模型和贴图。
        /// 活动范围锁在脚下，所以它们不会走开。
        /// </summary>
        void RunGolemPose()
        {
            if (_loop == null || _view == null)
                return;

            _loop.Restart();
            Fluids.Reset();
            Tnt.Reset();
            Structure.Reset();
            Melt.Reset();
            Fire.Reset();
            Golem.Reset();
            ClearGolemModels();

            int cx = _loop.Grid.SizeX / 2;

            // -golempose 后面可以再跟一个 0/1/2，只摆一个傀儡、镜头正对着它，
            // 核对模型细节时不用被旁边的挡住
            string[] argv = System.Environment.GetCommandLineArgs();
            int only = -1;
            for (int i = 0; i < argv.Length - 1; i++)
                if (argv[i] == "-golempose" && int.TryParse(argv[i + 1], out int v))
                    only = v;

            if (only == 0)
                SpawnGolemAt(GolemKind.Snow, 0, new Vector3(cx, 0f, 0f));
            else if (only == 1)
                SpawnGolemAt(GolemKind.Iron, 0, new Vector3(cx, 0f, 0f));
            else if (only == 2)
                SpawnGolemAt(GolemKind.Copper, 0, new Vector3(cx, 0f, 0f));
            else
            {
                SpawnGolemAt(GolemKind.Snow, 0, new Vector3(cx - 3, 0f, 0f));
                SpawnGolemAt(GolemKind.Iron, 0, new Vector3(cx, 0f, 0f));
                SpawnGolemAt(GolemKind.Copper, 0, new Vector3(cx + 3, 0f, 0f));
            }

            _view.SnapPiece(_loop.Current);
            _view.RefreshAll();

            // 镜头得等 _rig 建好（Bootstrap 里相机是在 Start 末尾才创建的，
            // 这里执行时它还是 null，直接设会被 if 跳过，看起来就是「镜头没动」）
            StartCoroutine(FrameGolemsSoon(0.25f, cx, only));
            Debug.Log("[golempose] 摆好傀儡，only=" + only);
        }

        /// <summary>
        /// 演示用：等相机建好后对准傀儡。
        /// 正对、几乎平视 —— 斜着看会同时看到正面和侧面，很难判断部件有没有错位。
        /// 三种傀儡高矮差很多（雪 1.9 格、铁 2.7、铜 1.1），镜头得各自对准身子中间，
        /// 不然矮的那个会掉到画面底部外面。
        /// </summary>
        System.Collections.IEnumerator FrameGolemsSoon(float delay, int cx, int only)
        {
            yield return new WaitForSeconds(delay);
            if (_rig == null)
                yield break;

            float targetY = 1.3f, dist = 12f;
            if (only == 0) { targetY = 0.95f; dist = 7.5f; }
            else if (only == 1) { targetY = 1.35f; dist = 8.5f; }
            else if (only == 2) { targetY = 0.72f; dist = 6.5f; }

            _rig.Target = new Vector3(cx, targetY, 0f);
            _rig.Distance = dist;
            _rig.Yaw = 0f;
            _rig.Pitch = 2f;
            Debug.Log("[golempose] 镜头对准 only=" + only + " dist=" + dist);
        }

        /// <summary>在指定位置放一个不动的傀儡（核对模型用）。</summary>
        GolemPet SpawnGolemAt(GolemKind kind, int oxidation, Vector3 pos)
        {
            var go = new GameObject("GolemPose_" + kind);
            go.transform.SetParent(transform, false);
            var pet = go.AddComponent<GolemPet>();
            pet.Build(kind, oxidation, pos, 0f, pos, pos);
            _golems.Add(pet);
            return pet;
        }

        /// <summary>
        /// -golemtest：四种摆法排开，一秒半后结算一次召唤。
        ///
        /// 从左到右：雪傀儡（南瓜头 + 两格雪）、铁傀儡（南瓜头 + 倒 T 的四块铁）、
        /// 铜傀儡（南瓜头 + 一格铜）、对照组（南瓜头 + 石头，什么都不发生）。
        /// 南瓜头连脚下那几格一起消失，傀儡出现在场地右侧的空地上，会自己走动。
        /// </summary>
        void RunGolemTest()
        {
            if (_loop == null || _view == null)
                return;

            int headId = BlockCatalog.CarvedPumpkinId > 0
                ? BlockCatalog.CarvedPumpkinId
                : BlockCatalog.PumpkinId;
            int stoneId = BlockCatalog.Blocks.Length > 0 ? 1 : 0;
            for (int i = 0; i < BlockCatalog.Blocks.Length; i++)
                if (BlockCatalog.Blocks[i].Name == "stone")
                    stoneId = i + 1;

            if (headId <= 0 || BlockCatalog.SnowId <= 0 || BlockCatalog.IronBlockId <= 0 ||
                BlockCatalog.CopperBlockId <= 0)
            {
                Debug.Log("[golemtest] 方块表里缺南瓜 / 雪块 / 铁块 / 铜块");
                return;
            }

            _loop.Restart();
            Fluids.Reset();
            Tnt.Reset();
            Structure.Reset();
            Melt.Reset();
            Fire.Reset();
            Golem.Reset();
            ClearGolemModels();

            Grid grid = _loop.Grid;
            int cx = grid.SizeX / 2;
            int cz = grid.SizeZ / 2;
            int yb = Mathf.Clamp(grid.SizeY / 8, 1, 3);
            int xMax = Mathf.Max(1, grid.SizeX - 2);

            // 雪傀儡：竖着两格雪 + 南瓜头
            int xs = Mathf.Clamp(cx - 4, 1, xMax);
            Put(grid, xs, yb, cz, BlockCatalog.SnowId);
            Put(grid, xs, yb + 1, cz, BlockCatalog.SnowId);
            Put(grid, xs, yb + 2, cz, headId);

            // 铁傀儡：竖着两格铁，顶上那格左右各一块（倒 T），再压南瓜头
            int xi = Mathf.Clamp(cx - 1, 2, Mathf.Max(2, grid.SizeX - 3));
            Put(grid, xi, yb, cz, BlockCatalog.IronBlockId);
            Put(grid, xi, yb + 1, cz, BlockCatalog.IronBlockId);
            Put(grid, xi - 1, yb + 1, cz, BlockCatalog.IronBlockId);
            Put(grid, xi + 1, yb + 1, cz, BlockCatalog.IronBlockId);
            Put(grid, xi, yb + 2, cz, headId);

            // 铜傀儡：一格铜 + 南瓜头
            int xc = Mathf.Clamp(cx + 2, 1, xMax);
            Put(grid, xc, yb, cz, BlockCatalog.CopperBlockId);
            Put(grid, xc, yb + 1, cz, headId);

            // 对照：石头 + 南瓜头，什么都不该发生
            int xo = Mathf.Clamp(cx + 4, 1, xMax);
            Put(grid, xo, yb, cz, stoneId);
            Put(grid, xo, yb + 1, cz, headId);

            _view.SnapPiece(_loop.Current);
            _view.RefreshAll();

            // 等一会儿再结算，先让人看清四种摆法
            StartCoroutine(SummonGolemsSoon(1.5f));
            // 再等一会儿把镜头摇到傀儡区，好看清模型长什么样
            StartCoroutine(FocusGolemsSoon(3.6f));
        }

        /// <summary>演示用：把镜头摇到场地右侧的傀儡区，看清模型细节。</summary>
        System.Collections.IEnumerator FocusGolemsSoon(float delay)
        {
            yield return new WaitForSeconds(delay);
            if (_rig == null)
                yield break;

            Vector3 min = GolemPenMin();
            Vector3 max = GolemPenMax();
            _rig.Target = new Vector3((min.x + max.x) * 0.5f, 1.3f, (min.z + max.z) * 0.5f);
            _rig.Distance = 9f;
            Debug.Log("[golemtest] 镜头已转到傀儡区");
        }

        /// <summary>演示用：等一会儿再结算一次召唤（场上已经摆好组合了）。</summary>
        System.Collections.IEnumerator SummonGolemsSoon(float delay)
        {
            yield return new WaitForSeconds(delay);
            if (_loop == null || _view == null)
                yield break;

            var list = new System.Collections.Generic.List<Golem.Summon>();
            int n = Golem.Check(_loop.Grid, list);
            Gravity.Settle(_loop.Grid);
            _view.RefreshAll();
            ShowGolems(list);
            Debug.Log("[golemtest] 召出 " + n + " 个傀儡（第四个压的是石头，应该没动静）");
        }

        /// <summary>
        /// -melttest：雪块 / 冰立在石头做的浅槽上，一秒半后往槽里灌岩浆。
        ///
        /// 三组并排，唯一的差别是「槽里有没有岩浆」：
        /// - 左：雪块 + 岩浆 → 化成水，水落进槽里被岩浆烫成黑曜石；
        /// - 中：冰 + 岩浆     → 同上；
        /// - 右：雪块，槽里空着 → 对照组，一直立着不动。
        ///
        /// 为什么要石槽：岩浆是流体，直接摆在平地上会朝四面淌走，根本贴不住上面的雪块。
        /// 槽只要一格见方，底一块石头、四周各一块，灌满 8 份就封住了。
        /// </summary>
        void RunMeltTest()
        {
            if (_loop == null || _view == null)
                return;

            int snowId = BlockCatalog.SnowId;
            int iceId = BlockCatalog.IceId;
            int lavaId = Fluids.LavaId;
            int stoneId = 0;
            for (int i = 0; i < BlockCatalog.Blocks.Length; i++)
                if (BlockCatalog.Blocks[i].Name == "stone")
                    stoneId = i + 1;

            if (snowId <= 0 || iceId <= 0 || lavaId <= 0 || stoneId <= 0)
            {
                Debug.Log("[melttest] 方块表里缺雪 / 冰 / 岩浆 / 石头");
                return;
            }

            _loop.Restart();
            Fluids.Reset();
            Tnt.Reset();
            Structure.Reset();
            Melt.Reset();
            Fire.Reset();

            Grid grid = _loop.Grid;
            Fluids.Prepare(grid);   // 先把流体表建好，等下往槽里灌岩浆才写得进去

            int cx = grid.SizeX / 2;
            int cz = grid.SizeZ / 2;
            int left = Mathf.Clamp(cx - 4, 1, Mathf.Max(1, grid.SizeX - 2));
            int mid = cx;
            int right = Mathf.Clamp(cx + 4, 1, Mathf.Max(1, grid.SizeX - 2));

            MeltCup(grid, left, cz, stoneId);
            MeltCup(grid, mid, cz, stoneId);
            MeltCup(grid, right, cz, stoneId);

            Put(grid, left, 2, cz, snowId);
            Put(grid, mid, 2, cz, iceId);
            Put(grid, right, 2, cz, snowId);

            _view.SnapPiece(_loop.Current);
            _view.RefreshAll();

            // 等一秒半再灌，先让人看清「雪块 / 冰立在石槽上」的样子
            StartCoroutine(PourLavaSoon(1.5f,
                new Vector3Int(left, 1, cz), new Vector3Int(mid, 1, cz)));
        }

        /// <summary>
        /// -firetest：左右各一根「木板 + 羊毛 + 树叶 + 木板」的柱子，中间隔一道石墙。
        /// 一秒半后往左边灌岩浆：左边从底下那格开始烧，火一格一格往上窜，烧完塌掉；
        /// 右边同样一根，离岩浆远，应该一直没动静。
        /// </summary>
        void RunFireTest()
        {
            if (_loop == null || _view == null)
                return;

            int planksId = 0, woolId = 0, leavesId = 0, stoneId = 0;
            for (int i = 0; i < BlockCatalog.Blocks.Length; i++)
            {
                string n = BlockCatalog.Blocks[i].Name;
                if (n == "oak_planks") planksId = i + 1;
                else if (n == "white_wool") woolId = i + 1;
                else if (n == "oak_leaves") leavesId = i + 1;
                else if (n == "stone") stoneId = i + 1;
            }

            if (planksId <= 0 || woolId <= 0 || leavesId <= 0 || stoneId <= 0 || Fluids.LavaId <= 0)
            {
                Debug.Log("[firetest] 方块表里缺木板 / 羊毛 / 树叶 / 石头 / 岩浆");
                return;
            }

            Grid grid = _loop.Grid;
            _loop.Restart();
            Fluids.Reset();
            Tnt.Reset();
            Structure.Reset();
            Melt.Reset();
            Fire.Reset();

            int cx = grid.SizeX / 2;
            int cz = grid.SizeZ / 2;

            // 地板铺一层石头：岩浆落在上面不会漏下去，往旁边漫开才会碰到柱子
            for (int x = 0; x < grid.SizeX; x++)
                Put(grid, x, 0, cz, stoneId);

            // 中间那道石墙：隔开左右两组，右边那根才不会被漫过来的岩浆顺手点上
            for (int y = 1; y <= 3; y++)
                Put(grid, cx, y, cz, stoneId);

            int xl = Mathf.Clamp(cx - 3, 1, grid.SizeX - 2);
            Put(grid, xl, 1, cz, planksId);
            Put(grid, xl, 2, cz, woolId);
            Put(grid, xl, 3, cz, leavesId);
            Put(grid, xl, 4, cz, planksId);

            int xr = Mathf.Clamp(cx + 3, 1, grid.SizeX - 2);
            Put(grid, xr, 1, cz, planksId);
            Put(grid, xr, 2, cz, woolId);
            Put(grid, xr, 3, cz, leavesId);
            Put(grid, xr, 4, cz, planksId);

            _view.SnapPiece(_loop.Current);
            _view.RefreshAll();

            // 等一秒半再灌，先让人看清「两根柱子长得一样」的样子
            StartCoroutine(PourLavaAt(1.5f, new Vector3Int(Mathf.Clamp(xl - 1, 0, grid.SizeX - 1), 1, cz)));
        }

        /// <summary>演示用：等一会儿再往一格灌岩浆。</summary>
        System.Collections.IEnumerator PourLavaAt(float delay, Vector3Int cell)
        {
            yield return new WaitForSeconds(delay);
            if (_loop == null)
                yield break;

            Fluids.Prepare(_loop.Grid);
            Fluids.AddSource(_loop.Grid, cell.x, cell.y, cell.z, Fluids.LavaId);
            Debug.Log("[firetest] 已往左边灌岩浆，等着看火往上烧");
        }

        /// <summary>
        /// -spongetest：左边一块干海绵挨着一汪水，右边一块湿海绵也挨着一汪水。
        /// 一秒半后吸一次：左边把水吸干、自己变湿；右边本来就已经是湿的，吸不动，水还在。
        /// </summary>
        void RunSpongeTest()
        {
            if (_loop == null || _view == null)
                return;

            int stoneId = 0;
            for (int i = 0; i < BlockCatalog.Blocks.Length; i++)
                if (BlockCatalog.Blocks[i].Name == "stone")
                    stoneId = i + 1;

            if (stoneId <= 0 || Sponge.SpongeId <= 0 || Sponge.WetSpongeId <= 0 || Fluids.WaterId <= 0)
            {
                Debug.Log("[spongetest] 方块表里缺石头 / 海绵 / 湿海绵 / 水");
                return;
            }

            Grid grid = _loop.Grid;
            _loop.Restart();
            Fluids.Reset();
            Tnt.Reset();
            Structure.Reset();
            Melt.Reset();
            Fire.Reset();

            int cx = grid.SizeX / 2;
            int cz = grid.SizeZ / 2;

            // 地板铺一层石头：水落在上面不会漏下去，只会往旁边漫
            for (int x = 0; x < grid.SizeX; x++)
                Put(grid, x, 0, cz, stoneId);

            int xl = Mathf.Clamp(cx - 3, 1, grid.SizeX - 2);
            int xr = Mathf.Clamp(cx + 3, 1, grid.SizeX - 2);

            // 左边：干海绵摆好，旁边留一格空着等下灌水
            Put(grid, xl, 1, cz, Sponge.SpongeId);

            // 右边：湿海绵 + 旁边一汪水。湿海绵吸不动，水应该一直在
            Put(grid, xr, 1, cz, Sponge.WetSpongeId);

            _view.SnapPiece(_loop.Current);
            _view.RefreshAll();

            // 等一秒半再灌水和吸水，先让人看清「干海绵和湿海绵摆在一起」的样子
            StartCoroutine(SoakSoon(1.5f, new Vector3Int(Mathf.Clamp(xl + 1, 0, grid.SizeX - 1), 1, cz),
                new Vector3Int(Mathf.Clamp(xr + 1, 0, grid.SizeX - 1), 1, cz)));
        }

        /// <summary>演示用：等一会儿往两处灌水，然后扫一遍海绵（左边吸干、右边湿的吸不动）。</summary>
        System.Collections.IEnumerator SoakSoon(float delay, Vector3Int waterA, Vector3Int waterB)
        {
            yield return new WaitForSeconds(delay);
            if (_loop == null)
                yield break;

            Fluids.Prepare(_loop.Grid);
            Fluids.AddSource(_loop.Grid, waterA.x, waterA.y, waterA.z, Fluids.WaterId);
            Fluids.AddSource(_loop.Grid, waterB.x, waterB.y, waterB.z, Fluids.WaterId);
            _view.RefreshAll();
            Debug.Log("[spongetest] 两处都灌了水");

            yield return new WaitForSeconds(0.8f);
            _loop.SoakWater();
            _view.RefreshAll();
            Debug.Log("[spongetest] 吸水扫了一遍");
        }

        /// <summary>演示用的一个 1×1 石槽：底 + 四壁。角落那几块越界就不铺（场地只有 1 深时正常）。</summary>
        void MeltCup(Grid grid, int x, int z, int stoneId)
        {
            Put(grid, x, 0, z, stoneId);
            Put(grid, x - 1, 1, z, stoneId);
            Put(grid, x + 1, 1, z, stoneId);
            Put(grid, x, 1, z - 1, stoneId);
            Put(grid, x, 1, z + 1, stoneId);
        }

        /// <summary>演示用：等一会儿再往两个槽里灌岩浆，右边那个槽故意不灌，当对照。</summary>
        System.Collections.IEnumerator PourLavaSoon(float delay, Vector3Int a, Vector3Int b)
        {
            yield return new WaitForSeconds(delay);
            if (_loop == null)
                yield break;

            Fluids.Prepare(_loop.Grid);
            Fluids.AddSource(_loop.Grid, a.x, a.y, a.z, Fluids.LavaId);
            Fluids.AddSource(_loop.Grid, b.x, b.y, b.z, Fluids.LavaId);
            Debug.Log("[melttest] 已往两个槽里灌岩浆，等着看雪块 / 冰化成水");
        }

        /// <summary>演示用：等一会儿再压碎，让画面先停在场上有玻璃的状态。</summary>
        System.Collections.IEnumerator CrushGlassSoon(float delay)
        {
            yield return new WaitForSeconds(delay);
            if (_loop == null || _view == null)
                yield break;

            int crushed = CrushAllGlass();
            _view.RefreshAll();
            Debug.Log("[glasstest] 压碎 " + crushed + " 格玻璃，沙落回基座");
        }

        /// <summary>演示用：反复「压碎 + 重力」，直到没有玻璃再碎（等价于落定时那套循环）。</summary>
        int CrushAllGlass()
        {
            int total = 0;
            for (int guard = 0; guard < 64; guard++)
            {
                int n = Glass.Crush(_loop.Grid);
                if (n == 0)
                    break;
                total += n;
                Gravity.Settle(_loop.Grid);
            }
            return total;
        }

        /// <summary>调试用：摆一颗 TNT 挨一块红石，周围铺一圈石头当靶子，看引信闪白和爆炸范围。</summary>
        void RunTntTest()
        {
            if (_loop == null || _view == null)
                return;

            int tntId = Tnt.TntId;
            int redId = Tnt.RedstoneId;
            if (tntId <= 0 || redId <= 0)
            {
                Debug.Log("[tnttest] 方块表里找不到 TNT / 红石块");
                return;
            }

            int stoneId = 1;
            int obsidianId = 0, cryingId = 0, bedrockId = 0;
            int debrisId = 0, netheriteId = 0, deepslateId = 0, commandId = 0;
            for (int i = 0; i < BlockCatalog.Blocks.Length; i++)
            {
                string n = BlockCatalog.Blocks[i].Name;
                if (n == "stone") stoneId = i + 1;
                else if (n == "obsidian") obsidianId = i + 1;
                else if (n == "crying_obsidian") cryingId = i + 1;
                else if (n == "bedrock") bedrockId = i + 1;
                else if (n == "ancient_debris") debrisId = i + 1;
                else if (n == "netherite_block") netheriteId = i + 1;
                else if (n == "reinforced_deepslate") deepslateId = i + 1;
                else if (n == "command_block") commandId = i + 1;
            }

            int cx = _loop.Grid.SizeX / 2;
            int cy = Mathf.Min(6, Mathf.Max(2, _loop.Grid.SizeY - 4));
            int cz = _loop.Grid.SizeZ / 2;

            // 红石就在 TNT 右边一格：接触即点火
            _loop.Grid.Set(cx, cy, cz, tntId);
            _loop.Grid.Set(cx + 1, cy, cz, redId);

            // 周围铺一圈石头，炸掉多少一眼能看出来
            for (int dx = -2; dx <= 2; dx++)
            {
                for (int dy = -1; dy <= 2; dy++)
                {
                    if ((dx == 0 && dy == 0) || (dx == 1 && dy == 0))
                        continue;
                    int x = cx + dx;
                    int y = cy + dy;
                    if (_loop.Grid.InBounds(x, y, cz) && _loop.Grid.Get(x, y, cz) == 0)
                        _loop.Grid.Set(x, y, cz, stoneId);
                }
            }

            // 靶子里混七种「炸不掉」的方块：原版 TNT 也炸不动它们。
            // 位置都挑在爆炸半径 2 之内（dx²+dy² ≤ 4），炸完能直接看出谁留下来了。
            //   左 (-2, 0) 黑曜石      上 ( 0, 2) 哭泣的黑曜石     右 ( 2, 0) 基岩
            //   左上 (-1, 1) 远古残骸  右上 ( 1, 1) 下界合金块
            //   左下 (-1,-1) 强化深板岩  右下 ( 1,-1) 命令方块
            int[,] spots =
            {
                { -2, 0 }, { 0, 2 }, { 2, 0 },
                { -1, 1 }, { 1, 1 }, { -1, -1 }, { 1, -1 },
            };
            int[] ids = { obsidianId, cryingId, bedrockId, debrisId, netheriteId, deepslateId, commandId };
            for (int i = 0; i < ids.Length; i++)
            {
                if (ids[i] <= 0) continue;
                int x = cx + spots[i, 0];
                int y = cy + spots[i, 1];
                if (_loop.Grid.InBounds(x, y, cz))
                    _loop.Grid.Set(x, y, cz, ids[i]);
            }

            // 再摆一组：一颗 TNT 左边挨着岩浆。岩浆不写网格，得靠「流体淌动后再扫一遍」点火，
            // 这里把岩浆放好，下面 PrimeContact 就等价于岩浆漫过来那一刻
            int lavaTntX = cx - 4;
            if (_loop.Grid.InBounds(lavaTntX, cy, cz) && _loop.Grid.InBounds(lavaTntX - 1, cy, cz))
            {
                if (_loop.Grid.Get(lavaTntX, cy, cz) == 0)
                    _loop.Grid.Set(lavaTntX, cy, cz, tntId);
                if (_loop.Grid.Get(lavaTntX - 1, cy, cz) == 0)
                    Fluids.AddSource(_loop.Grid, lavaTntX - 1, cy, cz, Fluids.LavaId);
            }

            _view.RefreshAll();
            int fired = Tnt.PrimeContact(_loop.Grid);
            Debug.Log("[tnttest] 点着 " + fired + " 颗（右边那颗靠红石块，左边那颗靠岩浆），引信 " + Tnt.FuseSeconds + " 秒" +
                      "；靶子里左=黑曜石 上=哭泣黑曜石 右=基岩 左上=远古残骸 右上=下界合金块" +
                      " 左下=强化深板岩 右下=命令方块，炸完看这七个还在不在");
        }

        /// <summary>调试用：摆一块原木，结算一次长树叶，肉眼核对树叶贴图和生长位置。</summary>
        void RunLeafTest()
        {
            if (_loop == null || _view == null)
                return;

            int oakLog = -1;
            for (int i = 0; i < BlockCatalog.Blocks.Length; i++)
            {
                if (BlockCatalog.Blocks[i].Name == "oak_log")
                {
                    oakLog = i + 1;
                    break;
                }
            }
            if (oakLog <= 0)
            {
                Debug.Log("[leaftest] 方块表里找不到 oak_log");
                return;
            }

            int cx = _loop.Grid.SizeX / 2;
            int cy = Mathf.Min(6, Mathf.Max(2, _loop.Grid.SizeY - 6));
            int cz = _loop.Grid.SizeZ / 2;

            // 摆一块 I 型原木（4 格），落定后每格长一片，能看出叶子往哪几个方向长
            var piece = new Piece(PieceType.I, oakLog, cx, cy, cz);
            for (int i = 0; i < piece.Cells.Length; i++)
            {
                Vector3Int c = piece.WorldCell(i);
                if (_loop.Grid.InBounds(c.x, c.y, c.z))
                    _loop.Grid.Set(c.x, c.y, c.z, oakLog);
            }

            int grown = Foliage.Grow(_loop.Grid, piece);
            _view.RefreshAll();
            Debug.Log("[leaftest] 长叶 " + grown + " 片：" + Foliage.LastGrown.Count);
        }

        /// <summary>
        /// 调试用：底层铺满一整行，左列中间夹一块基岩，右列同样高度但没有基岩。
        /// 随便落一块方块触发消行后：左列整摞停在原处（基岩钉住，上面的被它托着），
        /// 右列整体下移一格 —— 两列一比就知道「基岩不下落」是什么效果。
        /// </summary>
        void RunBedrockTest()
        {
            if (_loop == null || _view == null)
                return;

            int bedrockId = BlockCatalog.BedrockId;
            if (bedrockId <= 0)
            {
                Debug.Log("[bedrocktest] 方块表里找不到基岩");
                return;
            }

            int stoneId = 1;
            for (int i = 0; i < BlockCatalog.Blocks.Length; i++)
            {
                if (BlockCatalog.Blocks[i].Name == "stone")
                {
                    stoneId = i + 1;
                    break;
                }
            }

            Grid grid = _loop.Grid;
            int cz = grid.SizeZ / 2;
            int lastX = grid.SizeX - 1;

            // 底层铺满：下一块落定时这一行会被消掉
            for (int x = 0; x < grid.SizeX; x++)
                grid.Set(x, 0, cz, stoneId);

            // 左列：基岩夹在中间，上面压两块石头
            grid.Set(0, 1, cz, bedrockId);
            grid.Set(0, 2, cz, stoneId);
            grid.Set(0, 3, cz, stoneId);

            // 右列：一样高，但没有基岩，纯对照
            grid.Set(lastX, 1, cz, stoneId);
            grid.Set(lastX, 2, cz, stoneId);
            grid.Set(lastX, 3, cz, stoneId);

            _view.RefreshAll();
            Debug.Log("[bedrocktest] 底层铺满 + 左列基岩（x=0）+ 右列对照（x=" + lastX +
                      "），落一块方块就会消掉底层，然后看两列的区别");
        }

        /// <summary>
        /// 调试用：摆一场看得清「整场记忆 + 从下往上一波扫回去」的场面。
        /// 地面铺几层交错材质，上方悬一格结构方块（四邻不同材质），场地另一头也摆一格；
        /// 记住整场之后把地面挖条沟、再插一根沙柱（模拟记忆之后场地被改成别的样子），
        /// 然后点着 TNT 把结构方块炸掉触发还原：沟被填上、沙柱从下往上被抹掉、材质变回来。
        /// 按 F5 可以再放一遍。
        /// </summary>
        void RunStructureTest()
        {
            if (_loop == null || _view == null)
                return;

            int structureId = BlockCatalog.StructureId;
            if (structureId <= 0)
            {
                Debug.Log("[structuretest] 方块表里找不到 structure_block");
                return;
            }

            // 挑几个颜色差别大的，扫到哪一层一眼能看出来
            int stoneId = 0, leavesId = 0, sandId = 0, dirtId = 0, logId = 0;
            for (int i = 0; i < BlockCatalog.Blocks.Length; i++)
            {
                string n = BlockCatalog.Blocks[i].Name;
                if (n == "stone") stoneId = i + 1;
                else if (n == "oak_leaves") leavesId = i + 1;
                else if (n == "sand") sandId = i + 1;
                else if (n == "dirt") dirtId = i + 1;
                else if (n == "oak_log") logId = i + 1;
            }

            Grid grid = _loop.Grid;
            int cx = grid.SizeX / 2;
            int cz = grid.SizeZ / 2;
            int lastX = grid.SizeX - 1;
            int lastY = grid.SizeY - 1;
            int lastZ = grid.SizeZ - 1;
            int rows = Mathf.Clamp(grid.SizeY / 4, 2, 5);

            // F5 重放时从零开始，别叠在上一轮的场面上
            grid.Clear();
            Fluids.Reset();
            Tnt.Reset();
            Structure.Reset();

            // 地面铺几层，材质交错
            int[] floorIds = { stoneId, dirtId, sandId, logId };
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < grid.SizeX; x++)
                    grid.Set(x, y, cz, floorIds[(x + y) % floorIds.Length]);
            }
            // 左边搭根柱子，恢复时高度方向的变化也看得见
            Put(grid, 0, rows, cz, logId);
            Put(grid, 0, rows + 1, cz, leavesId);

            // 结构方块悬在地面之上，四邻不同材质，都进快照
            int cy = rows + 3;
            Put(grid, cx, cy, cz, structureId);
            Put(grid, cx - 1, cy, cz, stoneId);
            Put(grid, cx + 1, cy, cz, leavesId);
            Put(grid, cx, cy - 1, cz, sandId);
            Put(grid, cx + 1, cy + 1, cz, dirtId);
            // 场地另一头各摆一格：记忆拍的是整场，不只是结构方块身边那一圈
            Put(grid, lastX, Mathf.Min(lastY, cy + 1), cz, leavesId);
            Put(grid, 0, Mathf.Min(lastY, cy + 2), lastZ, stoneId);

            Structure.Memory m = Structure.Remember(grid, cx, cy, cz);

            // TNT 和红石在记忆之后才放：它们不在快照里，炸完不会跟着一起还原回来
            Put(grid, cx, cy + 1, cz, Tnt.TntId);
            Put(grid, cx, cy + 2, cz, Tnt.RedstoneId);

            // 记忆拍完再动手：地面挖条沟，插一根沙柱，另一头再补一格。
            // 还原时这些都会被改回记忆里的样子，沙柱会从下往上一点点被抹掉。
            for (int x = cx - 2; x <= cx + 2; x++)
            {
                for (int y = 0; y < Mathf.Min(rows, 2); y++)
                {
                    if (grid.InBounds(x, y, cz))
                        grid.Set(x, y, cz, 0);
                }
            }
            int pillarX = Mathf.Max(0, cx - 4);
            for (int y = 0; y <= Mathf.Min(lastY, cy + 3); y++)
            {
                if (grid.InBounds(pillarX, y, cz))
                    grid.Set(pillarX, y, cz, sandId);
            }
            Put(grid, Mathf.Max(0, cx - 3), Mathf.Min(lastY, cy + 4), cz, sandId);

            _view.RefreshAll();
            int primed = Tnt.PrimeContact(grid);
            Debug.Log("[structuretest] 记忆整场 " + (m != null ? m.Blocks.Length : 0) +
                      " 格，点着 TNT " + primed + " 颗；引信烧完炸掉结构方块，然后从下往上一波扫回去。" +
                      " 沟会被填上、沙柱从下往上被抹掉、另一头补的那格也会消失。按 F5 再放一遍");
        }

        static void Put(Grid grid, int x, int y, int z, int id)
        {
            if (id > 0 && grid.InBounds(x, y, z))
                grid.Set(x, y, z, id);
        }

        void RunFluidTest(string which)
        {
            if (_loop == null || _view == null)
                return;
            int id = which == "lava" ? Fluids.LavaId : Fluids.WaterId;
            if (id <= 0)
            {
                Debug.Log("[fluidtest] 没找到 " + which);
                return;
            }
            int cx = _loop.Grid.SizeX / 2;
            int cz = _loop.Grid.SizeZ / 2;
            int top = _loop.Grid.SizeY - 5;
            _loop.Restart();                       // 先清场，免得旧存档把地方占满看不见水
            Fluids.Reset();
            Fluids.AddSource(_loop.Grid, cx, top, cz, id);
            _view.SnapPiece(_loop.Current);
            _view.RefreshAll();
            Debug.Log("[fluidtest] " + which + " id=" + id + " at " + cx + "," + top + "," + cz);
        }

        /// <summary>
        /// 调试用：摆一个 I 型，复制两份，一份不转、一份俯视转 90°，
        /// 两半都停在半空，跑起来看是不是「一块竖着插进场地、一块平贴着玩家」。
        /// 用 -auto -size 10 20 6 -yawtest -shotat 4 看截图。
        /// </summary>
        void RunYawTest()
        {
            if (_loop == null || _view == null)
                return;

            _loop.Restart();
            Fluids.Reset();
            Fluids.Prepare(_loop.Grid);

            // 别让它掉下去，停在半空看形状
            _loop.FallInterval = 1000f;

            int y = _loop.Grid.SizeY - 6;
            int zBack = Mathf.Max(0, _loop.Grid.SizeZ - 4);

            // 左边：不转，沿 x 展开、贴着最前面（直接写进网格，当作已落定）
            for (int i = 0; i < 4 && 1 + i < _loop.Grid.SizeX; i++)
                _loop.Grid.Set(1 + i, y, 0, 1);
            _view.RefreshAll();

            // 右边：同样一个 I 型，俯视转 90° → 应该变成沿 z 展开的竖板
            var turned = new Piece(Tetromino.GetShape(PieceType.I), "I", PieceType.I, 1, 6, y, zBack);
            _loop.Current = turned;
            bool ok = _loop.TryRotateYaw(true);
            Debug.Log("[yawtest] 场地=" + _loop.Grid.SizeX + "x" + _loop.Grid.SizeY + "x" + _loop.Grid.SizeZ
                + " 旋转成功=" + ok + " rot=" + turned.Rot
                + " origin=" + turned.OriginX + "," + turned.OriginY + "," + turned.OriginZ);
            _view.SnapPiece(_loop.Current);
            _view.RefreshAll();
        }

        /// <summary>
        /// 调试用：三个 T 型排一排——不转（立着）、往背面翻（躺平，沿 z 铺开两格）、往正面翻。
        /// 用 -auto -size 10 20 6 -pitchtest -shotat 4 看截图。
        /// </summary>
        void RunPitchTest()
        {
            if (_loop == null || _view == null)
                return;

            _loop.FallInterval = 1000f;
            int y = _loop.Grid.SizeY - 6;

            PlaceProbePiece(PieceType.T, 1, 1, 0);
            PlaceProbePiece(PieceType.T, 2, 4, 1);
            PlaceProbePiece(PieceType.T, 3, 7, -1);
            _view.RefreshAll();

            Debug.Log("[pitchtest] 场地=" + _loop.Grid.SizeX + "x" + _loop.Grid.SizeY + "x" + _loop.Grid.SizeZ
                + " y=" + y + "（x=1 不转；x=4 往背面翻；x=7 往正面翻）");
        }

        /// <summary>把一块指定朝向的方块直接写进网格，当作已落定（调试铺场景用）。dir：1 背面，-1 正面，0 不转。</summary>
        void PlaceProbePiece(PieceType type, int blockId, int x, int dir)
        {
            int y = _loop.Grid.SizeY - 6;
            int z = Mathf.Max(0, _loop.Grid.SizeZ - 4);
            var p = new Piece(type, blockId, x, y, z);
            if (dir > 0)
                p.Rot = p.Rot.Rotate(RotationAxis.Pitch, true);
            else if (dir < 0)
                p.Rot = p.Rot.Rotate(RotationAxis.Pitch, false);

            for (int i = 0; i < p.Cells.Length; i++)
            {
                Vector3Int c = p.WorldCell(i);
                if (_loop.Grid.InBounds(c.x, c.y, c.z))
                    _loop.Grid.Set(c.x, c.y, c.z, p.BlockId);
            }
        }

        /// <summary>
        /// 调试用：一次摆两组对照，跑起来看水岩浆怎么结晶。
        /// 左边是满格岩浆紧挨着一格水（该变黑曜石），右边是 3 份的流动岩浆上面压着水（该变原石）。
        /// </summary>
        void RunReactTest()
        {
            if (_loop == null || _view == null)
                return;
            if (Fluids.WaterId <= 0 || Fluids.LavaId <= 0)
            {
                Debug.Log("[reacttest] 没找到水或岩浆");
                return;
            }

            _loop.Restart();
            Fluids.Reset();
            Fluids.Prepare(_loop.Grid);

            int cx = _loop.Grid.SizeX / 2;
            int cz = _loop.Grid.SizeZ / 2;

            // 左：满格岩浆 + 紧邻的水
            Fluids.AddSource(_loop.Grid, cx - 2, 0, cz, Fluids.LavaId);
            Fluids.AddSource(_loop.Grid, cx - 1, 0, cz, Fluids.WaterId);

            // 右：流动岩浆（3 份）+ 上面压着的水（5 份）
            Fluids.AddSource(_loop.Grid, cx + 1, 0, cz, Fluids.LavaId);
            Fluids.SetAmount(cx + 1, 0, cz, 3, Fluids.LavaId);
            Fluids.SetAmount(cx + 1, 1, cz, 5, Fluids.WaterId);

            _view.SnapPiece(_loop.Current);
            _view.RefreshAll();
            Debug.Log("[reacttest] 左边满格岩浆+水（预期黑曜石），右边流动岩浆+水（预期原石）");
        }

        // ---- 调试面板用的小动作（F1 打开面板后点按钮） ----
        void RunBlockTest(string name)
        {
            int id = -1;
            for (int i = 0; i < BlockCatalog.Blocks.Length; i++)
            {
                if (BlockCatalog.Blocks[i].Name == name)
                {
                    id = i + 1;
                    break;
                }
            }
            if (id <= 0 || _loop == null || _view == null)
            {
                Debug.Log("[blocktest] 找不到方块 " + name);
                return;
            }

            int cx = _loop.Grid.SizeX / 2;
            int cz = _loop.Grid.SizeZ / 2;
            int top = _loop.Grid.SizeY - 6;
            _loop.Grid.Set(cx, top, cz, id);
            _loop.Grid.Set(cx + 1, top, cz, id);
            _loop.Grid.Set(cx, top - 1, cz, id);
            _view.RefreshAll();
            Debug.Log("[blocktest] " + name + " id=" + id);
        }

        void OnGravitySettled()
        {
            if (_view != null)
            {
                _view.ApplyGravityMoves(_loop.LastGravityMoves);
                _view.RefreshAll();
            }
            Debug.Log("[gravity] 落定后有方块下移");
        }

        void OnFluidChanged()
        {
            if (_view != null)
                _view.RefreshAll();
        }

        /// <summary>TNT 点着了：引信声 + 开始闪白（闪白本身每帧在 SyncTntFlash 里刷）。</summary>
        void OnTntPrimed(Vector3Int cell)
        {
            if (_sfx != null)
                _sfx.PlayFuse();
        }

        /// <summary>
        /// TNT 炸了：音效 + 震屏 + 撒碎块。
        /// 被炸掉的格子不用在这里 SetCell —— GameLoop 紧随其后会派 FluidChanged，
        /// 那一路是全场 RefreshAll，格子已经清空了。
        /// </summary>
        void OnTntExploded(Tnt.Blast blast)
        {
            if (_sfx != null)
                _sfx.PlayExplode();

            if (_rig != null)
                _rig.Shake(0.6f);

            if (_particles == null || blast.Cells == null)
                return;

            // 一次爆炸能掀掉几十格，每格都撒三片会把粒子池撑爆：按步长抽样，总量封顶约 24 格
            int step = Mathf.Max(1, blast.Cells.Count / 24);
            for (int i = 0; i < blast.Cells.Count; i += step)
            {
                Vector3Int c = blast.Cells[i];
                _particles.EmitCell(c.x, c.y, c.z, blast.Blocks[i], 2);
            }
        }

        /// <summary>
        /// 结构方块还原：消失阶段收掉一格。擦掉它，顺手撒一点碎块，看起来像被拆下来。
        /// 只动这一格，不需要整场重画。
        /// </summary>
        void OnStructureCellVanished(int x, int y, int z, int blockId)
        {
            if (_view == null)
                return;

            _view.SetCell(x, y, z, 0);
            if (_particles != null && blockId > 0)
                _particles.EmitCell(x, y, z, blockId, 1);
        }

        /// <summary>结构方块还原：重建阶段落下一格，从目标上方掉下来。</summary>
        void OnStructureCellDropped(int x, int y, int z, int blockId)
        {
            if (_view == null)
                return;
            _view.SetCellDropping(x, y, z, blockId, Structure.DropHeight);
        }

        /// <summary>
        /// 原木长出了树叶：把那几格画出来。
        /// 树叶是透明材质（走 BlockTransparentMat），SetCell 会照方块本身选材质，不用特殊处理。
        /// </summary>
        void OnFoliageGrew()
        {
            if (_view == null)
                return;

            for (int i = 0; i < Foliage.LastGrown.Count; i++)
            {
                Vector3Int c = Foliage.LastGrown[i];
                _view.SetCell(c.x, c.y, c.z, _loop.Grid.Get(c.x, c.y, c.z));
            }
        }

        /// <summary>
        /// 玻璃被压碎：脆响一声，格子清掉，撒几片碎块。
        /// 这个事件在重力结算**之前**发（每连碎一轮发一次），格位表里这格还挂着玻璃的
        /// 显示对象，SetCell(…,0) 正好把它收掉；等沙子搬进来再发就找不到玻璃了。
        /// </summary>
        void OnGlassCrushed()
        {
            if (_sfx != null)
                _sfx.PlayGlassBreak();

            if (_rig != null)
                _rig.Shake(0.08f);

            if (_view == null)
                return;

            for (int i = 0; i < _loop.LastGlassCrushed.Count; i++)
            {
                Vector3Int c = _loop.LastGlassCrushed[i];
                _view.SetCell(c.x, c.y, c.z, 0);
                if (_particles != null)
                    _particles.EmitCell(c.x, c.y, c.z, BlockCatalog.GlassId, 2);
            }
        }

        /// <summary>
        /// 雪块 / 冰被岩浆化掉了：嘶一声，格子清空，撒一把白色碎块。
        ///
        /// 这里用整场重画而不是逐格 SetCell：融化那一格同时清掉了固体、又新增了一格水，
        /// SetCell 只认网格里的方块 id，看不出「这格现在有水」；RefreshAll 会把流体层一起重画。
        /// 融化是坏事，但发生频率很低（要有岩浆又要有雪/冰），整场重画的开销可以接受。
        /// </summary>
        void OnSnowMelted()
        {
            if (_sfx != null)
                _sfx.PlayFizz();

            if (_view != null)
                _view.RefreshAll();

            if (_particles == null)
                return;

            // 融掉的是雪块还是冰已经查不出来了（格子已经清空），统一撒白色碎块 —— 两者都是白的。
            int id = BlockCatalog.SnowId > 0 ? BlockCatalog.SnowId : BlockCatalog.IceId;
            for (int i = 0; i < _loop.LastSnowMelted.Count; i++)
            {
                Vector3Int c = _loop.LastSnowMelted[i];
                _particles.EmitCell(c.x, c.y, c.z, id, 2);
            }
        }

        /// <summary>
        /// 有方块被点着了：嘶一声（MC 里点火就是这一下），撒几粒碎屑。
        /// 火苗由 SyncFire 每帧同步，这里只管声音。
        /// </summary>
        void OnFireIgnited()
        {
            if (_sfx != null)
                _sfx.PlayFizz();

            if (_particles == null)
                return;

            // 点着的是木头还是羊毛已经不重要了，统一撒橙黄色碎屑（火的颜色）
            for (int i = 0; i < Fire.LastIgnited.Count; i++)
            {
                Vector3Int c = Fire.LastIgnited[i];
                _particles.EmitCell(c.x, c.y, c.z, BlockCatalog.OakLogId > 0 ? BlockCatalog.OakLogId : 1, 1);
            }
        }

        /// <summary>
        /// 有方块烧光了：格子清空、撒一把碎块、嘶一声收尾。
        /// 壳交给下一帧的 SyncFire 摘（那一格的火已经被逻辑层摘掉了）。
        /// </summary>
        void OnBlocksBurned()
        {
            if (_view == null)
                return;

            for (int i = 0; i < _loop.LastBurnedOut.Count; i++)
            {
                Vector3Int c = _loop.LastBurnedOut[i];
                _view.SetCell(c.x, c.y, c.z, 0);

                int blockId = i < _loop.LastBurnedBlocks.Count ? _loop.LastBurnedBlocks[i] : 0;
                if (_particles != null && blockId > 0)
                    _particles.EmitCell(c.x, c.y, c.z, blockId, 3);
            }

            if (_sfx != null)
                _sfx.PlayFizz();
        }

        /// <summary>
        /// 海绵吸到水了：嘶一声，撒几粒水花，整场重画。
        /// 被吸干的水不占网格、海绵又换成了湿海绵，RefreshAll 一次把两边都收拾干净。
        /// </summary>
        void OnWaterSoaked()
        {
            if (_sfx != null)
                _sfx.PlayFizz();

            if (_view != null)
                _view.RefreshAll();

            if (_particles == null)
                return;

            for (int i = 0; i < _loop.LastSpongeSoaks.Count; i++)
            {
                Sponge.Soak soak = _loop.LastSpongeSoaks[i];
                for (int j = 0; j < soak.Cells.Count; j++)
                    _particles.EmitCell(soak.Cells[j].x, soak.Cells[j].y, soak.Cells[j].z,
                        Fluids.WaterId, 1);
            }
        }

        /// <summary>
        /// 召出傀儡了：清掉场地里那两格、撒碎块、播一下身体方块的放置音，
        /// 再在场外右侧摆一个小雕像。
        /// </summary>
        void OnGolemSummoned()
        {
            if (_view != null)
                _view.RefreshAll();   // 抽走两格之后上面可能还有塌下来的，整场刷一遍最省心

            ShowGolems(_loop.LastGolemsSummoned);
        }

        /// <summary>对着一批召唤记录播放效果，并在场外放出傀儡。事件和演示场景共用。</summary>
        void ShowGolems(System.Collections.Generic.List<Golem.Summon> summons)
        {
            if (summons == null || summons.Count == 0)
                return;

            int headId = BlockCatalog.CarvedPumpkinId > 0
                ? BlockCatalog.CarvedPumpkinId
                : BlockCatalog.PumpkinId;

            for (int i = 0; i < summons.Count; i++)
            {
                Golem.Summon sm = summons[i];

                if (_particles != null)
                {
                    if (headId > 0)
                        _particles.EmitCell(sm.X, sm.Y, sm.Z, headId, 2);
                    _particles.EmitCell(sm.X, sm.Y - 1, sm.Z, sm.BodyBlockId, 2);
                }
                if (_sfx != null && sm.BodyBlockId > 0)
                    _sfx.PlayPlace(BlockCatalog.Of(sm.BodyBlockId));
            }

            // 展示位的序号就是它在 Collection 里的下标：先算出这一批的起点
            int total = Golem.Collection.Count;
            int first = total - summons.Count;
            for (int i = 0; i < summons.Count; i++)
            {
                int slot = first + i;
                if (slot < 0 || slot >= Golem.MaxDisplay)
                    continue;   // 场地外站满了：傀儡照旧算数，只是不放出来
                Golem.Summon sm = Golem.Collection[slot];
                SpawnGolem(sm.Kind, sm.Oxidation);
            }
        }

        /// <summary>
        /// 在场外放一个真正的傀儡（实体模型 + 会自己走动），见 GolemPet。
        /// 位置在那片空地中间随便挑一处，活动范围由场地尺寸决定。
        /// </summary>
        void SpawnGolem(GolemKind kind, int oxidation)
        {
            var go = new GameObject("Golem_" + kind);
            go.transform.SetParent(transform, false);

            var pet = go.AddComponent<GolemPet>();
            Vector3 min = GolemPenMin();
            Vector3 max = GolemPenMax();
            // 出生点离已有傀儡越远越好：同时召出几个时不能叠成一团
            Vector3 pos = GolemPet.BestSpot(min, max, null);

            pet.Build(kind, oxidation, pos, Random.Range(0f, 360f), min, max);
            _golems.Add(pet);
        }

        /// <summary>
        /// 傀儡活动区：场地右侧那片空地。为什么不在场地里：场地里要占格子、要参与消行、
        /// 还会挡住落下来的方块，一个彩蛋不值得改消行逻辑。
        /// </summary>
        Vector3 GolemPenMin()
        {
            return new Vector3(SizeX + 1.2f, 0f, -1f);
        }

        Vector3 GolemPenMax()
        {
            return new Vector3(SizeX + 7.5f, 0f, SizeZ + 0.5f);
        }

        /// <summary>把场外的傀儡全清掉（新开一局 / 改场地尺寸时）。</summary>
        void ClearGolemModels()
        {
            for (int i = 0; i < _golems.Count; i++)
            {
                if (_golems[i] != null)
                    Destroy(_golems[i].gameObject);
            }
            _golems.Clear();
        }

        /// <summary>
        /// 把「哪些格子正在烧引信」同步给视图层，顺手让它闪白。
        ///
        /// 频率照抄原版：MC 的 TntRenderer 是用引信 tick 数算的（`fuse / 5 % 2`），
        /// 每 5 tick 换一次贴图 —— 0.25 秒白、0.25 秒原色，也就是 2 Hz 方波。
        /// 相位按「已经烧了多久」算而不是跟全局时钟走：每颗 TNT 从点着那一刻开始白，
        /// 连锁点着的几颗各自错开，不会全场同时闪成一片。
        /// </summary>
        void SyncTntFlash()
        {
            if (_view == null)
                return;

            System.Collections.Generic.List<Tnt.Primed> primed = Tnt.PrimedCells;
            for (int i = 0; i < primed.Count; i++)
            {
                Tnt.Primed p = primed[i];
                _view.SetCellFlash(p.X, p.Y, p.Z, Tnt.FlashOn(p.Total - p.Fuse));
            }
        }

        /// <summary>
        /// 把「哪些格子正在烧」同步给视图层，让那些格子长出原版火贴图的火苗。
        ///
        /// 每帧都重算而不是只在点燃时设一次：方块可能被消行带走、被水浇灭、被重力挪走，
        /// 火得跟着一起没 —— 逻辑层那边已经把失效的火摘掉了，这里照着抄一遍就行。
        /// </summary>
        void SyncFire()
        {
            if (_view == null)
                return;

            System.Collections.Generic.List<Fire.Burning> burning = Fire.BurningCells;

            // 上一帧在烧、这一帧不在了的：把壳摘掉
            for (int i = 0; i < _lastBurning.Count; i++)
            {
                Vector3Int c = _lastBurning[i];
                bool still = false;
                for (int j = 0; j < burning.Count; j++)
                {
                    if (burning[j].X == c.x && burning[j].Y == c.y && burning[j].Z == c.z)
                    {
                        still = true;
                        break;
                    }
                }
                if (!still)
                    _view.SetCellBurning(c.x, c.y, c.z, false);
            }

            _lastBurning.Clear();
            for (int i = 0; i < burning.Count; i++)
            {
                Fire.Burning b = burning[i];
                _view.SetCellBurning(b.X, b.Y, b.Z, true);
                _lastBurning.Add(new Vector3Int(b.X, b.Y, b.Z));
            }
        }

        /// <summary>上一帧哪些格子在烧（SyncFire 用来找出「火灭了」的那些）。</summary>
        readonly System.Collections.Generic.List<Vector3Int> _lastBurning
            = new System.Collections.Generic.List<Vector3Int>();

        /// <summary>
        /// 落点提示：把「这一块直接落下去会停在哪」用高亮方框标出来。
        /// 每帧重算一次就够了 —— 方块横移、转朝向、底下堆的方块变了，框都得跟着变。
        /// 落点是 GameLoop.GhostOrigin 算的，和硬降同一套判定，框指哪儿按空格就落哪儿。
        /// </summary>
        void SyncGhost()
        {
            if (_view == null)
                return;

            if (!GameSettings.GhostPiece || _loop == null || _loop.Current == null || _loop.GameOver)
            {
                _view.HideGhost();
                return;
            }

            Vector3Int o = _loop.GhostOrigin();
            _view.ShowGhost(_loop.Current, o.x, o.y, o.z);
        }

        /// <summary>
        /// 水岩浆结晶：把那几格换成黑曜石 / 原石，冒一点碎屑，播 MC 的嘶声（random.fizz）。
        /// </summary>
        void OnFluidReacted(System.Collections.Generic.List<Fluids.Reaction> reactions)
        {
            if (reactions == null || reactions.Count == 0)
                return;

            for (int i = 0; i < reactions.Count; i++)
            {
                Fluids.Reaction r = reactions[i];
                if (_view != null)
                    _view.SetCell(r.X, r.Y, r.Z, r.BlockId);
            }

            if (_sfx != null)
                _sfx.PlayFizz();

            if (_particles != null)
            {
                for (int i = 0; i < reactions.Count; i++)
                {
                    Fluids.Reaction r = reactions[i];
                    _particles.EmitCell(r.X, r.Y, r.Z, r.BlockId, 2);
                }
            }
        }

        void OnPieceLocked(Piece piece)
        {
            _sfx.PlayPlace(BlockCatalog.Of(piece.BlockId));
            // 保住朝向：旋转过的方块，落定那一瞬间如果朝向归零，看起来就是“啪”地转了一下。
            // 朝向直接从方块身上取（逻辑层的整数矩阵转四元数），不读视图层的动画状态——
            // 同一帧里"转完立刻硬降"也能拿到正确角度。
            Quaternion rot = piece.Rot.ToQuaternion();
            for (int i = 0; i < piece.Cells.Length; i++)
            {
                Vector3Int c = piece.WorldCell(i);
                // 按网格里实际是什么来画，而不是按方块自己的编号：
                // 水方块落进岩浆那格会当场结晶成黑曜石/原石，网格里已经不是水了。
                int cellId = _loop.Grid.InBounds(c.x, c.y, c.z)
                    ? _loop.Grid.Get(c.x, c.y, c.z)
                    : piece.BlockId;
                _view.SetCell(c.x, c.y, c.z, cellId);
                _view.SetCellRotation(c.x, c.y, c.z, rot);
            }
        }

        void OnLinesCleared(int lines)
        {
            _sfx.PlayClear(lines);
            // 视觉上的下移交给 OnRowsCleared（它拿得到具体清了哪几行），
            // 这里只放音效，不要再 RefreshAll 一次，否则会对同一批格子做两遍。
        }

        void OnSpawned()
        {
            _view.SnapPiece(_loop.Current);
            _nextPreview.Show(_loop.Next, _loop.NextBlockId);
            if (!_loop.GameOver)
                GameSave.Save(_loop);
        }

        void OnPieceRotated(RotationAxis axis, bool clockwise)
        {
            if (_view != null)
                _view.RotatePieceVisual(axis, clockwise);
        }

        void OnRowsCleared(System.Collections.Generic.List<int> rows, System.Collections.Generic.List<int> blocks)
        {
            // 先给整堆一个"从上面滑下来"的视觉起点，再按新内容刷新贴图
            if (_view != null)
            {
                _view.BeginCollapse(rows);
                _view.RefreshAll();
            }

            for (int i = 0; i < rows.Count; i++)
            {
                int blockId = blocks[i];
                for (int x = 0; x < SizeX; x++)
                    for (int z = 0; z < SizeZ; z++)
                        _particles.EmitCell(x, rows[i], z, blockId);
            }
        }

        void SetupScene()
        {
            // 边框挂在独立子对象上：GridFrame.Build 会清空自己的子节点，
            // 如果和 GridView 挂在同一个 GameObject 上，会把格子和当前方块一起删掉
            GameObject frameGo = new GameObject("GridFrame");
            frameGo.transform.SetParent(transform, false);
            GridFrame frame = frameGo.AddComponent<GridFrame>();
            frame.Build(SizeX, SizeY, SizeZ,
                new Color(0.42f, 0.86f, 1f),                 // 核心线：海天蓝
                new Color(0.10f, 0.55f, 1f, 0.34f));         // 光晕：更深的蓝、半透明

            Vector3 center = new Vector3((SizeX - 1) / 2f, (SizeY - 1) / 2f, (SizeZ - 1) / 2f);

            Camera cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera");
                camGo.tag = "MainCamera";
                cam = camGo.AddComponent<Camera>();
            }

            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.08f, 0.09f, 0.12f);
            // 预览层只给「下一个」相机渲染，主相机不画；建造台同理，它有自己的相机
            cam.cullingMask &= ~(1 << NextPreview.PreviewLayer);
            cam.cullingMask &= ~(1 << MoldWorkbench.WorkLayer);

            CameraRig rig = cam.GetComponent<CameraRig>();
            if (rig == null)
                rig = cam.gameObject.AddComponent<CameraRig>();

            rig.Target = center;
            // 场地变深以后，相机要退得够远才看得全
            rig.Distance = Mathf.Max(SizeY * 1.05f, SizeZ * 1.8f);

            _mainCam = cam;
            _rig = rig;
            _normalCullMask = cam.cullingMask;
        }
    }
}
