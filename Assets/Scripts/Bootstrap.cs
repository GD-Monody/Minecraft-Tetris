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
        bool _autoShotDone;
        bool _demoMode;
        bool _demoBurst;
        bool _titleMode;
        int _titleShotIndex;
        static readonly float[] TitleShotTimes = { 0.4f, 1.3f, 2.5f, 3.4f, 4.2f };
        Camera _mainCam;
        int _normalCullMask;

        void Start()
        {
            SizeX = GameSettings.SizeX;
            SizeY = GameSettings.SizeY;
            SizeZ = GameSettings.SizeZ;
            ApplyDebugSizeArgs();

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
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-pause") >= 0)
            {
                _title.Visible = false;
                _paused = true;
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

            _view.RefreshAll();
            _view.RefreshPiece(_loop.Current);
            _nextPreview.Show(_loop.Next, _loop.NextBlockId);

            SetupScene();
        }

        void Update()
        {
            if (_loop == null)
                return;

            if (Input.GetKeyDown(KeyCode.F12))
                CaptureShot("manual_shot.png");

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
            _loop.Restart();
            _particles.Clear();
            _paused = false;
            _view.RefreshAll();
            _view.RefreshPiece(_loop.Current);
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
            _paused = false;
            _view.RefreshAll();
            _view.RefreshPiece(_loop.Current);
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
            _view.RefreshAll();
        }

        void OnSpawned()
        {
            _view.RefreshPiece(_loop.Current);
            _nextPreview.Show(_loop.Next, _loop.NextBlockId);
            if (!_loop.GameOver)
                GameSave.Save(_loop);
        }

        void OnRowsCleared(System.Collections.Generic.List<int> rows, System.Collections.Generic.List<int> blocks)
        {
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
            GridFrame frame = gameObject.GetComponent<GridFrame>();
            if (frame == null)
                frame = gameObject.AddComponent<GridFrame>();
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
            _normalCullMask = cam.cullingMask;
        }
    }
}
