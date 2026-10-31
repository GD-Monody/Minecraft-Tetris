using System;
using System.Collections.Generic;
using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>
    /// 创意工坊的三维建造台：一台专用相机把 5×5×5 的建造盒渲染进 RenderTexture，
    /// 标题界面把这张图当一扇「窗口」贴在面板里，鼠标在窗口里的位置换算成一条射线。
    ///
    /// 为什么不能像二维那样「点一格亮一格」：屏幕上的一个点，在三维里对应一整条射线，
    /// 深度是未知的。这里的解法是取射线最先撞到的实心格，再看撞的是哪个面 ——
    /// 新方块长在那个面朝外的那一格，跟 Minecraft 往方块面上放东西一个手感。
    /// 一次点击因此只解出一个确定的格子，不用玩家自己判断深度。
    ///
    /// 整个建造盒放在世界底下 500 格处，用第 9 层渲染：建造台相机只画这一层，
    /// 主相机把它剔除。两边各画各的，互不干扰。
    /// </summary>
    public class MoldWorkbench : MonoBehaviour
    {
        /// <summary>建造台专用的渲染层。主相机在 SetupScene 里把这一层剔掉。</summary>
        public const int WorkLayer = 9;

        /// <summary>建造盒沉到世界底下多远。离主场地越远越不会互相挡。</summary>
        const float WorldY = -500f;

        const int Size = MoldData.Size;
        const int MaxCells = MoldData.MaxCells;

        /// <summary>正交相机离目标的距离。正交投影下拉远拉近不改变大小，只为了避开近远裁剪面。</summary>
        const float CamDist = 30f;
        const float ZoomMin = 3.4f;
        const float ZoomMax = 8.5f;
        const float ZoomDefault = 5.2f;
        const float OrbitDegPerPixel = 0.6f;

        /// <summary>编辑器里模具长什么样（游戏里它跟普通方块一样随机取材质）。</summary>
        const string EditorBlock = "diamond_block";

        /// <summary>提示行显示多久。</summary>
        const float HintSeconds = 1.8f;

        /// <summary>格子动过了：标题界面据此存盘。</summary>
        public event Action OnChanged;

        readonly List<Vector3Int> _cells = new List<Vector3Int>();
        readonly HashSet<int> _keys = new HashSet<int>();
        Func<Vector3Int, bool> _isSolid;

        Camera _cam;
        RenderTexture _rt;
        Transform _root;
        readonly List<Transform> _cellTs = new List<Transform>();
        readonly List<MeshRenderer> _cellRends = new List<MeshRenderer>();
        Transform _ghost;
        Transform _faceHi;

        Mesh _cube;
        Mesh _wire;
        Mesh _quad;
        Material _blockMat;
        Material _dimMat;
        Material _coreMat;
        Material _glowMat;
        Material _ghostMat;
        Material _hiMat;

        float _yaw = -38f;
        float _pitch = 22f;
        float _zoom = ZoomDefault;
        int _layer;
        bool _dimOthers = true;

        // 右键：按下时记位置，抬手时看总共挪了多少 —— 没怎么挪就是「擦除」，挪多了就是「转视角」
        bool _rbDown;
        Vector2 _rbStart;
        float _rbMoved;

        bool _hasHit;
        VoxelHit _hit;
        bool _hasTarget;
        Vector3Int _target;

        string _hint;
        float _hintEnd;

        // ---------- 对外接口 ----------

        public IReadOnlyList<Vector3Int> Cells
        {
            get { return _cells; }
        }

        /// <summary>建造台开着没有。关着的时候相机不渲染，省一次 RT 绘制。</summary>
        public bool Active
        {
            set { if (_cam != null) _cam.enabled = value; }
        }

        /// <summary>当前层（z）。只影响显示：开「只亮当前层」时别的层画成半透明。</summary>
        public int Layer
        {
            get { return _layer; }
            set
            {
                int v = Mathf.Clamp(value, 0, Size - 1);
                if (v == _layer)
                    return;
                _layer = v;
                RefreshCells();
            }
        }

        public bool DimOthers
        {
            get { return _dimOthers; }
            set
            {
                if (value == _dimOthers)
                    return;
                _dimOthers = value;
                RefreshCells();
            }
        }

        /// <summary>一行提示（放不下、这格已经有了之类），到点自动清空。</summary>
        public string Hint
        {
            get { return Time.realtimeSinceStartup < _hintEnd ? _hint : null; }
        }

        /// <summary>最近一次点击会落在哪一格（画在界面上当坐标读数）。没有就返回 false。</summary>
        public bool TargetCell(out Vector3Int cell)
        {
            cell = _target;
            return _hasTarget;
        }

        public void Init()
        {
            _isSolid = IsSolid;

            int blockId = BlockCatalog.IdOf(EditorBlock);
            if (blockId <= 0)
                blockId = 1;
            BlockDef def = BlockCatalog.Of(blockId);
            _cube = BlockMesh.Build(def);
            _blockMat = BlockMesh.GetMaterial(def);

            _wire = BuildWireCube(0.52f, 0.028f);
            _quad = BuildQuad();

            _dimMat = MakeGhostMaterial(new Color(0.62f, 0.80f, 1f, 0.30f));
            _ghostMat = CloneGlow(new Color(0.35f, 0.92f, 1f, 0.55f));
            _hiMat = CloneGlow(new Color(1f, 1f, 1f, 0.34f));
            // 工程里的 GridCoreMat / GridGlowMat 是共享资源，GridFrame 每次 Build 会改它们的
            // 颜色。这里克隆一份再上色，省得两个边框互相抢颜色。
            _coreMat = CloneMat("GridCoreMat", new Color(0.42f, 0.86f, 1f, 1f));
            _glowMat = CloneMat("GridGlowMat", new Color(0.10f, 0.55f, 1f, 0.30f));

            _rt = new RenderTexture(512, 512, 16);
            _rt.antiAliasing = 4;
            _rt.filterMode = FilterMode.Bilinear;
            _rt.Create();

            var camGo = new GameObject("MoldWorkbenchCamera");
            camGo.transform.SetParent(transform, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.orthographic = true;
            _cam.orthographicSize = _zoom;
            _cam.nearClipPlane = 1f;
            _cam.farClipPlane = CamDist * 2f;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            // 比面板再暗一档，看上去像一扇通往建造盒的窗口
            _cam.backgroundColor = new Color(0.05f, 0.06f, 0.09f, 1f);
            _cam.targetTexture = _rt;
            _cam.cullingMask = 1 << WorkLayer;
            _cam.depth = -10f;
            _cam.enabled = false;

            _root = new GameObject("MoldBox").transform;
            _root.SetParent(transform, false);
            _root.position = new Vector3(0f, WorldY, 0f);
            _root.gameObject.layer = WorkLayer;

            BuildFrame();

            _ghost = NewChild("Ghost", _wire, _ghostMat);
            _ghost.gameObject.SetActive(false);
            _faceHi = NewChild("FaceHi", _quad, _hiMat);
            _faceHi.gameObject.SetActive(false);
        }

        public void SetCells(IList<Vector3Int> cells)
        {
            _cells.Clear();
            _keys.Clear();
            if (cells != null)
            {
                for (int i = 0; i < cells.Count; i++)
                    Add(cells[i]);
            }
            // 载入时把视角带到形状所在的那层，不然可能一进来整块都是半透明的
            if (_cells.Count > 0)
                _layer = Mathf.Clamp(_cells[0].z, 0, Size - 1);
            RefreshCells();
        }

        public void Clear()
        {
            if (_cells.Count == 0)
                return;
            _cells.Clear();
            _keys.Clear();
            RefreshCells();
            Notify();
        }

        /// <summary>左右镜像（x → Size-1-x）。做对称形状时省一半工夫。</summary>
        public void Mirror()
        {
            Vector3Int[] next = Mirrored(_cells, 0);
            _cells.Clear();
            _keys.Clear();
            for (int i = 0; i < next.Length; i++)
                Add(next[i]);
            RefreshCells();
            Notify();
        }

        /// <summary>把整块挪到建造盒正中。形状歪在一边时看着累，也容易贴到盒壁上。</summary>
        public void Recenter()
        {
            Vector3Int[] next = Centered(_cells);
            _cells.Clear();
            _keys.Clear();
            for (int i = 0; i < next.Length; i++)
                Add(next[i]);
            RefreshCells();
            Notify();
        }

        /// <summary>视角复位。</summary>
        public void ResetView()
        {
            _yaw = -38f;
            _pitch = 22f;
            _zoom = ZoomDefault;
        }

        // ---------- 每帧 ----------

        void Update()
        {
            if (_cam == null || _root == null)
                return;

            // 轨道相机：位置从目标反推，朝向就是「从相机指向目标」，不用再 LookAt
            Vector3 pivot = _root.position + new Vector3(Size, Size, Size) * 0.5f;
            Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
            _cam.transform.position = pivot + rot * new Vector3(0f, 0f, -CamDist);
            _cam.transform.rotation = rot;
            _cam.orthographicSize = _zoom;
        }

        /// <summary>
        /// 把建造盒画进面板上的这块矩形，并处理这个矩形里的鼠标操作。
        /// 必须在 OnGUI 里调用（要用 Event.current）。
        /// </summary>
        public void Draw(Rect rect)
        {
            if (_rt != null)
                GUI.DrawTexture(rect, _rt, ScaleMode.StretchToFill, true);

            Event e = Event.current;
            if (e == null)
                return;

            bool inside = rect.Contains(e.mousePosition);

            if (inside && e.type == EventType.ScrollWheel)
            {
                _zoom = Mathf.Clamp(_zoom + e.delta.y * 0.5f, ZoomMin, ZoomMax);
                e.Use();
            }

            HandleOrbit(e, inside);
            UpdatePick(rect, inside);
            UpdateMarkers();

            if (inside && e.type == EventType.MouseDown && e.button == 0)
            {
                PlaceAt();
                e.Use();
            }
        }

        void HandleOrbit(Event e, bool inside)
        {
            if (e.type == EventType.MouseDown && e.button == 1 && inside)
            {
                _rbDown = true;
                _rbStart = e.mousePosition;
                _rbMoved = 0f;
                e.Use();
                return;
            }

            // 只用 MouseDrag：按住键拖动时 Unity 才发这个事件，正好就是「转视角」。
            // 顺带躲开 UiSkin.WorthDrawing —— 它把 MouseMove 那一趟整段跳过了，
            // 挂在这里的更新就永远轮不到。
            if (_rbDown && e.type == EventType.MouseDrag)
            {
                Vector2 d = e.mousePosition - _rbStart;
                _rbStart = e.mousePosition;
                _rbMoved += d.magnitude;
                _yaw += d.x * OrbitDegPerPixel;
                _pitch = Mathf.Clamp(_pitch - d.y * OrbitDegPerPixel, -80f, 80f);
                return;
            }

            // 抬手时按总位移区分「擦除」和「转视角」。5 像素的容错：手抖不算转视角
            if (_rbDown && e.type == EventType.MouseUp && e.button == 1)
            {
                _rbDown = false;
                if (_rbMoved < 5f && inside)
                    EraseAt();
                e.Use();
            }
        }

        /// <summary>把鼠标位置变成一条射线，解出「点在哪一格、撞的是哪个面」。</summary>
        void UpdatePick(Rect rect, bool inside)
        {
            _hasHit = false;
            _hasTarget = false;

            if (!inside || _cam == null || _root == null)
                return;

            Vector2 local = Event.current.mousePosition - rect.position;
            float u = Mathf.Clamp01(local.x / rect.width);
            float v = Mathf.Clamp01(local.y / rect.height);

            Ray ray = _cam.ViewportPointToRay(new Vector3(u, 1f - v, 0f));
            Vector3 o = _root.InverseTransformPoint(ray.origin);
            Vector3 d = _root.InverseTransformDirection(ray.direction);

            var box = new Vector3Int(Size, Size, Size);
            if (VoxelRaycast.Trace(o, d, box, _isSolid, out VoxelHit h))
            {
                _hasHit = true;
                _hit = h;
                Vector3Int t = h.Cell + h.Face;
                // 撞在贴着盒壁的那一面上时，外侧那一格已经出盒了 —— 提示一下而不是硬塞
                if (MoldData.InBox(t))
                {
                    _target = t;
                    _hasTarget = true;
                }
                return;
            }

            // 空盒子（或者射线从方块之间的缝里穿出去了）：退一步让它落在地板上。
            // 没有这一步的话第一格永远放不下去 —— 盒子里一个方块都没有，射线打不到任何东西。
            if (GroundCell(o, d, box, out Vector3Int g))
            {
                _target = g;
                _hasTarget = true;
            }
        }

        void UpdateMarkers()
        {
            // 幽灵框：下一块会落在哪
            bool showGhost = _hasTarget;
            _ghost.gameObject.SetActive(showGhost);
            if (showGhost)
                _ghost.localPosition = CellCenter(_target);

            // 命中面的高亮：一小片贴在那一面上，告诉你新方块往哪边长
            bool showFace = _hasHit && _hit.Face != Vector3Int.zero;
            _faceHi.gameObject.SetActive(showFace);
            if (showFace)
            {
                _faceHi.localPosition = CellCenter(_hit.Cell)
                    + new Vector3(_hit.Face.x, _hit.Face.y, _hit.Face.z) * 0.53f;
                _faceHi.localRotation = Quaternion.LookRotation(
                    new Vector3(_hit.Face.x, _hit.Face.y, _hit.Face.z));
            }
        }

        void PlaceAt()
        {
            if (!_hasTarget)
            {
                Say("这一下没落在建造盒里");
                return;
            }
            if (_keys.Contains(Key(_target)))
            {
                Say("这一格已经有方块了");
                return;
            }
            if (_cells.Count >= MaxCells)
            {
                Say("最多只能放 " + MaxCells + " 格");
                return;
            }

            Add(_target);
            // 放完自动跟到那一层：不然新块会落在别的层里、立刻变半透明，像是没放上
            _layer = _target.z;
            RefreshCells();
            Notify();
        }

        void EraseAt()
        {
            if (!_hasHit)
                return;
            Remove(_hit.Cell);
            RefreshCells();
            Notify();
        }

        void Say(string msg)
        {
            _hint = msg;
            _hintEnd = Time.realtimeSinceStartup + HintSeconds;
        }

        void Notify()
        {
            if (OnChanged != null)
                OnChanged();
        }

        // ---------- 格子集合 ----------

        bool IsSolid(Vector3Int c)
        {
            return _keys.Contains(Key(c));
        }

        bool Add(Vector3Int c)
        {
            if (!MoldData.InBox(c))
                return false;
            if (!_keys.Add(Key(c)))
                return false;
            _cells.Add(c);
            return true;
        }

        bool Remove(Vector3Int c)
        {
            if (!_keys.Remove(Key(c)))
                return false;
            for (int i = _cells.Count - 1; i >= 0; i--)
            {
                if (_cells[i] == c)
                {
                    _cells.RemoveAt(i);
                    break;
                }
            }
            return true;
        }

        static int Key(Vector3Int c)
        {
            return (c.x * Size + c.y) * Size + c.z;
        }

        static Vector3 CellCenter(Vector3Int c)
        {
            return new Vector3(c.x + 0.5f, c.y + 0.5f, c.z + 0.5f);
        }

        void RefreshCells()
        {
            while (_cellTs.Count < _cells.Count)
            {
                Transform t = NewChild("Cell" + _cellTs.Count, _cube, _blockMat);
                _cellTs.Add(t);
                _cellRends.Add(t.GetComponent<MeshRenderer>());
            }

            for (int i = 0; i < _cellTs.Count; i++)
            {
                if (i >= _cells.Count)
                {
                    _cellTs[i].gameObject.SetActive(false);
                    continue;
                }
                _cellTs[i].gameObject.SetActive(true);
                _cellTs[i].localPosition = CellCenter(_cells[i]);
                _cellRends[i].sharedMaterial = (_dimOthers && _cells[i].z != _layer) ? _dimMat : _blockMat;
            }
        }

        // ---------- 纯几何：独立出来是为了能进自检 ----------

        /// <summary>
        /// 射线打不到任何方块时，退一步算它和地板（y=0 那一面）的交点。
        /// 盒子空着的时候这一步是唯一能让玩家放下第一格的途径。
        /// </summary>
        public static bool GroundCell(Vector3 origin, Vector3 dir, Vector3Int size, out Vector3Int cell)
        {
            cell = Vector3Int.zero;

            if (Mathf.Abs(dir.y) < 1e-6f)
                return false;

            float t = -origin.y / dir.y;
            if (t < 0f)
                return false;

            float x = origin.x + dir.x * t;
            float z = origin.z + dir.z * t;
            if (x < 0f || x >= size.x || z < 0f || z >= size.z)
                return false;

            cell = new Vector3Int(
                Mathf.Clamp(Mathf.FloorToInt(x), 0, size.x - 1),
                0,
                Mathf.Clamp(Mathf.FloorToInt(z), 0, size.z - 1));
            return true;
        }

        /// <summary>沿某一轴镜像（0=x，1=y，2=z）。坐标按建造盒的整条边翻，不是按形状自己的包围盒。</summary>
        public static Vector3Int[] Mirrored(IList<Vector3Int> cells, int axis)
        {
            var result = new Vector3Int[cells.Count];
            for (int i = 0; i < cells.Count; i++)
            {
                Vector3Int c = cells[i];
                if (axis == 0)
                    c.x = Size - 1 - c.x;
                else if (axis == 1)
                    c.y = Size - 1 - c.y;
                else
                    c.z = Size - 1 - c.z;
                result[i] = c;
            }
            return result;
        }

        /// <summary>把整块平移到建造盒正中（三个轴各自居中，取整时往下偏一点）。</summary>
        public static Vector3Int[] Centered(IList<Vector3Int> cells)
        {
            var result = new Vector3Int[cells.Count];
            if (cells.Count == 0)
                return result;

            int[] min = new int[3];
            int[] max = new int[3];
            for (int a = 0; a < 3; a++)
            {
                min[a] = int.MaxValue;
                max[a] = int.MinValue;
            }
            for (int i = 0; i < cells.Count; i++)
            {
                int[] v = { cells[i].x, cells[i].y, cells[i].z };
                for (int a = 0; a < 3; a++)
                {
                    if (v[a] < min[a]) min[a] = v[a];
                    if (v[a] > max[a]) max[a] = v[a];
                }
            }

            int[] off = new int[3];
            for (int a = 0; a < 3; a++)
                off[a] = (Size - (max[a] - min[a] + 1)) / 2 - min[a];

            for (int i = 0; i < cells.Count; i++)
            {
                Vector3Int c = cells[i];
                result[i] = new Vector3Int(
                    Mathf.Clamp(c.x + off[0], 0, Size - 1),
                    Mathf.Clamp(c.y + off[1], 0, Size - 1),
                    Mathf.Clamp(c.z + off[2], 0, Size - 1));
            }
            return result;
        }

        // ---------- 建场景 ----------

        Transform NewChild(string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.layer = WorkLayer;
            Transform t = go.transform;
            t.SetParent(_root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return t;
        }

        /// <summary>建造盒的框：12 条棱 + 底面 5×5 的网格。两套宽度分别给亮线和光晕。</summary>
        void BuildFrame()
        {
            var cv = new List<Vector3>();
            var ct = new List<int>();
            var gv = new List<Vector3>();
            var gt = new List<int>();

            var corners = new Vector3[8];
            for (int i = 0; i < 8; i++)
            {
                corners[i] = new Vector3(
                    (i & 1) != 0 ? Size : 0f,
                    (i & 2) != 0 ? Size : 0f,
                    (i & 4) != 0 ? Size : 0f);
            }

            int[,] edges =
            {
                { 0, 1 }, { 1, 3 }, { 3, 2 }, { 2, 0 },
                { 4, 5 }, { 5, 7 }, { 7, 6 }, { 6, 4 },
                { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 },
            };
            for (int i = 0; i < 12; i++)
                AddTube(cv, ct, gv, gt, corners[edges[i, 0]], corners[edges[i, 1]], 0.022f, 0.075f);

            // 底面网格：给个「地」的参照，不然空盒子看着像悬在半空
            for (int i = 0; i <= Size; i++)
            {
                AddTube(cv, ct, gv, gt, new Vector3(i, 0f, 0f), new Vector3(i, 0f, Size), 0.012f, 0.04f);
                AddTube(cv, ct, gv, gt, new Vector3(0f, 0f, i), new Vector3(Size, 0f, i), 0.012f, 0.04f);
            }

            NewChildWithMesh("BoxCore", cv, ct, _coreMat);
            NewChildWithMesh("BoxGlow", gv, gt, _glowMat);
        }

        void NewChildWithMesh(string name, List<Vector3> verts, List<int> tris, Material mat)
        {
            if (verts.Count == 0 || mat == null)
                return;

            var mesh = new Mesh { name = "MoldFrame_" + name };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            NewChild(name, mesh, mat);
        }

        /// <summary>
        /// 一根「管子」：两张互相垂直的四边形沿线段撑开，截面是个十字。
        /// 只铺一张四边形的话，视角转到和它平行时那条线会整根消失 —— 建造盒是要绕着看的，
        /// 用十字截面保证从任何角度看都还是一条线。
        /// </summary>
        static void AddTube(List<Vector3> cv, List<int> ct, List<Vector3> gv, List<int> gt,
            Vector3 a, Vector3 b, float coreHalf, float glowHalf)
        {
            AddTube(cv, ct, a, b, coreHalf);
            AddTube(gv, gt, a, b, glowHalf);
        }

        static void AddTube(List<Vector3> verts, List<int> tris, Vector3 a, Vector3 b, float half)
        {
            Vector3 dir = b - a;
            if (dir.sqrMagnitude < 1e-9f)
                return;
            dir.Normalize();

            Vector3 s1 = Vector3.Cross(dir, Mathf.Abs(dir.y) > 0.9f ? Vector3.forward : Vector3.up);
            if (s1.sqrMagnitude < 1e-9f)
                s1 = Vector3.Cross(dir, Vector3.right);
            s1.Normalize();
            Vector3 s2 = Vector3.Cross(dir, s1).normalized;

            AddQuad(verts, tris, a, b, s1 * half);
            AddQuad(verts, tris, a, b, s2 * half);
        }

        static void AddQuad(List<Vector3> verts, List<int> tris, Vector3 a, Vector3 b, Vector3 side)
        {
            int i = verts.Count;
            verts.Add(a - side);
            verts.Add(a + side);
            verts.Add(b + side);
            verts.Add(b - side);
            tris.Add(i);
            tris.Add(i + 1);
            tris.Add(i + 2);
            tris.Add(i);
            tris.Add(i + 2);
            tris.Add(i + 3);
        }

        /// <summary>立方体的 12 条棱，用作「下一块会落在哪」的幽灵框。</summary>
        static Mesh BuildWireCube(float half, float tubeHalf)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();

            var corners = new Vector3[8];
            for (int i = 0; i < 8; i++)
            {
                corners[i] = new Vector3(
                    (i & 1) != 0 ? half : -half,
                    (i & 2) != 0 ? half : -half,
                    (i & 4) != 0 ? half : -half);
            }

            int[,] edges =
            {
                { 0, 1 }, { 1, 3 }, { 3, 2 }, { 2, 0 },
                { 4, 5 }, { 5, 7 }, { 7, 6 }, { 6, 4 },
                { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 },
            };
            for (int i = 0; i < 12; i++)
                AddTube(verts, tris, corners[edges[i, 0]], corners[edges[i, 1]], tubeHalf);

            var mesh = new Mesh { name = "MoldWireCube" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>朝向 +Z 的单位四边形，用来高亮被点中的那一面。</summary>
        static Mesh BuildQuad()
        {
            var mesh = new Mesh { name = "MoldFaceQuad" };
            mesh.SetVertices(new List<Vector3>
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f),
            });
            mesh.SetTriangles(new List<int> { 0, 1, 2, 0, 2, 3 }, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // ---------- 材质 ----------

        static Material CloneMat(string resourceName, Color color)
        {
            Material baseMat = Resources.Load<Material>(resourceName);
            if (baseMat == null)
                return null;
            var mat = new Material(baseMat) { color = color };
            return mat;
        }

        static Material CloneGlow(Color color)
        {
            // 光晕材质是加色混合：颜色越亮叠得越白，这里只是借它的混合方式
            return CloneMat("GridGlowMat", color);
        }

        static Material MakeGhostMaterial(Color color)
        {
            Material baseMat = Resources.Load<Material>("MoldGhostMat");
            if (baseMat == null)
            {
                Shader shader = Shader.Find("MinecraftTetris/MoldGhost");
                if (shader != null)
                {
                    // 材质资源还没生成（比如刚加了 shader 没跑过生成器）时退一步：
                    // 运行时建一个，贴图直接借图集材质上那张
                    baseMat = new Material(shader);
                    baseMat.mainTexture = BlockMesh.AtlasMaterial.mainTexture;
                }
            }
            if (baseMat == null)
                return null;

            var mat = new Material(baseMat);
            mat.color = color;
            if (mat.mainTexture == null)
                mat.mainTexture = BlockMesh.AtlasMaterial.mainTexture;
            return mat;
        }

        void OnDestroy()
        {
            if (_rt != null)
            {
                _rt.Release();
                Destroy(_rt);
                _rt = null;
            }
        }
    }
}
