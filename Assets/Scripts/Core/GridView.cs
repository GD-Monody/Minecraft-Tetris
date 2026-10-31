using System.Collections.Generic;
using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>
    /// 把 Grid 和当前方块画出来。逻辑层永远是格子制，这里多做一层"视觉位置"：
    /// 每个显示出来的方块都有一个会慢慢追上目标格子的位置，于是横移、下落、旋转、
    /// 沙子塌落看起来都是滑过去的，而不是一帧瞬移。
    /// </summary>
    public class GridView : MonoBehaviour
    {
        GameObject[,,] _cells;
        Vector3[,,] _cellVisual;

        /// <summary>
        /// 这一格的视觉位置是不是已经贴在格子上了。贴上之后 Update 就完全不管它 ——
        /// 场上一大半格子是静止的，以前每帧照样给它们算一次 Lerp、写一次 transform，
        /// 深度开到 6 就是上千次白做的写。
        /// </summary>
        bool[,,] _settled;

        GameObject[] _pieceCells;
        Vector3[] _pieceVisual;
        Vector3[] _pieceTarget;
        Transform _pieceRoot;

        /// <summary>落点提示框：当前方块直接落下去会停在哪，就在那儿摆一圈高亮方框。</summary>
        GameObject[] _ghostCells;
        Transform _ghostRoot;

        /// <summary>正在下落的那个方块的父节点（调试用）。</summary>
        public Transform PieceRoot { get { return _pieceRoot; } }

        Mesh[] _meshes;
        Material[] _materials;

        /// <summary>TNT 引信闪白用的纯白材质（Unlit/Color，不吃贴图）。</summary>
        Material _flashMaterial;

        /// <summary>哪些格子现在该画成白闪（点着的 TNT）。SetCell 重建时也要照着它选材质。</summary>
        bool[,,] _flashing;

        /// <summary>正在烧的格子外面那层火苗（火）。方块表没有「火」这种方块，
        /// 所以用几片贴着原版火贴图的面片裹住它 —— 比换材质染色更像火。</summary>
        GameObject[,,] _fireCovers;
        bool[,,] _burning;
        /// <summary>每格火苗当前的「露脸方向」组合（见 FireBit*）。邻居变了要换网格。</summary>
        int[,,] _fireMask;
        Material _fireMaterial;
        float _fireTime;

        /// <summary>火苗贴图一帧 16 行、一共 32 帧（原版 fire_0.png 就是 16x512）。</summary>
        const int FireFrames = 32;
        /// <summary>每帧停留多久：原版 mcmeta 没写 frametime，默认 1 tick = 0.05 秒。</summary>
        const float FireFrameTime = 0.05f;
        /// <summary>原版 mcmeta 的播放顺序是从第 16 帧起头的。</summary>
        const int FireFrameOffset = 16;

        /// <summary>露脸方向的位：+x / -x / +z / -z / +y。+y 那一位同时决定火苗窜多高。</summary>
        public const int FireBitPx = 1;
        public const int FireBitNx = 2;
        public const int FireBitPz = 4;
        public const int FireBitNz = 8;
        public const int FireBitUp = 16;

        /// <summary>火苗面片离方块表面一点点，别和方块自己的面重合（方块比 1 格大 0.04 会打架）。</summary>
        const float FireOffset = 0.06f;

        /// <summary>
        /// 贴图是 32 宽：左半列是原版的 fire_0，右半列是 fire_1。
        /// 各往里缩半格，免得点采样刚好采到隔壁那一列。
        /// </summary>
        const float FireU0A = 0.5f / 32f;
        const float FireU1A = 15.5f / 32f;
        const float FireU0B = 31.5f / 32f;
        const float FireU1B = 16.5f / 32f;

        /// <summary>流体（水/岩浆）按水位分档的矮方块 mesh：blockId -> [level0..levelMax]。</summary>
        readonly Dictionary<int, Mesh[]> _fluidMeshes = new Dictionary<int, Mesh[]>();

        // 下落中方块的朝向。逻辑层存的是整数旋转矩阵（Piece.Rot），这里存它的四元数形式，
        // 每帧拿目标值做球面插值，看起来就是方块原地转过去的。
        // 三根轴（面内 / 俯视 / 前后翻）都走同一个四元数，不用再分开记角度。
        Quaternion _pieceRot = Quaternion.identity;
        Quaternion _pieceRotTarget = Quaternion.identity;

        // 追赶速度：越大越快贴到位。16 大约 0.1 秒走完 80%
        const float Smooth = 16f;

        public Grid Grid { get; private set; }
        public float CellSize = 1f;

        public void Setup(Grid grid)
        {
            Grid = grid;
            _cells = new GameObject[grid.SizeX, grid.SizeY, grid.SizeZ];
            _cellVisual = new Vector3[grid.SizeX, grid.SizeY, grid.SizeZ];
            _settled = new bool[grid.SizeX, grid.SizeY, grid.SizeZ];
            _flashing = new bool[grid.SizeX, grid.SizeY, grid.SizeZ];
            _burning = new bool[grid.SizeX, grid.SizeY, grid.SizeZ];
            _fireMask = new int[grid.SizeX, grid.SizeY, grid.SizeZ];
            _fireCovers = new GameObject[grid.SizeX, grid.SizeY, grid.SizeZ];
            BuildResources();

            _pieceRoot = new GameObject("Piece").transform;
            _pieceRoot.SetParent(transform, false);
            _ghostRoot = new GameObject("Ghost").transform;
            _ghostRoot.SetParent(transform, false);
            _pieceCells = new GameObject[4];
            _pieceVisual = new Vector3[4];
            _pieceTarget = new Vector3[4];
            for (int i = 0; i < 4; i++)
            {
                _pieceCells[i] = NewCubeObject("PieceCell" + i);
                _pieceCells[i].transform.SetParent(_pieceRoot, false);
                _pieceCells[i].SetActive(false);
            }

            Debug.Log("[setup] grid=" + grid.SizeX + "x" + grid.SizeY + "x" + grid.SizeZ
                + " cells=" + (_cells != null ? _cells.GetLength(0).ToString() : "null")
                + " pieceCells=" + (_pieceCells != null ? _pieceCells.Length.ToString() : "null")
                + " meshes=" + (_meshes != null ? _meshes.Length.ToString() : "null")
                + " materials=" + (_materials != null ? _materials.Length.ToString() : "null")
                + " 空材质=" + (CountNullMaterials()));
        }

        void BuildResources()
        {
            int count = BlockCatalog.Blocks.Length;
            _meshes = new Mesh[count];
            _materials = new Material[count];
            for (int i = 0; i < count; i++)
            {
                BlockDef def = BlockCatalog.Blocks[i];
                _meshes[i] = BlockMesh.Build(def);
                _materials[i] = BlockMesh.GetMaterial(def);

                // 流体另外做几档高度的矮方块：水位越浅，水面越低，才像 MC 里流过来的水
                if (Fluids.IsFluid(i + 1))
                {
                    var levels = new Mesh[Fluids.MaxAmount + 1];
                    for (int lv = 0; lv <= Fluids.MaxAmount; lv++)
                        levels[lv] = BlockMesh.BuildFluid(def, FluidHeight(lv));
                    _fluidMeshes[i + 1] = levels;
                }
            }
        }

        /// <summary>水位 0 是水源（满格），之后每级矮一点，到最外圈只剩一层薄水面。</summary>
        static float FluidHeight(int amount)
        {
            // 8 份 = 满格；摊开以后按份数给高度，1 份就是 1/8 薄薄一层
            return Mathf.Clamp01((float)amount / Fluids.MaxAmount);
        }

        int CountNullMaterials()
        {
            if (_materials == null) return -1;
            int n = 0;
            for (int i = 0; i < _materials.Length; i++)
                if (_materials[i] == null) n++;
            return n;
        }

        /// <summary>按材质编号取渲染用的 mesh（预览和粒子共用）。</summary>
        public Mesh GetMesh(int blockId)
        {
            int idx = blockId - 1;
            if (idx < 0 || idx >= _meshes.Length)
                idx = 0;
            return _meshes[idx];
        }

        /// <summary>按材质编号取渲染用的材质。</summary>
        public Material GetMaterial(int blockId)
        {
            int idx = blockId - 1;
            if (idx < 0 || idx >= _materials.Length)
                idx = 0;
            return _materials[idx];
        }

        void Update()
        {
            if (Grid == null)
                return;

            float k = 1f - Mathf.Exp(-Smooth * Time.deltaTime);
            _fireTime += Time.deltaTime;
            UpdateFireFrame();

            // 拖窗口那几帧直接就位：那一帧的时间本来就紧，而且画面正在变形，
            // 看不出这一格是滑过去的还是一步到位的。
            bool snapAll = ResizeGuard.Busy;

            // 落定的方块：朝自己的格子追，追上后就停在格子上（之后不再碰它）
            for (int x = 0; x < Grid.SizeX; x++)
                for (int y = 0; y < Grid.SizeY; y++)
                    for (int z = 0; z < Grid.SizeZ; z++)
                    {
                        GameObject go = _cells[x, y, z];
                        if (go == null || !go.activeSelf)
                            continue;
                        if (_settled[x, y, z] && !snapAll)
                            continue;

                        // 火苗也跟着走（方块塌下来的时候火不能留在原地）
                        bool burn = _burning != null && _burning[x, y, z];

                        Vector3 v = _cellVisual[x, y, z];
                        float dx = v.x - x;
                        float dy = v.y - y;
                        float dz = v.z - z;
                        // 差不到百分之一格就算到了：屏幕上不到半个像素，看不出来，
                        // 再追下去是指数收敛，永远追不到 0。
                        if (snapAll || dx * dx + dy * dy + dz * dz < 0.0001f)
                        {
                            var p = new Vector3(x, y, z);
                            _cellVisual[x, y, z] = p;
                            go.transform.position = p;
                            _settled[x, y, z] = true;
                            if (burn)
                                UpdateFireCover(x, y, z);
                            continue;
                        }

                        _cellVisual[x, y, z] = Vector3.Lerp(v, new Vector3(x, y, z), k);
                        go.transform.position = _cellVisual[x, y, z];
                        if (burn)
                            UpdateFireCover(x, y, z);
                    }

            // 正在下落的方块：位置追目标，朝向也慢慢转到新角度
            if (_pieceCells == null)
                return;
            _pieceRot = Quaternion.Slerp(_pieceRot, _pieceRotTarget, k * 1.4f);
            Quaternion rot = _pieceRot;
            for (int i = 0; i < _pieceCells.Length; i++)
            {
                GameObject go = _pieceCells[i];
                if (go == null || !go.activeSelf)
                    continue;
                _pieceVisual[i] = Vector3.Lerp(_pieceVisual[i], _pieceTarget[i], k);
                go.transform.position = _pieceVisual[i];
                go.transform.rotation = rot;
            }
        }

        public void RefreshAll()
        {
            for (int x = 0; x < Grid.SizeX; x++)
                for (int y = 0; y < Grid.SizeY; y++)
                    for (int z = 0; z < Grid.SizeZ; z++)
                        SetCell(x, y, z, Grid.Get(x, y, z));
        }

        public void SetCell(int x, int y, int z, int blockId)
        {
            // 水现在不往 grid 里写方块 id（它是纯视觉的"不完整方块"），
            // 所以这里要单独看这一格的水量：没有方块但有水，也要画出来。
            int fAmt = Fluids.AmountAt(x, y, z);
            if (blockId <= 0 && fAmt <= 0)
            {
                if (_cells[x, y, z] != null)
                    _cells[x, y, z].SetActive(false);
                return;
            }

            GameObject go = _cells[x, y, z];
            if (go == null)
            {
                go = NewCubeObject("Cell_" + x + "_" + y + "_" + z);
                go.transform.SetParent(transform, false);
                _cells[x, y, z] = go;
                _cellVisual[x, y, z] = new Vector3(x, y, z);
                _settled[x, y, z] = true;
            }
            else if (!go.activeSelf)
            {
                // 这一格刚由空变成有：直接出现在格子里。
                // 不复用上一次留下的视觉位置，否则会看到它从别处滑进来（硬降后尤其明显）。
                _cellVisual[x, y, z] = new Vector3(x, y, z);
                _settled[x, y, z] = true;
                // 朝向也归零：这个物体以前可能装过别的方块、被转过去过。
                go.transform.rotation = Quaternion.identity;
            }

            int idx;
            Mesh mesh;
            bool isFluid = false;
            if (blockId > 0)
            {
                idx = blockId - 1;
                mesh = _meshes[idx];
            }
            else
            {
                isFluid = true;
                // 这一格没有方块，只有水：按水量选矮一点的 mesh
                int fid = Fluids.IdAt(x, y, z);
                if (fid <= 0)
                {
                    if (_cells[x, y, z] != null)
                        _cells[x, y, z].SetActive(false);
                    return;
                }
                idx = fid - 1;
                mesh = _meshes[idx];
                Mesh[] fluidLevels;
                if (_fluidMeshes.TryGetValue(fid, out fluidLevels))
                {
                    // 每格只按自己那格的份数画高度：1 份就是 1/8 的薄水，8 份才是满格。
                    // （以前"上面压着水就画满格"会把 3 个 1/8 拼成 1 个满格，看起来水凭空变多。）
                    int lv = fAmt;
                    if (lv < 0) lv = 0;
                    if (lv >= fluidLevels.Length) lv = fluidLevels.Length - 1;
                    mesh = fluidLevels[lv];
                }
            }

            go.SetActive(true);
            go.transform.position = _cellVisual[x, y, z];
            if (isFluid)
            {
                // 水/岩浆是一块矮方块（高度 = 水位），不是正方体。
                // 一旦继承了上一次落定时留下的面内旋转（roll），它就会从「一片水面」立成「一片竖墙」。
                // 水面永远是水平的，所以流体格子不吃任何旋转。
                go.transform.rotation = Quaternion.identity;
            }
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            // 点着的 TNT 画白闪。这里也要判一次：RefreshAll / SetCell 会重设材质，
            // 只在 SetCellFlash 里改一次的话，下一次重建就丢了。
            // 顺手把「这一格已经不是 TNT 了」的旧标记清掉，否则下一块落到这里会莫名其妙是白的。
            bool wantFlash = _flashing != null && _flashing[x, y, z];
            if (wantFlash && (isFluid || !Tnt.IsTnt(blockId)))
            {
                _flashing[x, y, z] = false;
                wantFlash = false;
            }
            go.GetComponent<MeshRenderer>().sharedMaterial = wantFlash ? FlashMaterial : _materials[idx];
        }

        /// <summary>
        /// 结构方块重建：这一格放回方块，但视觉上先出现在目标上方 drop 格的位置，
        /// 交给 Update 的追赶把它"掉"下来。drop &lt;= 0 时等同直接出现。
        /// </summary>
        public void SetCellDropping(int x, int y, int z, int blockId, int drop)
        {
            SetCell(x, y, z, blockId);
            if (drop <= 0 || !InCells(x, y, z))
                return;

            GameObject go = _cells[x, y, z];
            if (go == null || !go.activeSelf)
                return;

            _cellVisual[x, y, z] = new Vector3(x, y + drop, z);
            go.transform.position = _cellVisual[x, y, z];
            _settled[x, y, z] = false;   // 从上面掉下来，交给 Update 追
        }

        /// <summary>把一格画成白闪（TNT 引信）或还原。</summary>
        public void SetCellFlash(int x, int y, int z, bool on)
        {
            if (!InCells(x, y, z) || _flashing == null)
                return;
            if (_flashing[x, y, z] == on)
                return;
            _flashing[x, y, z] = on;

            GameObject go = _cells[x, y, z];
            if (go == null || !go.activeSelf)
                return;

            int id = Grid.Get(x, y, z);
            if (id <= 0)
                return;
            go.GetComponent<MeshRenderer>().sharedMaterial = on ? FlashMaterial : _materials[id - 1];
        }

        /// <summary>
        /// 给一格套上 / 摘掉火苗。火苗是几片贴着原版火贴图的面片，只长在这一格
        /// 露在外面的那几个面上 —— 埋在方块堆里的那一面本来就看不见，画了也是白画，
        /// 还会从上一格的缝隙里露出半截火苗。
        /// </summary>
        public void SetCellBurning(int x, int y, int z, bool on)
        {
            if (!InCells(x, y, z) || _burning == null)
                return;
            if (_burning[x, y, z] == on)
                return;

            _burning[x, y, z] = on;

            GameObject cover = _fireCovers[x, y, z];
            if (!on)
            {
                if (cover != null)
                    cover.SetActive(false);
                return;
            }

            if (cover == null)
            {
                cover = NewCubeObject("Fire_" + x + "_" + y + "_" + z);
                cover.transform.SetParent(transform, false);
                _fireCovers[x, y, z] = cover;
            }

            int mask = FireMask(x, y, z);
            _fireMask[x, y, z] = mask;
            cover.GetComponent<MeshFilter>().sharedMesh = GetFireMesh(mask);
            cover.GetComponent<MeshRenderer>().sharedMaterial = FireMaterial;
            // 火苗不跟着方块转：落定的方块可能带着落下时的朝向，火跟着转就躺下了
            cover.transform.rotation = Quaternion.identity;
            cover.SetActive(true);
        }

        /// <summary>火苗每帧跟着方块的位置走；邻居变了就换成另一套面片。</summary>
        void UpdateFireCover(int x, int y, int z)
        {
            GameObject cover = _fireCovers[x, y, z];
            if (cover == null || !cover.activeSelf)
                return;

            cover.transform.position = _cellVisual[x, y, z];

            // 消行塌下来、上面的方块被烧掉之后，露脸的方向就变了
            int mask = FireMask(x, y, z);
            if (mask == _fireMask[x, y, z])
                return;

            _fireMask[x, y, z] = mask;
            var filter = cover.GetComponent<MeshFilter>();
            if (filter != null)
                filter.sharedMesh = GetFireMesh(mask);
        }

        /// <summary>这一格哪几个面是露在外面的（旁边的格子空着、或者在场地外）。</summary>
        int FireMask(int x, int y, int z)
        {
            int mask = 0;
            if (FireFaceOpen(x + 1, y, z)) mask |= FireBitPx;
            if (FireFaceOpen(x - 1, y, z)) mask |= FireBitNx;
            if (FireFaceOpen(x, y, z + 1)) mask |= FireBitPz;
            if (FireFaceOpen(x, y, z - 1)) mask |= FireBitNz;
            if (FireFaceOpen(x, y + 1, z)) mask |= FireBitUp;
            return mask;
        }

        /// <summary>
        /// 场地只有左右和底下有边框，前后两面是敞开的，所以越界也算「露在外面」；
        /// 上面那格空着的时候火苗才窜得比方块高。
        /// </summary>
        bool FireFaceOpen(int x, int y, int z)
        {
            if (Grid == null)
                return false;
            if (x < 0 || y < 0 || z < 0 || x >= Grid.SizeX || y >= Grid.SizeY || z >= Grid.SizeZ)
                return true;
            return Grid.Get(x, y, z) <= 0;
        }

        /// <summary>
        /// 按露脸方向取火苗网格。31 种组合最多各建一次，之后全是共享的
        /// （一片木头一起烧的时候，每格的面片组合就那么几种）。
        /// </summary>
        Mesh GetFireMesh(int mask)
        {
            if (_fireMeshes == null)
                _fireMeshes = new Dictionary<int, Mesh>();

            Mesh mesh;
            if (_fireMeshes.TryGetValue(mask, out mesh))
                return mesh;

            mesh = BuildFireMesh(mask);
            _fireMeshes[mask] = mesh;
            return mesh;
        }

        Dictionary<int, Mesh> _fireMeshes;

        /// <summary>
        /// 照原版的火方块模型摆面片：
        /// - 侧面是一片竖着的火苗（原版 template_fire_side：16 宽 x 22.4 高，比一格高 0.4，
        ///   火苗才像往上窜）。上面那格被压着的时候就只长到和方块齐平，免得插进上一格里。
        /// - 每个面铺两层，一层 fire_0 一层 fire_1（原版 fire_floor0 + fire_floor1 就是这么叠的），
        ///   两层贴同一张平面、第二层把贴图左右翻一下 —— 一层看着太稀。
        /// - 顶面那片是躺着铺的（原版 template_fire_up 也是在顶上铺一片），
        ///   方块四周都被埋住的时候，就靠它告诉玩家「这一格在烧」。
        /// </summary>
        public static Mesh BuildFireMesh(int mask)
        {
            bool tall = (mask & FireBitUp) != 0;
            // 格心在原点，方块占 -0.5 ~ 0.5；窜得高的时候到 0.9
            float top = tall ? 0.9f : 0.5f;
            float off = FireOffset;

            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            // 第一层：fire_0，正常朝向
            if ((mask & FireBitPx) != 0)
                FireQuad(verts, uvs, tris, FireU0A, FireU1A,
                    new Vector3(off + 0.5f, -0.5f, 0.5f), new Vector3(off + 0.5f, -0.5f, -0.5f),
                    new Vector3(off + 0.5f, top, -0.5f), new Vector3(off + 0.5f, top, 0.5f));
            if ((mask & FireBitNx) != 0)
                FireQuad(verts, uvs, tris, FireU0A, FireU1A,
                    new Vector3(-off - 0.5f, -0.5f, -0.5f), new Vector3(-off - 0.5f, -0.5f, 0.5f),
                    new Vector3(-off - 0.5f, top, 0.5f), new Vector3(-off - 0.5f, top, -0.5f));
            if ((mask & FireBitPz) != 0)
                FireQuad(verts, uvs, tris, FireU0A, FireU1A,
                    new Vector3(-0.5f, -0.5f, off + 0.5f), new Vector3(0.5f, -0.5f, off + 0.5f),
                    new Vector3(0.5f, top, off + 0.5f), new Vector3(-0.5f, top, off + 0.5f));
            if ((mask & FireBitNz) != 0)
                FireQuad(verts, uvs, tris, FireU0A, FireU1A,
                    new Vector3(0.5f, -0.5f, -off - 0.5f), new Vector3(-0.5f, -0.5f, -off - 0.5f),
                    new Vector3(-0.5f, top, -off - 0.5f), new Vector3(0.5f, top, -off - 0.5f));

            // 顶面：贴图底边（火苗那一头）朝着 +z，也就是朝着相机那一侧
            if ((mask & FireBitUp) != 0)
                FireQuad(verts, uvs, tris, FireU0A, FireU1A,
                    new Vector3(-0.5f, off + 0.5f, 0.5f), new Vector3(0.5f, off + 0.5f, 0.5f),
                    new Vector3(0.5f, off + 0.5f, -0.5f), new Vector3(-0.5f, off + 0.5f, -0.5f));

            // 第二层：fire_1，同一张平面、贴图左右翻过来
            if ((mask & FireBitPx) != 0)
                FireQuad(verts, uvs, tris, FireU0B, FireU1B,
                    new Vector3(off + 0.5f, -0.5f, 0.5f), new Vector3(off + 0.5f, -0.5f, -0.5f),
                    new Vector3(off + 0.5f, top, -0.5f), new Vector3(off + 0.5f, top, 0.5f));
            if ((mask & FireBitNx) != 0)
                FireQuad(verts, uvs, tris, FireU0B, FireU1B,
                    new Vector3(-off - 0.5f, -0.5f, -0.5f), new Vector3(-off - 0.5f, -0.5f, 0.5f),
                    new Vector3(-off - 0.5f, top, 0.5f), new Vector3(-off - 0.5f, top, -0.5f));
            if ((mask & FireBitPz) != 0)
                FireQuad(verts, uvs, tris, FireU0B, FireU1B,
                    new Vector3(-0.5f, -0.5f, off + 0.5f), new Vector3(0.5f, -0.5f, off + 0.5f),
                    new Vector3(0.5f, top, off + 0.5f), new Vector3(-0.5f, top, off + 0.5f));
            if ((mask & FireBitNz) != 0)
                FireQuad(verts, uvs, tris, FireU0B, FireU1B,
                    new Vector3(0.5f, -0.5f, -off - 0.5f), new Vector3(-0.5f, -0.5f, -off - 0.5f),
                    new Vector3(-0.5f, top, -off - 0.5f), new Vector3(0.5f, top, -off - 0.5f));
            if ((mask & FireBitUp) != 0)
                FireQuad(verts, uvs, tris, FireU0B, FireU1B,
                    new Vector3(-0.5f, off + 0.5f, 0.5f), new Vector3(0.5f, off + 0.5f, 0.5f),
                    new Vector3(0.5f, off + 0.5f, -0.5f), new Vector3(-0.5f, off + 0.5f, -0.5f));

            Mesh mesh = new Mesh();
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>铺一片火苗。a、b 是底边两头，c、d 是顶边两头（贴图底边 = 火苗那一头）。</summary>
        static void FireQuad(List<Vector3> v, List<Vector2> uv, List<int> t, float u0, float u1,
                             Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int i = v.Count;
            v.Add(a);
            v.Add(b);
            v.Add(c);
            v.Add(d);

            uv.Add(new Vector2(u0, 0f));
            uv.Add(new Vector2(u1, 0f));
            uv.Add(new Vector2(u1, 1f));
            uv.Add(new Vector2(u0, 1f));

            t.Add(i);
            t.Add(i + 1);
            t.Add(i + 2);
            t.Add(i);
            t.Add(i + 2);
            t.Add(i + 3);
        }

        /// <summary>清掉所有火苗（重开一局时用）。</summary>
        public void ClearFire()
        {
            if (_burning == null)
                return;
            for (int x = 0; x < Grid.SizeX; x++)
                for (int y = 0; y < Grid.SizeY; y++)
                    for (int z = 0; z < Grid.SizeZ; z++)
                        if (_burning[x, y, z])
                            SetCellBurning(x, y, z, false);
        }

        /// <summary>
        /// 火苗用的材质：原版火贴图（32 帧动画条）+ alpha test 的不吃光 shader。
        /// 克隆 FireMat（工程里的资源），理由和 FlashMaterial 一样：
        /// 构建版里裸 Shader.Find 常拿到 null。
        /// </summary>
        Material FireMaterial
        {
            get
            {
                if (_fireMaterial != null)
                    return _fireMaterial;

                Material baseMat = Resources.Load<Material>("FireMat");
                if (baseMat != null)
                {
                    _fireMaterial = new Material(baseMat);
                }
                else
                {
                    Shader shader = Shader.Find("MinecraftTetris/FireCutout");
                    if (shader != null)
                    {
                        _fireMaterial = new Material(shader);
                        Texture2D tex = Resources.Load<Texture2D>("Fire");
                        if (tex != null)
                        {
                            tex.filterMode = FilterMode.Point;
                            _fireMaterial.mainTexture = tex;
                        }
                    }
                }
                return _fireMaterial;
            }
        }

        /// <summary>
        /// 每帧把材质切到动画条的下一帧。全场共用一份材质，所以所有火苗是同步跳的
        /// （原版也是：贴图动画按全局时间走，一片火一起烧就是一起跳）。
        ///
        /// v 往里缩了半格：网格里 UV 是 0~1，正好落在帧边界上时点采样会采到隔壁那一帧，
        /// 缩半格才能保证取到的是这一帧自己的像素。
        /// </summary>
        void UpdateFireFrame()
        {
            Material mat = FireMaterial;
            if (mat == null || mat.mainTexture == null)
                return;

            int f = ((int)(_fireTime / FireFrameTime) + FireFrameOffset) % FireFrames;
            float h = (float)(FireFrames * 16);
            mat.SetTextureScale("_MainTex", new Vector2(1f, 15f / h));
            mat.SetTextureOffset("_MainTex", new Vector2(0f, 1f - (float)f / FireFrames - 15.5f / h));
        }

        /// <summary>清掉所有闪白标记（重开一局时用）。</summary>
        public void ClearFlash()
        {
            if (_flashing == null)
                return;
            for (int x = 0; x < Grid.SizeX; x++)
                for (int y = 0; y < Grid.SizeY; y++)
                    for (int z = 0; z < Grid.SizeZ; z++)
                        if (_flashing[x, y, z])
                            SetCellFlash(x, y, z, false);
        }

        /// <summary>
        /// 纯白材质。克隆 BorderMat 而不是 Shader.Find：BorderMat 是 Resources 里的资源、
        /// 引用了内置 Unlit/Color，构建版才会把那个 shader 打进包；
        /// 只调 Shader.Find 的话，构建版里往往拿到 null（shader 没有任何资源引用会被裁掉）。
        /// </summary>
        Material FlashMaterial
        {
            get
            {
                if (_flashMaterial != null)
                    return _flashMaterial;

                Material baseMat = Resources.Load<Material>("BorderMat");
                if (baseMat != null)
                {
                    _flashMaterial = new Material(baseMat);
                }
                else
                {
                    Shader shader = Shader.Find("Unlit/Color");
                    if (shader != null)
                        _flashMaterial = new Material(shader);
                }

                if (_flashMaterial != null)
                    _flashMaterial.color = Color.white;
                return _flashMaterial;
            }
        }

        /// <summary>
        /// 沙子塌落这类"整格位移"：把 from 格的显示对象搬到 to 格，
        /// 视觉上还留在 from，下一帧开始自己滑过去。
        /// </summary>
        /// <summary>落定时把方块当时的朝向记到这一格：旋转过的方块落下去要保持下落时的朝向。</summary>
        public void SetCellRotation(int x, int y, int z, Quaternion rotation)
        {
            if (!InCells(x, y, z))
                return;
            GameObject go = _cells[x, y, z];
            if (go == null)
                return;
            // 这一格如果装的是流体（网格里没有方块、但有水量），朝向要丢掉：
            // 水是矮方块，随便一点角度都会让它变成竖着的切片。
            bool isFluid = Grid.Get(x, y, z) <= 0 && Fluids.AmountAt(x, y, z) > 0;
            go.transform.rotation = CellRotation(isFluid, rotation);
        }

        /// <summary>
        /// 某一格该用的朝向。流体一律不转——它比正方体矮，任何角度都会让它立起来变成竖切片。
        /// </summary>
        public static Quaternion CellRotation(bool isFluid, Quaternion rotation)
        {
            return isFluid ? Quaternion.identity : rotation;
        }

        /// <summary>当前方块的目标朝向（四元数）。</summary>
        public Quaternion PieceRotationQuaternion
        {
            get { return _pieceRotTarget; }
        }

        public void ApplyGravityMoves(List<GravityStep> moves)
        {
            if (moves == null || moves.Count == 0)
                return;

            // 从下往上搬，避免同一柱里互相覆盖
            moves.Sort((a, b) => a.ToY.CompareTo(b.ToY));
            for (int i = 0; i < moves.Count; i++)
            {
                GravityStep m = moves[i];
                if (!InCells(m.FromX, m.FromY, m.FromZ) || !InCells(m.ToX, m.ToY, m.ToZ))
                    continue;
                GameObject go = _cells[m.FromX, m.FromY, m.FromZ];
                if (go == null)
                    continue;
                // 目标格里要是还挂着一个显示对象（比如刚被压碎、画面还没来得及清的玻璃），
                // 直接覆盖会把它的引用顶丢——对象还 active 在场上，就成了擦不掉的残影。
                // 搬进来之前先把它收掉，格位表里不丢东西。
                GameObject prev = _cells[m.ToX, m.ToY, m.ToZ];
                if (prev != null && prev != go && prev.activeSelf)
                    prev.SetActive(false);
                _cells[m.ToX, m.ToY, m.ToZ] = go;
                _cells[m.FromX, m.FromY, m.FromZ] = null;
                _cellVisual[m.ToX, m.ToY, m.ToZ] = new Vector3(m.FromX, m.FromY, m.FromZ);
                go.transform.position = _cellVisual[m.ToX, m.ToY, m.ToZ];
                _settled[m.ToX, m.ToY, m.ToZ] = false;   // 从原来那一格滑下来
            }
        }

        /// <summary>
        /// 行消除之后整堆内容会往下补。为了让它是"滑下来"而不是原地换一张贴图，
        /// 先给每个已经有内容的格子一个视觉起点（比目标高 n 行），随后 RefreshAll 换贴图，
        /// 下一帧开始 Update 会把它们从高处追到自己的格子里。
        /// </summary>
        public void BeginCollapse(List<int> clearedRows)
        {
            if (clearedRows == null || clearedRows.Count == 0)
                return;

            for (int x = 0; x < Grid.SizeX; x++)
                for (int y = 0; y < Grid.SizeY; y++)
                    for (int z = 0; z < Grid.SizeZ; z++)
                    {
                        if (_cells[x, y, z] == null)
                            continue;
                        int below = 0;
                        for (int i = 0; i < clearedRows.Count; i++)
                            if (clearedRows[i] < y)
                                below++;
                        if (below > 0)
                        {
                            _cellVisual[x, y, z] = new Vector3(x, y + below, z);
                            _settled[x, y, z] = false;   // 等下从高处追下来
                        }
                    }
        }

        bool InCells(int x, int y, int z)
        {
            return x >= 0 && y >= 0 && z >= 0 && x < Grid.SizeX && y < Grid.SizeY && z < Grid.SizeZ;
        }

        public void RefreshPiece(Piece piece)
        {
            if (piece == null)
            {
                HidePiece();
                return;
            }

            int n = piece.Cells.Length;
            EnsurePieceCapacity(n);
            // 朝向直接取逻辑层算好的，不做账：三组旋转键怎么按，这里都不会累积误差
            _pieceRotTarget = piece.Rot.ToQuaternion();

            int idx = piece.BlockId - 1;
            if (idx < 0 || idx >= _meshes.Length)
                idx = 0;
            for (int i = 0; i < _pieceCells.Length; i++)
            {
                GameObject go = _pieceCells[i];
                if (go == null) continue;
                if (i < n)
                {
                    _pieceTarget[i] = piece.WorldCell(i);
                    go.SetActive(true);
                    go.GetComponent<MeshFilter>().sharedMesh = _meshes[idx];
                    go.GetComponent<MeshRenderer>().sharedMaterial = _materials[idx];
                }
                else
                {
                    go.SetActive(false);
                }
            }
        }

        /// <summary>新方块生成 / 读档：直接出现在自己的位置上，不要从上个方块滑过来。</summary>
        public void SnapPiece(Piece piece)
        {
            if (piece == null)
            {
                HidePiece();
                return;
            }
            int n = piece.Cells.Length;
            EnsurePieceCapacity(n);
            for (int i = 0; i < _pieceCells.Length; i++)
            {
                if (i >= n) continue;
                _pieceTarget[i] = piece.WorldCell(i);
                _pieceVisual[i] = _pieceTarget[i];
                if (_pieceCells[i] != null)
                    _pieceCells[i].transform.position = _pieceVisual[i];
            }
            _pieceRotTarget = piece.Rot.ToQuaternion();
            _pieceRot = _pieceRotTarget;
        }

        /// <summary>
        /// 方块转了个 90°。视图层的朝向现在是每帧跟着 Piece.Rot 走的（见 RefreshPiece），
        /// 所以这里只是留着给调试看，不用再自己累加角度。
        /// </summary>
        public void RotatePieceVisual(RotationAxis axis, bool clockwise)
        {
        }

        void HidePiece()
        {
            if (_pieceCells == null)
                return;
            for (int i = 0; i < _pieceCells.Length; i++)
                if (_pieceCells[i] != null)
                    _pieceCells[i].SetActive(false);
        }

        // ---------- 落点提示框 ----------

        /// <summary>框的半边长。方块自己比 1 格大一点点（0.52），框要再大一圈才压在它表面上。</summary>
        const float GhostHalf = 0.56f;
        /// <summary>框线的粗细。</summary>
        const float GhostThick = 0.055f;

        static Mesh _ghostFrame;

        /// <summary>
        /// 落点框的网格：一个立方体的 12 条棱，每条是一根细长方块，全场共用一份。
        ///
        /// 没用 MeshTopology.Lines 画线：那种线宽在 Windows 上恒为 1 像素，
        /// 场地一远就细得看不见。用有厚度的条拼能吃上加色材质，看起来是发光的。
        /// </summary>
        public static Mesh GhostFrame
        {
            get
            {
                if (_ghostFrame == null)
                    _ghostFrame = BuildGhostFrame(GhostHalf, GhostThick);
                return _ghostFrame;
            }
        }

        /// <summary>造一个空心线框（12 根棱）。自检直接调它，所以做成 public static。</summary>
        public static Mesh BuildGhostFrame(float half, float thick)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();

            for (int axis = 0; axis < 3; axis++)
            {
                int b = (axis + 1) % 3;
                int c = (axis + 2) % 3;
                for (int sb = -1; sb <= 1; sb += 2)
                {
                    for (int sc = -1; sc <= 1; sc += 2)
                    {
                        // 这根棱沿 axis 方向躺满，在另外两个轴上卡在立方体的角上
                        var center = Vector3.zero;
                        var size = Vector3.zero;
                        center[b] = sb * half;
                        center[c] = sc * half;
                        size[axis] = half * 2f;
                        size[b] = thick;
                        size[c] = thick;
                        AddBar(verts, tris, center, size);
                    }
                }
            }

            var mesh = new Mesh();
            mesh.name = "GhostFrame";
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            return mesh;
        }

        /// <summary>往网格里加一根长方体（中心 + 尺寸）。</summary>
        static void AddBar(List<Vector3> verts, List<int> tris, Vector3 center, Vector3 size)
        {
            Vector3 h = size * 0.5f;
            int first = verts.Count;
            for (int i = 0; i < 8; i++)
            {
                verts.Add(center + new Vector3(
                    (i & 1) == 0 ? -h.x : h.x,
                    (i & 2) == 0 ? -h.y : h.y,
                    (i & 4) == 0 ? -h.z : h.z));
            }

            // 六个面的四个角（顶点编号：bit0 = +x，bit1 = +y，bit2 = +z）
            int[] quads =
            {
                0, 2, 6, 4,   // -x
                1, 5, 7, 3,   // +x
                0, 4, 5, 1,   // -y
                2, 3, 7, 6,   // +y
                0, 1, 3, 2,   // -z
                4, 6, 7, 5,   // +z
            };
            for (int f = 0; f < 6; f++)
            {
                int a = first + quads[f * 4];
                int b2 = first + quads[f * 4 + 1];
                int c = first + quads[f * 4 + 2];
                int d = first + quads[f * 4 + 3];
                tris.Add(a); tris.Add(b2); tris.Add(c);
                tris.Add(a); tris.Add(c); tris.Add(d);
            }
        }

        Material _ghostMaterial;

        /// <summary>
        /// 落点框用的青色发光材质。克隆 GridGlowMat（它引用了 GlowAdditive 这个加色 shader），
        /// 和 FireMat 同理：构建版里裸 Shader.Find 常常拿到 null。
        /// </summary>
        public Material GhostMaterial
        {
            get
            {
                if (_ghostMaterial != null)
                    return _ghostMaterial;

                Material baseMat = Resources.Load<Material>("GridGlowMat");
                if (baseMat != null)
                    _ghostMaterial = new Material(baseMat);
                else
                {
                    Shader sh = Shader.Find("MinecraftTetris/GlowAdditive");
                    if (sh != null)
                        _ghostMaterial = new Material(sh);
                }

                if (_ghostMaterial != null)
                    _ghostMaterial.color = new Color(0.35f, 0.92f, 1f, 0.55f);
                return _ghostMaterial;
            }
        }

        /// <summary>
        /// 把落点框摆到 (ox, oy, oz) 这个原点上，框的格子照方块的形状来 ——
        /// 一格一个方框，合起来就是「这一块落下去会占哪儿」。
        /// </summary>
        public void ShowGhost(Piece piece, int ox, int oy, int oz)
        {
            if (_ghostRoot == null || piece == null)
            {
                HideGhost();
                return;
            }

            int n = piece.Cells.Length;
            EnsureGhostCapacity(n);
            Mesh frame = GhostFrame;
            Material mat = GhostMaterial;

            for (int i = 0; i < _ghostCells.Length; i++)
            {
                GameObject go = _ghostCells[i];
                if (go == null)
                    continue;
                if (i >= n)
                {
                    go.SetActive(false);
                    continue;
                }

                Vector3Int off = piece.CellOffset(i);
                go.transform.position = new Vector3(ox + off.x, oy + off.y, oz + off.z);
                go.GetComponent<MeshFilter>().sharedMesh = frame;
                go.GetComponent<MeshRenderer>().sharedMaterial = mat;
                go.SetActive(true);
            }
        }

        /// <summary>藏起落点框（关掉开关、暂停、游戏结束时用）。</summary>
        public void HideGhost()
        {
            if (_ghostCells == null)
                return;
            for (int i = 0; i < _ghostCells.Length; i++)
                if (_ghostCells[i] != null)
                    _ghostCells[i].SetActive(false);
        }

        void EnsureGhostCapacity(int n)
        {
            if (_ghostCells != null && _ghostCells.Length >= n)
                return;

            int old = _ghostCells == null ? 0 : _ghostCells.Length;
            int grown = Mathf.Max(n, old + 4);
            var cells = new GameObject[grown];
            for (int i = 0; i < grown; i++)
            {
                if (i < old)
                {
                    cells[i] = _ghostCells[i];
                    continue;
                }
                cells[i] = NewCubeObject("GhostCell" + i);
                cells[i].transform.SetParent(_ghostRoot, false);
                cells[i].SetActive(false);
            }
            _ghostCells = cells;
        }

        void EnsurePieceCapacity(int n)
        {
            if (_pieceCells != null && _pieceCells.Length >= n)
                return;

            int oldCount = _pieceCells == null ? 0 : _pieceCells.Length;
            int grown = Mathf.Max(n, oldCount + 4);
            var newCells = new GameObject[grown];
            var newVisual = new Vector3[grown];
            var newTarget = new Vector3[grown];
            for (int i = 0; i < grown; i++)
            {
                if (i < oldCount)
                {
                    newCells[i] = _pieceCells[i];
                    newVisual[i] = _pieceVisual[i];
                    newTarget[i] = _pieceTarget[i];
                }
                else
                {
                    newCells[i] = NewCubeObject("PieceCell" + i);
                    newCells[i].transform.SetParent(_pieceRoot, false);
                    newCells[i].SetActive(false);
                }
            }
            _pieceCells = newCells;
            _pieceVisual = newVisual;
            _pieceTarget = newTarget;
        }

        GameObject NewCubeObject(string name)
        {
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>();
            return go;
        }
    }
}
