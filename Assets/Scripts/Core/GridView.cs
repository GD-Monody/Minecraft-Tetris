using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>
    /// 把 Grid 和当前方块画出来。v0.1 用每格一个 GameObject，简单直观；
    /// 后面格子多了再换合并网格。
    /// </summary>
    public class GridView : MonoBehaviour
    {
        GameObject[,,] _cells;
        GameObject[] _pieceCells;
        Transform _pieceRoot;

        Mesh[] _meshes;
        Material[] _materials;

        public Grid Grid { get; private set; }
        public float CellSize = 1f;

        public void Setup(Grid grid)
        {
            Grid = grid;
            _cells = new GameObject[grid.SizeX, grid.SizeY, grid.SizeZ];
            BuildResources();

            _pieceRoot = new GameObject("Piece").transform;
            _pieceRoot.SetParent(transform, false);
            _pieceCells = new GameObject[4];
            for (int i = 0; i < 4; i++)
            {
                _pieceCells[i] = NewCubeObject("PieceCell" + i);
                _pieceCells[i].transform.SetParent(_pieceRoot, false);
                _pieceCells[i].SetActive(false);
            }
        }

        void BuildResources()
        {
            int count = BlockCatalog.Pieces.Length;
            _meshes = new Mesh[count];
            _materials = new Material[count];
            for (int i = 0; i < count; i++)
            {
                BlockDef def = BlockCatalog.Pieces[i];
                _meshes[i] = BlockMesh.Build(def);
                _materials[i] = BlockMesh.GetMaterial(def);
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
            }

            int idx = blockId - 1;
            go.SetActive(true);
            go.transform.position = new Vector3(x, y, z);
            go.GetComponent<MeshFilter>().sharedMesh = _meshes[idx];
            go.GetComponent<MeshRenderer>().sharedMaterial = _materials[idx];
        }

        public void RefreshPiece(Piece piece)
        {
            if (piece == null)
            {
                HidePiece();
                return;
            }

            int idx = (int)piece.Type;
            for (int i = 0; i < 4; i++)
            {
                GameObject go = _pieceCells[i];
                go.SetActive(true);
                go.transform.position = piece.WorldCell(i);
                go.GetComponent<MeshFilter>().sharedMesh = _meshes[idx];
                go.GetComponent<MeshRenderer>().sharedMaterial = _materials[idx];
            }
        }

        void HidePiece()
        {
            for (int i = 0; i < _pieceCells.Length; i++)
                _pieceCells[i].SetActive(false);
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
