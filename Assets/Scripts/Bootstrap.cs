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
        TitleScreen _title;
        NextPreview _nextPreview;
        ParticleBurst _particles;
        bool _paused;
        float _autoShotTimer = 6f;
        float _forceGameOverAt = -1f;
        bool _autoShotDone;
        bool _demoMode;
        bool _demoBurst;
        bool _titleMode;
        int _titleShotIndex;
        static readonly float[] TitleShotTimes = { 0.4f, 1.3f, 2.5f, 3.4f, 4.2f };
        Camera _mainCam;
        CameraRig _rig;
        bool _camLogged;
        int _normalCullMask;

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
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-workshop") >= 0)
            {
                _title.Visible = true;
                _title.OpenSettingsPage(3);
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
                "-sandtest", "-reacttest", "-tnttest",
                "-blocktest", "-fluidtest", "-yawtest", "-pitchtest",
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

            _view.RefreshAll();
            _view.SnapPiece(_loop.Current);
            _nextPreview.Show(_loop.Next, _loop.NextBlockId);

            SetupScene();
        }

        void Update()
        {
            if (_loop == null)
                return;

            if (Input.GetKeyDown(KeyCode.F12))
                CaptureShot("manual_shot.png");

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

            if (_title != null && _title.Visible)
            {
                _title.HasContinue = GameSave.HasSave;
                if (_hud != null)
                    _hud.Hidden = true;
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
            if (_hud != null)
                _hud.Hidden = false;
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
                if (Input.GetKeyDown(KeyCode.R))
                    StartNewGame();
                return;
            }

            if (_paused)
                return;

            _loop.Tick(Time.deltaTime, _input.Read());
            _view.RefreshPiece(_loop.Current);
            SyncTntFlash();

            // 调试用：-demo 启动时在自动截图前炸一把粒子，方便截图看效果
            if (_demoMode && !_demoBurst && Time.time > 5.2f)
            {
                _demoBurst = true;
                var rows = new System.Collections.Generic.List<int> { 2, 3, 4 };
                var blocks = new System.Collections.Generic.List<int> { 12, 150, 260 };
                OnRowsCleared(rows, blocks);
            }
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
            _loop.FallSpeed = GameSettings.FallSpeed;
            _particles.Clear();
            _paused = false;
            _view.RefreshAll();
            _view.ClearFlash();
            _view.SnapPiece(_loop.Current);
            _nextPreview.Show(_loop.Next, _loop.NextBlockId);
            if (_title != null)
                _title.Show(false);
            if (_hud != null)
                _hud.Hidden = false;
            GameSave.Save(_loop);
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
            if (_hud != null)
                _hud.Hidden = false;
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
            for (int i = 0; i < BlockCatalog.Blocks.Length; i++)
            {
                if (BlockCatalog.Blocks[i].Name == "stone")
                {
                    stoneId = i + 1;
                    break;
                }
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

            _view.RefreshAll();
            int fired = Tnt.PrimeContact(_loop.Grid);
            Debug.Log("[tnttest] 点着 " + fired + " 颗，引信 " + Tnt.FuseSeconds + " 秒");
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

        /// <summary>把「哪些格子正在烧引信」同步给视图层，顺手让它闪白（6 Hz 方波）。</summary>
        void SyncTntFlash()
        {
            if (_view == null)
                return;

            bool on = Mathf.Repeat(Time.unscaledTime * 6f, 1f) < 0.5f;
            System.Collections.Generic.List<Tnt.Primed> primed = Tnt.PrimedCells;
            for (int i = 0; i < primed.Count; i++)
                _view.SetCellFlash(primed[i].X, primed[i].Y, primed[i].Z, on);
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
            // 预览层只给「下一个」相机渲染，主相机不画
            cam.cullingMask &= ~(1 << NextPreview.PreviewLayer);

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
