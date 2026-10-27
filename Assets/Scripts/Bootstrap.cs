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

            _view = gameObject.AddComponent<GridView>();
            _view.Setup(_loop.Grid);

            _input = gameObject.AddComponent<KeyboardInput>();
            _sfx = gameObject.AddComponent<SfxPlayer>();

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

            _title = gameObject.AddComponent<TitleScreen>();
            _title.HasContinue = GameSave.HasSave;
            _title.OnStartGame += StartNewGame;
            _title.OnContinue += ContinueGame;
            _title.OnQuit += QuitGame;
            _title.OnSfxChanged += OnSfxChanged;
            _title.OnKeysChanged += OnKeysChanged;
            _title.OnSizeApplied += OnSizeApplied;
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

            // -blocktest <方块英文名>：在场地高处摆三个该方块，用来肉眼核对六面贴图
            var btArgv = System.Environment.GetCommandLineArgs();
            int btAt = System.Array.IndexOf(btArgv, "-blocktest");
            if (btAt >= 0 && btAt + 1 < btArgv.Length)
                RunBlockTest(btArgv[btAt + 1]);

            // -fluidtest water|lava：在半空放一汪水/岩浆，看它流下来再漫开
            int ftAt = System.Array.IndexOf(btArgv, "-fluidtest");
            if (ftAt >= 0 && ftAt + 1 < btArgv.Length)
                RunFluidTest(btArgv[ftAt + 1]);

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
                if (_title.AnyInputPressed())
                    StartNewGame();
                return;
            }
            if (_hud != null)
                _hud.Hidden = false;
            if (_mainCam != null && _mainCam.cullingMask != _normalCullMask)
                _mainCam.cullingMask = _normalCullMask;

            if (Input.GetKeyDown(KeyCode.Escape))
                _paused = !_paused;

            if (_hud != null)
                _hud.Paused = _paused;

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
            _loop.Restart();
            _particles.Clear();
            _paused = false;
            _view.RefreshAll();
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
            Fluids.Reset();
            if (!GameSave.TryLoad(_loop))
            {
                StartNewGame();
                return;
            }

            _particles.Clear();
            _paused = false;
            _view.RefreshAll();
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

        void OnPieceLocked(Piece piece)
        {
            _sfx.PlayPlace(BlockCatalog.Of(piece.BlockId));
            for (int i = 0; i < piece.Cells.Length; i++)
            {
                Vector3Int c = piece.WorldCell(i);
                _view.SetCell(c.x, c.y, c.z, piece.BlockId);
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

        void OnPieceRotated(bool clockwise)
        {
            if (_view != null)
                _view.RotatePieceVisual(clockwise);
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
