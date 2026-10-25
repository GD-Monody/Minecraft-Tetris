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
        bool _paused;
        float _autoShotTimer = 6f;
        bool _autoShotDone;

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

            _title = gameObject.AddComponent<TitleScreen>();

            _loop.PieceLocked += OnPieceLocked;
            _loop.LinesClearedEvent += OnLinesCleared;
            _loop.Spawned += OnSpawned;

            _view.RefreshAll();
            _view.RefreshPiece(_loop.Current);

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
            _view.RefreshAll();
            _view.RefreshPiece(_loop.Current);
        }

        void OnPieceLocked(Piece piece)
        {
            _sfx.PlayPlace(BlockCatalog.For(piece.Type));
            for (int i = 0; i < piece.Cells.Length; i++)
            {
                Vector3Int c = piece.WorldCell(i);
                _view.SetCell(c.x, c.y, c.z, (int)piece.Type + 1);
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

            CameraRig rig = cam.GetComponent<CameraRig>();
            if (rig == null)
                rig = cam.gameObject.AddComponent<CameraRig>();

            rig.Target = center;
            rig.Distance = SizeY * 1.05f;
        }
    }
}
