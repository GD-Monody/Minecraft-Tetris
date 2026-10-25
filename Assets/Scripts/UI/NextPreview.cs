using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>
    /// 「下一个」预览：一台独立的正交相机把下一个方块渲染进 RenderTexture，
    /// HUD 再把这张图贴到面板里。预览物体放在 PreviewLayer（第 8 层），
    /// 主相机不渲染这一层，所以不会干扰场地。
    /// </summary>
    public class NextPreview : MonoBehaviour
    {
        public const int PreviewLayer = 8;

        Camera _cam;
        RenderTexture _rt;
        Transform _root;
        GridView _view;
        readonly MeshFilter[] _filters = new MeshFilter[4];
        readonly MeshRenderer[] _renderers = new MeshRenderer[4];

        public Texture Texture
        {
            get { return _rt; }
        }

        public void Init(GridView view)
        {
            _view = view;

            _rt = new RenderTexture(224, 224, 16);
            _rt.filterMode = FilterMode.Point;
            _rt.Create();

            var camGo = new GameObject("NextPreviewCamera");
            camGo.transform.SetParent(transform, false);
            camGo.transform.localPosition = new Vector3(0f, 0f, -10f);
            _cam = camGo.AddComponent<Camera>();
            _cam.orthographic = true;
            _cam.orthographicSize = 2.3f;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _cam.targetTexture = _rt;
            _cam.cullingMask = 1 << PreviewLayer;
            _cam.depth = -10f;
            _cam.nearClipPlane = 0.1f;
            _cam.farClipPlane = 50f;

            var rootGo = new GameObject("NextPreviewRoot");
            rootGo.transform.SetParent(camGo.transform, false);
            rootGo.layer = PreviewLayer;
            _root = rootGo.transform;
            _root.localPosition = new Vector3(0f, 0f, 10f);
            _root.localRotation = Quaternion.Euler(20f, 34f, 0f);

            for (int i = 0; i < 4; i++)
            {
                var go = new GameObject("NextCell" + i);
                go.transform.SetParent(_root, false);
                go.layer = PreviewLayer;
                _filters[i] = go.AddComponent<MeshFilter>();
                _renderers[i] = go.AddComponent<MeshRenderer>();
            }
        }

        /// <summary>换成下一个方块：形状用 Type，材质用 BlockId。</summary>
        public void Show(PieceType type, int blockId)
        {
            if (_view == null)
                return;

            Vector2Int[] shape = Tetromino.GetShape(type);
            int minX = int.MaxValue, maxX = int.MinValue;
            int minY = int.MaxValue, maxY = int.MinValue;
            for (int i = 0; i < shape.Length; i++)
            {
                minX = Mathf.Min(minX, shape[i].x);
                maxX = Mathf.Max(maxX, shape[i].x);
                minY = Mathf.Min(minY, shape[i].y);
                maxY = Mathf.Max(maxY, shape[i].y);
            }
            float cx = (minX + maxX) * 0.5f;
            float cy = (minY + maxY) * 0.5f;

            Mesh mesh = _view.GetMesh(blockId);
            Material mat = _view.GetMaterial(blockId);

            for (int i = 0; i < 4; i++)
            {
                GameObject go = _filters[i].gameObject;
                go.SetActive(true);
                go.transform.localPosition = new Vector3(shape[i].x - cx, shape[i].y - cy, 0f);
                _filters[i].sharedMesh = mesh;
                _renderers[i].sharedMaterial = mat;
            }
        }

        void OnDestroy()
        {
            if (_rt != null)
            {
                _rt.Release();
                Destroy(_rt);
            }
        }
    }
}
