using System.Collections.Generic;
using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>
    /// 结构方块：落定时记住周围一片的方块构造，被消除后把记住的构造还原出来，
    /// 还原完自己消失。还原是「先整体清空，再一格一格搭回来」的动画。
    ///
    /// 三个阶段：
    ///   记忆 —— 结构方块落定时拍一张快照，范围是以它为中心的 3×3×3（受场地深度限制）。
    ///   还原 —— 被消行 / 被炸掉时触发：先把范围内现有方块全清掉，再按快照一格一格放回。
    ///   收尾 —— 搭完之后把结构方块自己清掉，这条记忆也就没了。
    ///
    /// 记忆记的是**绝对坐标**：还原时不管中间发生了什么，那一片就恢复成记忆时的样子。
    /// </summary>
    public static class Structure
    {
        /// <summary>记忆范围的边长（以结构方块为中心，向各轴各扩 1 格）。</summary>
        public const int Span = 3;

        /// <summary>每搭一格之间的间隔（秒）。0.06 秒 → 9 格约 0.5 秒，27 格约 1.6 秒。</summary>
        public const float StepInterval = 0.06f;

        /// <summary>一条记忆：结构方块在哪儿、当时那一片是什么样子。</summary>
        public class Memory
        {
            /// <summary>中心格（结构方块自己所在的那格）。</summary>
            public int X, Y, Z;

            /// <summary>记忆范围的起点（已经裁到场地内）。</summary>
            public int MinX, MinY, MinZ;

            public int SizeX, SizeY, SizeZ;

            /// <summary>快照，按 (y * SizeZ + z) * SizeX + x 排。</summary>
            public int[] Blocks;
        }

        static readonly List<Memory> _memories = new List<Memory>();
        static readonly Queue<Memory> _pending = new Queue<Memory>();

        /// <summary>正在还原的那一条。</summary>
        static Memory _active;

        /// <summary>已经搭了几格 / 距离下一格还差多少秒。</summary>
        static int _step;
        static float _timer;

        /// <summary>搭好一格：视图层拿它把那一格画出来。</summary>
        public static event System.Action<int, int, int, int> CellRebuilt;

        /// <summary>清掉一格：视图层拿它把那一格擦掉。</summary>
        public static event System.Action<int, int, int> CellCleared;

        /// <summary>整条还原流程结束（结构方块自己消失了）：参数是它原来在哪儿。</summary>
        public static event System.Action<int, int, int> Finished;

        public static List<Memory> Memories { get { return _memories; } }

        /// <summary>现在有没有正在进行的还原（队列里也算）。</summary>
        public static bool Busy { get { return _active != null || _pending.Count > 0; } }

        /// <summary>快照里某个位置对应的是哪一格（给存档和自检用）。</summary>
        public static void CellOf(Memory m, int index, out int x, out int y, out int z)
        {
            x = m.MinX + index % m.SizeX;
            z = m.MinZ + (index / m.SizeX) % m.SizeZ;
            y = m.MinY + index / (m.SizeX * m.SizeZ);
        }

        static int IndexOf(Memory m, int x, int y, int z)
        {
            return ((y - m.MinY) * m.SizeZ + (z - m.MinZ)) * m.SizeX + (x - m.MinX);
        }

        /// <summary>快照里某一格记的是什么方块（0 = 空）。</summary>
        public static int BlockAt(Memory m, int x, int y, int z)
        {
            if (x < m.MinX || y < m.MinY || z < m.MinZ)
                return 0;
            if (x >= m.MinX + m.SizeX || y >= m.MinY + m.SizeY || z >= m.MinZ + m.SizeZ)
                return 0;
            return m.Blocks[IndexOf(m, x, y, z)];
        }

        /// <summary>方块 id 是不是结构方块。</summary>
        public static bool IsStructure(int blockId)
        {
            return blockId > 0 && blockId == BlockCatalog.StructureId;
        }

        // ---------------------------------------------------------------- 记忆

        /// <summary>
        /// 拍一张快照：以 (x, y, z) 为中心，范围是各轴 ±1（裁到场地内）。
        /// 同一格只记一条，重复落定不会覆盖。
        /// </summary>
        public static Memory Remember(Grid grid, int x, int y, int z)
        {
            if (grid == null || !grid.InBounds(x, y, z))
                return null;

            for (int i = 0; i < _memories.Count; i++)
            {
                if (_memories[i].X == x && _memories[i].Y == y && _memories[i].Z == z)
                    return _memories[i];
            }

            int minX = Mathf.Max(0, x - Span / 2);
            int minY = Mathf.Max(0, y - Span / 2);
            int minZ = Mathf.Max(0, z - Span / 2);
            int maxX = Mathf.Min(grid.SizeX - 1, x + Span / 2);
            int maxY = Mathf.Min(grid.SizeY - 1, y + Span / 2);
            int maxZ = Mathf.Min(grid.SizeZ - 1, z + Span / 2);

            var m = new Memory
            {
                X = x, Y = y, Z = z,
                MinX = minX, MinY = minY, MinZ = minZ,
                SizeX = maxX - minX + 1,
                SizeY = maxY - minY + 1,
                SizeZ = maxZ - minZ + 1,
            };

            m.Blocks = new int[m.SizeX * m.SizeY * m.SizeZ];
            for (int yy = 0; yy < m.SizeY; yy++)
            {
                for (int zz = 0; zz < m.SizeZ; zz++)
                {
                    for (int xx = 0; xx < m.SizeX; xx++)
                    {
                        m.Blocks[(yy * m.SizeZ + zz) * m.SizeX + xx] =
                            grid.Get(minX + xx, minY + yy, minZ + zz);
                    }
                }
            }

            _memories.Add(m);
            return m;
        }

        /// <summary>一整块落定的方块里有结构方块的话，每一格都记一份。</summary>
        public static void RememberFromPiece(Grid grid, Piece piece)
        {
            if (grid == null || piece == null)
                return;
            if (!IsStructure(piece.BlockId))
                return;

            for (int i = 0; i < piece.Cells.Length; i++)
            {
                Vector3Int c = piece.WorldCell(i);
                if (grid.InBounds(c.x, c.y, c.z))
                    Remember(grid, c.x, c.y, c.z);
            }
        }

        // ---------------------------------------------------------------- 触发

        /// <summary>
        /// 某一格被消掉了。调用时那一格还没被搬走覆盖，所以还能读到它原本是什么。
        /// 是结构方块就排一次还原。
        /// </summary>
        public static bool NotifyCleared(Grid grid, int x, int y, int z)
        {
            if (grid == null || !grid.InBounds(x, y, z))
                return false;

            if (!IsStructure(grid.Get(x, y, z)))
                return false;

            return Trigger(grid, x, y, z);
        }

        /// <summary>直接触发一次还原（调试用）。</summary>
        public static bool Trigger(Grid grid, int x, int y, int z)
        {
            for (int i = 0; i < _memories.Count; i++)
            {
                Memory m = _memories[i];
                if (m.X == x && m.Y == y && m.Z == z)
                {
                    _pending.Enqueue(m);
                    return true;
                }
            }
            return false;
        }

        // ---------------------------------------------------------------- 还原

        /// <summary>推进还原动画。挂在主循环的 Tick 里。</summary>
        public static void Tick(Grid grid, float dt)
        {
            if (grid == null)
                return;

            if (_active == null)
            {
                if (_pending.Count == 0)
                    return;

                _active = _pending.Dequeue();
                ClearRegion(grid, _active);
                _step = 0;
                _timer = 0f;
            }

            _timer += dt;
            while (_timer >= StepInterval)
            {
                _timer -= StepInterval;
                if (!RebuildStep(grid, _active))
                {
                    Finish(grid, _active);
                    break;
                }
            }
        }

        /// <summary>把记忆范围内现有的方块（和流体）全清掉。</summary>
        static void ClearRegion(Grid grid, Memory m)
        {
            for (int yy = 0; yy < m.SizeY; yy++)
            {
                for (int zz = 0; zz < m.SizeZ; zz++)
                {
                    for (int xx = 0; xx < m.SizeX; xx++)
                    {
                        int gx = m.MinX + xx;
                        int gy = m.MinY + yy;
                        int gz = m.MinZ + zz;

                        if (grid.Get(gx, gy, gz) == 0 && Fluids.AmountAt(gx, gy, gz) <= 0)
                            continue;

                        grid.Set(gx, gy, gz, 0);
                        Fluids.ClearCell(gx, gy, gz);
                        if (CellCleared != null)
                            CellCleared(gx, gy, gz);
                    }
                }
            }
        }

        /// <summary>搭一格。返回 false 表示整片搭完了。</summary>
        static bool RebuildStep(Grid grid, Memory m)
        {
            int total = m.SizeX * m.SizeY * m.SizeZ;
            if (_step >= total)
                return false;

            int index = _step;
            _step++;

            int x, y, z;
            CellOf(m, index, out x, out y, out z);

            int id = m.Blocks[index];
            if (id <= 0)
                return true;   // 快照里这格是空的，不用搭

            grid.Set(x, y, z, id);
            if (CellRebuilt != null)
                CellRebuilt(x, y, z, id);
            return true;
        }

        /// <summary>搭完了：结构方块自己消失，这条记忆也就没了。</summary>
        static void Finish(Grid grid, Memory m)
        {
            grid.Set(m.X, m.Y, m.Z, 0);
            if (CellCleared != null)
                CellCleared(m.X, m.Y, m.Z);

            _memories.Remove(m);
            _active = null;
            _step = 0;
            _timer = 0f;

            if (Finished != null)
                Finished(m.X, m.Y, m.Z);
        }

        /// <summary>清掉全部记忆和队列（新开局、读档时用）。</summary>
        public static void Reset()
        {
            _memories.Clear();
            _pending.Clear();
            _active = null;
            _step = 0;
            _timer = 0f;
        }

        // ---------------------------------------------------------------- 存档

        /// <summary>
        /// 把记忆拍平成 int 数组：每条
        /// cx, cy, cz, minX, minY, minZ, sizeX, sizeY, sizeZ, 块数, 然后是块 id。
        /// 起点 min* 必须一起存：范围是「中心 ±1 再裁到场地内」，
        /// 贴边时尺寸会变成 2，光靠中心和尺寸反推起点会算错半格。
        /// </summary>
        public static int[] SerializeMemories()
        {
            var list = new List<int>();
            for (int i = 0; i < _memories.Count; i++)
            {
                Memory m = _memories[i];
                list.Add(m.X);
                list.Add(m.Y);
                list.Add(m.Z);
                list.Add(m.MinX);
                list.Add(m.MinY);
                list.Add(m.MinZ);
                list.Add(m.SizeX);
                list.Add(m.SizeY);
                list.Add(m.SizeZ);
                list.Add(m.Blocks.Length);
                for (int k = 0; k < m.Blocks.Length; k++)
                    list.Add(m.Blocks[k]);
            }
            return list.ToArray();
        }

        /// <summary>从存档里把记忆读回来。</summary>
        public static void DeserializeMemories(int[] data)
        {
            Reset();
            if (data == null)
                return;

            int i = 0;
            while (i + 9 < data.Length)
            {
                var m = new Memory
                {
                    X = data[i],
                    Y = data[i + 1],
                    Z = data[i + 2],
                    MinX = data[i + 3],
                    MinY = data[i + 4],
                    MinZ = data[i + 5],
                    SizeX = data[i + 6],
                    SizeY = data[i + 7],
                    SizeZ = data[i + 8],
                };
                int count = data[i + 9];
                i += 10;

                if (count <= 0 || i + count > data.Length)
                    break;

                m.Blocks = new int[count];
                for (int k = 0; k < count; k++)
                    m.Blocks[k] = data[i + k];
                i += count;

                _memories.Add(m);
            }
        }
    }
}
