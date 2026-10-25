using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>
    /// 三维网格。初版只用 z = 1，但数据层按三个轴存，后面开深度不用改结构。
    /// </summary>
    public class Grid
    {
        public readonly int SizeX;
        public readonly int SizeY;
        public readonly int SizeZ;

        // 0 表示空，>0 是方块类型索引 + 1（存 int 方便以后加方块种类）
        readonly int[,,] _cells;

        public Grid(int sizeX, int sizeY, int sizeZ)
        {
            SizeX = Mathf.Max(1, sizeX);
            SizeY = Mathf.Max(1, sizeY);
            SizeZ = Mathf.Max(1, sizeZ);
            _cells = new int[SizeX, SizeY, SizeZ];
        }

        public bool InBounds(int x, int y, int z)
        {
            return x >= 0 && x < SizeX && y >= 0 && y < SizeY && z >= 0 && z < SizeZ;
        }

        public bool IsFree(int x, int y, int z)
        {
            return InBounds(x, y, z) && _cells[x, y, z] == 0;
        }

        public int Get(int x, int y, int z)
        {
            return _cells[x, y, z];
        }

        /// <summary>按固定顺序导出所有格子，存档用。</summary>
        public int[] Export()
        {
            var data = new int[SizeX * SizeY * SizeZ];
            int i = 0;
            for (int z = 0; z < SizeZ; z++)
                for (int y = 0; y < SizeY; y++)
                    for (int x = 0; x < SizeX; x++)
                        data[i++] = _cells[x, y, z];
            return data;
        }

        /// <summary>导入存档格子，长度不对就忽略。</summary>
        public void Import(int[] data)
        {
            if (data == null || data.Length != _cells.Length)
                return;
            int i = 0;
            for (int z = 0; z < SizeZ; z++)
                for (int y = 0; y < SizeY; y++)
                    for (int x = 0; x < SizeX; x++)
                        _cells[x, y, z] = data[i++];
        }

        public void Set(int x, int y, int z, int block)
        {
            _cells[x, y, z] = block;
        }

        public void Clear()
        {
            System.Array.Clear(_cells, 0, _cells.Length);
        }

        /// <summary>
        /// 检测并消除满行。z = 1 时一层就是一行，直接按层处理；
        /// z > 1 时按 (y, z) 逐行检查（行消模式）。
        /// </summary>
        public int ClearFullLines()
        {
            return ClearFullLinesDetailed().Count;
        }

        /// <summary>消除满行，返回被清掉的行号列表（粒子效果要按行炸）。</summary>
        public System.Collections.Generic.List<int> ClearFullLinesDetailed()
        {
            var rows = new System.Collections.Generic.List<int>();
            for (int y = 0; y < SizeY; y++)
            {
                while (IsLayerFull(y))
                {
                    DropLayerAbove(y);
                    rows.Add(y);
                }
            }
            return rows;
        }

        /// <summary>行消：某个 (y, z) 上 x 方向一行是否填满。</summary>
        public bool IsRowFullPublic(int y, int z)
        {
            for (int x = 0; x < SizeX; x++)
                if (_cells[x, y, z] == 0)
                    return false;
            return true;
        }

        /// <summary>取这一行里第一个非空方块的编号，没有则返回 0。</summary>
        public int FirstBlockInRow(int y, int z)
        {
            for (int x = 0; x < SizeX; x++)
                if (_cells[x, y, z] != 0)
                    return _cells[x, y, z];
            return 0;
        }

        /// <summary>
        /// 行消模式：把所有填满的 (y, z) 行清掉，上方同 z 的格子下落。返回清除行数。
        /// </summary>
        public int ClearFullRows()
        {
            int cleared = 0;
            for (int z = 0; z < SizeZ; z++)
                for (int y = 0; y < SizeY; y++)
                    while (IsRowFull(y, z))
                    {
                        DropRowAbove(y, z);
                        cleared++;
                    }
            return cleared;
        }

        void DropRowAbove(int y, int z)
        {
            for (int yy = y; yy < SizeY - 1; yy++)
                for (int x = 0; x < SizeX; x++)
                    _cells[x, yy, z] = _cells[x, yy + 1, z];

            for (int x = 0; x < SizeX; x++)
                _cells[x, SizeY - 1, z] = 0;
        }

        bool IsRowFull(int y, int z)
        {
            for (int x = 0; x < SizeX; x++)
                if (_cells[x, y, z] == 0)
                    return false;
            return true;
        }

        /// <summary>
        /// 层消：整个 x × z 平面填满。
        /// </summary>
        public int ClearFullLayers()
        {
            int cleared = 0;
            for (int y = 0; y < SizeY; y++)
            {
                while (IsLayerFull(y))
                {
                    DropLayerAbove(y);
                    cleared++;
                }
            }
            return cleared;
        }

        bool IsLayerFull(int y)
        {
            for (int x = 0; x < SizeX; x++)
                for (int z = 0; z < SizeZ; z++)
                    if (_cells[x, y, z] == 0)
                        return false;
            return true;
        }

        /// <summary>这一层（y 高度上的 x × z 平面）是否填满。</summary>
        public bool IsLayerFullPublic(int y)
        {
            return IsLayerFull(y);
        }

        /// <summary>取这一层里第一个非空方块的编号，没有则返回 0。</summary>
        public int FirstBlockInLayer(int y)
        {
            for (int x = 0; x < SizeX; x++)
                for (int z = 0; z < SizeZ; z++)
                    if (_cells[x, y, z] != 0)
                        return _cells[x, y, z];
            return 0;
        }

        void DropLayerAbove(int y)
        {
            for (int yy = y; yy < SizeY - 1; yy++)
                for (int x = 0; x < SizeX; x++)
                    for (int z = 0; z < SizeZ; z++)
                        _cells[x, yy, z] = _cells[x, yy + 1, z];

            for (int x = 0; x < SizeX; x++)
                for (int z = 0; z < SizeZ; z++)
                    _cells[x, SizeY - 1, z] = 0;
        }
    }
}
