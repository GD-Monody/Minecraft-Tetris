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

        float _pieceYaw;
        float _pieceYawTarget;

        // 追赶速度：越大越快贴到位。16 大约 0.1 秒走完 80%
        const float Smooth = 16f;

        public Grid Grid { get; private set; }
        public float CellSize = 1f;

        public void Setup(Grid grid)
        {
            Grid = grid;
            _cells = new GameObject[grid.SizeX, grid.SizeY, grid.SizeZ];
            _cellVisual = new Vector3[grid.SizeX, grid.SizeY, grid.SizeZ];
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
            }
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
            _pieceYaw = Mathf.LerpAngle(_pieceYaw, _pieceYawTarget, k * 1.4f);
            Quaternion rot = Quaternion.Euler(0f, _pieceYaw, 0f);
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
            if (blockId <= 0)
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
            }

            int idx = blockId - 1;
            go.SetActive(true);
            go.transform.position = _cellVisual[x, y, z];
            go.GetComponent<MeshFilter>().sharedMesh = _meshes[idx];
            go.GetComponent<MeshRenderer>().sharedMaterial = _materials[idx];
        }

        /// <summary>
        /// 沙子塌落这类"整格位移"：把 from 格的显示对象搬到 to 格，
        /// 视觉上还留在 from，下一帧开始自己滑过去。
        /// </summary>
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
            _pieceYaw = 0f;
            _pieceYawTarget = 0f;
        }

        /// <summary>方块转了个 90°：视觉上让格子转过去。</summary>
        public void RotatePieceVisual(bool clockwise)
        {
            _pieceYawTarget += clockwise ? 90f : -90f;
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
