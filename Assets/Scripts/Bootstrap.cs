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

        void Start()
        {
            _loop = new GameLoop(SizeX, SizeY, SizeZ, (int)System.DateTime.Now.Ticks);

            _view = gameObject.AddComponent<GridView>();
            _view.Setup(_loop.Grid);

            _input = gameObject.AddComponent<KeyboardInput>();
            _sfx = gameObject.AddComponent<SfxPlayer>();

            _hud = gameObject.AddComponent<Hud>();
            _hud.Loop = _loop;
            _hud.OnRestartClick = RestartGame;

            _nextPreview = gameObject.AddComponent<NextPreview>();
            _nextPreview.Init(_view);
            _hud.Preview = _nextPreview;

            _particles = gameObject.AddComponent<ParticleBurst>();
            _particles.Init(_view);

            _title = gameObject.AddComponent<TitleScreen>();

            // 命令行带 -auto 时跳过开始界面，方便自动化截图与调试
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-auto") >= 0)
                _title.Visible = false;
            _demoMode = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-demo") >= 0;

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
                if (_title.AnyInputPressed())
                {
                    _title.Visible = false;
                }
                else
                {
                    if (_hud != null)
                        _hud.Hidden = true;
                    return;
                }
            }
            if (_hud != null)
                _hud.Hidden = false;

            if (Input.GetKeyDown(KeyCode.Escape))
                _paused = !_paused;

            if (_hud != null)
                _hud.Paused = _paused;

            if (_loop.GameOver)
            {
                if (Input.GetKeyDown(KeyCode.R))
                    RestartGame();
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
            _loop.Restart();
            _particles.Clear();
            _view.RefreshAll();
            _view.RefreshPiece(_loop.Current);
            _nextPreview.Show(_loop.Next, _loop.NextBlockId);
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
            BorderRenderer border = gameObject.GetComponent<BorderRenderer>();
            if (border == null)
                border = gameObject.AddComponent<BorderRenderer>();
            border.Build(SizeX, SizeY, SizeZ);

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
            rig.Distance = SizeY * 1.05f;
        }
    }
}
