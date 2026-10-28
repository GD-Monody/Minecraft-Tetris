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

        GameObject[] _pieceCells;
        Vector3[] _pieceVisual;
        Vector3[] _pieceTarget;
        Transform _pieceRoot;

        /// <summary>正在下落的那个方块的父节点（调试用）。</summary>
        public Transform PieceRoot { get { return _pieceRoot; } }

        Mesh[] _meshes;
        Material[] _materials;

        /// <summary>TNT 引信闪白用的纯白材质（Unlit/Color，不吃贴图）。</summary>
        Material _flashMaterial;

        /// <summary>哪些格子现在该画成白闪（点着的 TNT）。SetCell 重建时也要照着它选材质。</summary>
        bool[,,] _flashing;

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
            _flashing = new bool[grid.SizeX, grid.SizeY, grid.SizeZ];
            BuildResources();

            _pieceRoot = new GameObject("Piece").transform;
            _pieceRoot.SetParent(transform, false);
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

            // 落定的方块：朝自己的格子追，追上后就停在格子上
            for (int x = 0; x < Grid.SizeX; x++)
                for (int y = 0; y < Grid.SizeY; y++)
                    for (int z = 0; z < Grid.SizeZ; z++)
                    {
                        GameObject go = _cells[x, y, z];
                        if (go == null || !go.activeSelf)
                            continue;
                        Vector3 target = new Vector3(x, y, z);
                        _cellVisual[x, y, z] = Vector3.Lerp(_cellVisual[x, y, z], target, k);
                        go.transform.position = _cellVisual[x, y, z];
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
            }
            else if (!go.activeSelf)
            {
                // 这一格刚由空变成有：直接出现在格子里。
                // 不复用上一次留下的视觉位置，否则会看到它从别处滑进来（硬降后尤其明显）。
                _cellVisual[x, y, z] = new Vector3(x, y, z);
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
                _cells[m.ToX, m.ToY, m.ToZ] = go;
                _cells[m.FromX, m.FromY, m.FromZ] = null;
                _cellVisual[m.ToX, m.ToY, m.ToZ] = new Vector3(m.FromX, m.FromY, m.FromZ);
                go.transform.position = _cellVisual[m.ToX, m.ToY, m.ToZ];
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
                            _cellVisual[x, y, z] = new Vector3(x, y + below, z);
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
