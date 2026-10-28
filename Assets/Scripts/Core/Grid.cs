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

        /// <summary>
        /// 满行判定里"这一格算不算满"的额外来源。水不写进网格，
        /// 但攒满一格的应该能凑成一行，所以由 Bootstrap 注入一条判断。
        /// </summary>
        public static System.Func<int, int, int, bool> CountsAsFilled;

        /// <summary>某格被消掉时通知一声（用来把那一格的水也清掉）。</summary>
        public static System.Action<int, int, int> OnCellCleared;

        bool Filled(int x, int y, int z)
        {
            if (_cells[x, y, z] != 0)
                return true;
            return CountsAsFilled != null && CountsAsFilled(x, y, z);
        }

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
            if (x < 0 || x >= SizeX || z < 0 || z >= SizeZ || y < 0)
                return false;

            // 场地顶部以上算空：新方块在框外生成、往下掉进来的时候要能穿过这一段。
            if (y >= SizeY)
                return true;

            return _cells[x, y, z] == 0;
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
                if (!Filled(x, y, z))
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
                        if (OnCellCleared != null)
                            for (int x = 0; x < SizeX; x++)
                                OnCellCleared(x, y, z);
                        DropRowAbove(y, z);
                        cleared++;
                    }
            return cleared;
        }

        /// <summary>
        /// 行消：把 y 上方同一 z 的方块整体下落一格，但基岩留在原位。
        ///
        /// 原来是「整行往上搬一格」，碰到基岩有两个问题：基岩被一起搬走（它该钉住），
        /// 基岩上方的方块又会直接穿过它落下来（它该被托住）。
        /// 改成逐格判断「下面那格空不空」：基岩自己不落（它占着格子），
        /// 上方的方块就自然停在它上面。
        /// 从下往上扫：先落低的，高的那一格才看得见刚空出来的位置。
        /// </summary>
        void DropRowAbove(int y, int z)
        {
            // 先把被消的这一行清掉。原来的写法是「把上一行整体搬下来覆盖它」，
            // 换成逐格下落之后没有东西覆盖了，得自己清 —— 不清的话这一行还是满的，
            // 调用方的 while (IsRowFull(y)) 会永远转下去。
            for (int x = 0; x < SizeX; x++)
                _cells[x, y, z] = 0;

            for (int yy = y + 1; yy < SizeY; yy++)
            {
                for (int x = 0; x < SizeX; x++)
                {
                    int id = _cells[x, yy, z];
                    if (id == 0 || BlockCatalog.IsAnchor(id))
                        continue;

                    // 下面是基岩或别的方块就落不下去，这一格和它上面的一摞都停住
                    if (_cells[x, yy - 1, z] != 0)
                        continue;

                    _cells[x, yy - 1, z] = id;
                    _cells[x, yy, z] = 0;
                }
            }
        }

        bool IsRowFull(int y, int z)
        {
            for (int x = 0; x < SizeX; x++)
                if (!Filled(x, y, z))
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
                    if (OnCellCleared != null)
                        for (int x = 0; x < SizeX; x++)
                            for (int z = 0; z < SizeZ; z++)
                                OnCellCleared(x, y, z);
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
                    if (!Filled(x, y, z))
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

        /// <summary>层消版的下落，规则和 DropRowAbove 一样：基岩钉住，上面的一摞停在它上面。</summary>
        void DropLayerAbove(int y)
        {
            // 同 DropRowAbove：被消的这一层要先自己清空
            for (int x = 0; x < SizeX; x++)
                for (int z = 0; z < SizeZ; z++)
                    _cells[x, y, z] = 0;

            for (int yy = y + 1; yy < SizeY; yy++)
            {
                for (int x = 0; x < SizeX; x++)
                {
                    for (int z = 0; z < SizeZ; z++)
                    {
                        int id = _cells[x, yy, z];
                        if (id == 0 || BlockCatalog.IsAnchor(id))
                            continue;
                        if (_cells[x, yy - 1, z] != 0)
                            continue;

                        _cells[x, yy - 1, z] = id;
                        _cells[x, yy, z] = 0;
                    }
                }
            }
        }
    }
}
