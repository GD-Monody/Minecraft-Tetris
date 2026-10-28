using System.Collections.Generic;
using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>
    /// 结构方块：落定时把**整块场地**拍一张快照，被消掉 / 炸掉时把整场还原成那一刻的样子，
    /// 还原完自己消失。
    ///
    /// 三个阶段：
    ///   记忆 —— 结构方块落定的瞬间记一条。范围是整块场地，不再只是身边那一圈。
    ///           一整块方块里可能有 4 格结构方块，但它们只记一条：任意一格被消掉就触发一次。
    ///   消失 —— 场上现在的方块从下往上一个接一个消失，把场地腾出来。
    ///   重建 —— 记忆里的方块从下往上一个接一个从上方掉下来，落回各自的格子。
    ///
    /// 记忆记的是**绝对坐标**：还原时不管中间发生了什么，整场都恢复成记忆时的样子。
    /// </summary>
    public static class Structure
    {
        /// <summary>消失阶段：每隔多久收掉一格（秒）。</summary>
        public const float ClearInterval = 0.035f;

        /// <summary>重建阶段：每隔多久掉下一格（秒）。</summary>
        public const float DropInterval = 0.05f;

        /// <summary>重建时方块从目标上方几格开始落。</summary>
        public const int DropHeight = 2;

        /// <summary>一个阶段最多演多久（秒）：方块特别多的时候一次处理多格，不让玩家干等。</summary>
        public const float MaxPhaseTime = 3f;

        /// <summary>存档格式标记。老存档没有这个前缀，第一个数是中心 X（≥ 0），不会撞上负数。</summary>
        const int SaveVersion = -2;

        /// <summary>一条记忆：结构方块在哪儿、当时整场是什么样子。</summary>
        public class Memory
        {
            /// <summary>触发锚点：这块结构方块落定时占的第一格（事件和日志用）。</summary>
            public int X, Y, Z;

            /// <summary>快照范围。新存档是整块场地；老存档是 3×3×3 的那一片。</summary>
            public int MinX, MinY, MinZ;

            public int SizeX, SizeY, SizeZ;

            /// <summary>快照，按 (y * SizeZ + z) * SizeX + x 排。</summary>
            public int[] Blocks;

            /// <summary>这块结构方块占的每一格，3 个一组 x, y, z。老存档只有锚点那一格。</summary>
            public int[] Cells;

            /// <summary>已经排队 / 正在还原，别重复入队。</summary>
            public bool Queued;

            /// <summary>场地被别的还原抹掉了，这条记忆作废。</summary>
            public bool Cancelled;
        }

        static readonly List<Memory> _memories = new List<Memory>();
        static readonly Queue<Memory> _pending = new Queue<Memory>();

        /// <summary>正在还原的那一条。</summary>
        static Memory _active;

        enum Phase { Clear, Drop }

        /// <summary>这一阶段要挨个处理的格子，4 个一组：x, y, z, 方块 id。</summary>
        static readonly List<int> _work = new List<int>();

        static Phase _phase;
        static int _cursor;     // 处理到第几格
        static int _count;      // 这一阶段一共多少格
        static int _batch;      // 一次处理几格（格子太多时 > 1）
        static float _timer;    // 距离下一次处理还差多少秒

        /// <summary>消失阶段收掉一格：视图层擦掉它，顺手撒一点碎块。</summary>
        public static event System.Action<int, int, int, int> CellVanished;

        /// <summary>重建阶段落下一格：视图层让它从上方掉回自己的格子。</summary>
        public static event System.Action<int, int, int, int> CellDropped;

        /// <summary>整条还原流程结束（结构方块自己消失了）：参数是它原来在哪儿。</summary>
        public static event System.Action<int, int, int> Finished;

        public static List<Memory> Memories { get { return _memories; } }

        /// <summary>现在有没有正在进行的还原（队列里也算）。</summary>
        public static bool Busy { get { return _active != null || _pending.Count > 0; } }

        /// <summary>正处在重建阶段（消失阶段已经走完，方块正在一格一格掉回来）。</summary>
        public static bool InDropPhase { get { return _active != null && _phase == Phase.Drop; } }

        /// <summary>快照里某个下标对应的是场上哪一格（给存档和自检用）。</summary>
        public static void CellOf(Memory m, int index, out int x, out int y, out int z)
        {
            x = m.MinX + index % m.SizeX;
            z = m.MinZ + (index / m.SizeX) % m.SizeZ;
            y = m.MinY + index / (m.SizeX * m.SizeZ);
        }

        static int IndexOf(Memory m, int x, int y, int z)
        {
            int lx = x - m.MinX;
            int ly = y - m.MinY;
            int lz = z - m.MinZ;
            if (lx < 0 || ly < 0 || lz < 0)
                return -1;
            if (lx >= m.SizeX || ly >= m.SizeY || lz >= m.SizeZ)
                return -1;
            return (ly * m.SizeZ + lz) * m.SizeX + lx;
        }

        /// <summary>快照里某一格记的是什么方块（0 = 空）。</summary>
        public static int BlockAt(Memory m, int x, int y, int z)
        {
            int index = IndexOf(m, x, y, z);
            return index < 0 ? 0 : m.Blocks[index];
        }

        static bool CellInMemory(Memory m, int x, int y, int z)
        {
            if (m.Cells == null)
                return false;

            for (int i = 0; i + 2 < m.Cells.Length; i += 3)
            {
                if (m.Cells[i] == x && m.Cells[i + 1] == y && m.Cells[i + 2] == z)
                    return true;
            }
            return false;
        }

        /// <summary>方块 id 是不是结构方块。</summary>
        public static bool IsStructure(int blockId)
        {
            return blockId > 0 && blockId == BlockCatalog.StructureId;
        }

        // ---------------------------------------------------------------- 记忆

        /// <summary>
        /// 拍一张整场快照。cells 是这块结构方块占的格子（3 个一组 x, y, z），锚点取第一格。
        /// 越界的格子会被丢掉，一格都不剩就什么都不记。
        /// </summary>
        public static Memory RememberCells(Grid grid, int[] cells)
        {
            if (grid == null || cells == null)
                return null;

            var clean = new List<int>(cells.Length);
            for (int i = 0; i + 2 < cells.Length; i += 3)
            {
                int x = cells[i];
                int y = cells[i + 1];
                int z = cells[i + 2];
                if (!grid.InBounds(x, y, z))
                    continue;
                clean.Add(x);
                clean.Add(y);
                clean.Add(z);
            }

            if (clean.Count == 0)
                return null;

            // 这几格刚被结构方块占住，原来记在这几格上的记忆必然已经失效（那块早被消掉了）。
            for (int i = 0; i < clean.Count; i += 3)
                ForgetAt(clean[i], clean[i + 1], clean[i + 2], null);

            var m = new Memory
            {
                X = clean[0], Y = clean[1], Z = clean[2],
                MinX = 0, MinY = 0, MinZ = 0,
                SizeX = grid.SizeX, SizeY = grid.SizeY, SizeZ = grid.SizeZ,
                Cells = clean.ToArray(),
            };

            m.Blocks = new int[m.SizeX * m.SizeY * m.SizeZ];
            int k = 0;
            for (int yy = 0; yy < m.SizeY; yy++)
            {
                for (int zz = 0; zz < m.SizeZ; zz++)
                {
                    for (int xx = 0; xx < m.SizeX; xx++)
                        m.Blocks[k++] = grid.Get(m.MinX + xx, m.MinY + yy, m.MinZ + zz);
                }
            }

            _memories.Add(m);
            return m;
        }

        /// <summary>拍一张整场快照，只带一个格子（调试和自检用；正常游玩走 RememberFromPiece）。</summary>
        public static Memory Remember(Grid grid, int x, int y, int z)
        {
            if (grid == null || !grid.InBounds(x, y, z))
                return null;
            return RememberCells(grid, new[] { x, y, z });
        }

        /// <summary>一整块落定的方块里有结构方块的话，整块只记一条记忆。</summary>
        public static void RememberFromPiece(Grid grid, Piece piece)
        {
            if (grid == null || piece == null)
                return;
            if (!IsStructure(piece.BlockId))
                return;

            var cells = new List<int>(piece.Cells.Length * 3);
            for (int i = 0; i < piece.Cells.Length; i++)
            {
                Vector3Int c = piece.WorldCell(i);
                if (!grid.InBounds(c.x, c.y, c.z))
                    continue;
                cells.Add(c.x);
                cells.Add(c.y);
                cells.Add(c.z);
            }

            RememberCells(grid, cells.ToArray());
        }

        // ---------------------------------------------------------------- 触发

        /// <summary>
        /// 某一格被消掉了。调用时那一格还没被搬走覆盖，所以还能读到它原本是什么。
        /// 是结构方块就排一次还原（同一条记忆排队中不会重复入队）。
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
                if (m == _active || m.Cancelled || m.Queued)
                    continue;
                if (!(m.X == x && m.Y == y && m.Z == z) && !CellInMemory(m, x, y, z))
                    continue;

                m.Queued = true;
                _pending.Enqueue(m);
                return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- 还原

        /// <summary>推进还原动画。挂在主循环的 Tick 里。</summary>
        public static void Tick(Grid grid, float dt)
        {
            if (grid == null)
                return;

            while (_active == null && _pending.Count > 0)
            {
                Memory next = _pending.Dequeue();
                next.Queued = false;
                if (next.Cancelled)
                    continue;
                BeginRestore(grid, next);
            }

            if (_active == null)
                return;

            float interval = _phase == Phase.Clear ? ClearInterval : DropInterval;
            _timer += dt;
            while (_timer >= interval && _active != null)
            {
                _timer -= interval;
                StepPhase(grid);
            }
        }

        static void BeginRestore(Grid grid, Memory m)
        {
            _active = m;
            _timer = 0f;

            // 结构方块自己是这次还原的开关：快照里把它记成空的，重建时不会把自己搭回来
            if (m.Cells != null)
            {
                for (int i = 0; i + 2 < m.Cells.Length; i += 3)
                {
                    int index = IndexOf(m, m.Cells[i], m.Cells[i + 1], m.Cells[i + 2]);
                    if (index >= 0)
                        m.Blocks[index] = 0;
                }
            }

            _phase = Phase.Clear;
            BuildClearList(grid, m);
        }

        /// <summary>收集场上现在有东西的格子，从下往上排：消失就是照这个顺序一格一格收。</summary>
        static void BuildClearList(Grid grid, Memory m)
        {
            _work.Clear();
            for (int y = m.MinY; y < m.MinY + m.SizeY; y++)
            {
                for (int z = m.MinZ; z < m.MinZ + m.SizeZ; z++)
                {
                    for (int x = m.MinX; x < m.MinX + m.SizeX; x++)
                    {
                        int id = grid.Get(x, y, z);
                        if (id <= 0 && Fluids.AmountAt(x, y, z) <= 0)
                            continue;   // 空格子不用管，收的是有东西的
                        _work.Add(x);
                        _work.Add(y);
                        _work.Add(z);
                        _work.Add(id);
                    }
                }
            }

            _cursor = 0;
            _count = _work.Count / 4;
            _batch = BatchFor(_count, ClearInterval);
        }

        /// <summary>收集记忆里有方块的格子，同样从下往上：重建就是照这个顺序一格一格掉回来。</summary>
        static void BuildDropList(Memory m)
        {
            _work.Clear();
            for (int y = 0; y < m.SizeY; y++)
            {
                for (int z = 0; z < m.SizeZ; z++)
                {
                    for (int x = 0; x < m.SizeX; x++)
                    {
                        int id = m.Blocks[(y * m.SizeZ + z) * m.SizeX + x];
                        if (id <= 0)
                            continue;
                        _work.Add(m.MinX + x);
                        _work.Add(m.MinY + y);
                        _work.Add(m.MinZ + z);
                        _work.Add(id);
                    }
                }
            }

            _cursor = 0;
            _count = _work.Count / 4;
            _batch = BatchFor(_count, DropInterval);
        }

        /// <summary>格子太多的时候一次处理多格，把一个阶段的时长压在上限以内。</summary>
        static int BatchFor(int count, float interval)
        {
            if (count <= 0)
                return 1;
            float capacity = Mathf.Max(1f, MaxPhaseTime / interval);
            return Mathf.Max(1, Mathf.CeilToInt(count / capacity));
        }

        /// <summary>处理这一小批格子；一批走完就切阶段或者收尾。</summary>
        static void StepPhase(Grid grid)
        {
            int end = Mathf.Min(_count, _cursor + _batch);
            for (int i = _cursor; i < end; i++)
            {
                int b = i * 4;
                if (_phase == Phase.Clear)
                    Vanish(grid, _work[b], _work[b + 1], _work[b + 2], _work[b + 3]);
                else
                    Drop(grid, _work[b], _work[b + 1], _work[b + 2], _work[b + 3]);
            }
            _cursor = end;

            if (_cursor < _count)
                return;

            if (_phase == Phase.Clear)
            {
                // 收的这一两秒里玩家可能又落了一块：场上还有东西就再收一轮
                if (HasContent(grid, _active))
                {
                    BuildClearList(grid, _active);
                    return;
                }

                BuildDropList(_active);
                _phase = Phase.Drop;
                _timer = 0f;
                if (_count <= 0)
                    Finish(_active);
            }
            else
            {
                Finish(_active);
            }
        }

        static bool HasContent(Grid grid, Memory m)
        {
            for (int y = m.MinY; y < m.MinY + m.SizeY; y++)
                for (int z = m.MinZ; z < m.MinZ + m.SizeZ; z++)
                    for (int x = m.MinX; x < m.MinX + m.SizeX; x++)
                    {
                        if (grid.Get(x, y, z) > 0 || Fluids.AmountAt(x, y, z) > 0)
                            return true;
                    }
            return false;
        }

        /// <summary>收掉一格：方块、水一起清。视图层擦掉它，再撒一点碎块。</summary>
        static void Vanish(Grid grid, int x, int y, int z, int blockId)
        {
            if (!grid.InBounds(x, y, z))
                return;

            // 这一格要是别的结构方块的落脚点，那条记忆跟着作废
            if (IsStructure(blockId))
                ForgetAt(x, y, z, _active);

            if (grid.Get(x, y, z) > 0)
                grid.Set(x, y, z, 0);
            if (Fluids.AmountAt(x, y, z) > 0)
                Fluids.ClearCell(x, y, z);

            if (CellVanished != null)
                CellVanished(x, y, z, blockId);
        }

        /// <summary>落下一格：把记忆里的方块放回原位，视图层让它从上面掉下来。</summary>
        static void Drop(Grid grid, int x, int y, int z, int blockId)
        {
            if (!grid.InBounds(x, y, z) || blockId <= 0)
                return;

            if (grid.Get(x, y, z) != blockId)
                grid.Set(x, y, z, blockId);

            if (CellDropped != null)
                CellDropped(x, y, z, blockId);
        }

        /// <summary>某一格的结构方块被抹掉了：记在那格上的记忆作废（正在还原的那条除外）。</summary>
        static void ForgetAt(int x, int y, int z, Memory keep)
        {
            for (int i = _memories.Count - 1; i >= 0; i--)
            {
                Memory other = _memories[i];
                if (other == keep || other == _active)
                    continue;
                if (!CellInMemory(other, x, y, z))
                    continue;

                other.Cancelled = true;
                other.Queued = false;
                _memories.RemoveAt(i);
            }
        }

        /// <summary>重建完了：这条记忆删掉。结构方块在快照里记成空的，早就跟着消失那一段收掉了。</summary>
        static void Finish(Memory m)
        {
            _memories.Remove(m);
            _active = null;
            _cursor = 0;
            _count = 0;
            _batch = 1;
            _timer = 0f;
            _work.Clear();

            if (Finished != null)
                Finished(m.X, m.Y, m.Z);
        }

        /// <summary>清掉全部记忆和队列（新开局、读档时用）。</summary>
        public static void Reset()
        {
            _memories.Clear();
            _pending.Clear();
            _active = null;
            _phase = Phase.Clear;
            _work.Clear();
            _cursor = 0;
            _count = 0;
            _batch = 1;
            _timer = 0f;
        }

        // ---------------------------------------------------------------- 存档

        /// <summary>
        /// 把记忆拍平成 int 数组。第一个数是版本标记 -2，然后每条：
        /// cx, cy, cz, minX, minY, minZ, sizeX, sizeY, sizeZ, 块数, 格子数,
        /// 接着是块 id，最后是这块结构方块占的格子（3 个一组）。
        ///
        /// 起点 min* 和格子都要单独存：老记忆的范围是「中心 ±1 再裁到场地内」，
        /// 贴边时尺寸会变成 2，光靠中心和尺寸反推起点会算错半格；
        /// 新记忆一整块 4 格结构方块只记一条，光有锚点找不到另外三格。
        /// </summary>
        public static int[] SerializeMemories()
        {
            var list = new List<int>();
            list.Add(SaveVersion);

            for (int i = 0; i < _memories.Count; i++)
            {
                Memory m = _memories[i];
                if (m == _active || m.Cancelled)
                    continue;   // 正在还原的那条马上要消失，不用存

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

                int cellCount = m.Cells != null ? m.Cells.Length : 0;
                list.Add(cellCount);

                for (int k = 0; k < m.Blocks.Length; k++)
                    list.Add(m.Blocks[k]);
                for (int k = 0; k < cellCount; k++)
                    list.Add(m.Cells[k]);
            }

            return list.ToArray();
        }

        /// <summary>从存档里把记忆读回来。带 -2 前缀的是新格式，不带的是老格式（3×3×3 那种）。</summary>
        public static void DeserializeMemories(int[] data)
        {
            Reset();
            if (data == null || data.Length == 0)
                return;

            int i = 0;
            bool versioned = data[0] == SaveVersion;
            if (versioned)
                i = 1;

            while (true)
            {
                int header = versioned ? 11 : 10;
                if (i + header > data.Length)
                    break;

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
                int cellCount = versioned ? data[i + 10] : 3;
                i += header;

                // 尺寸和块数对不上就是存档坏了，后面的不再硬读
                if (m.SizeX <= 0 || m.SizeY <= 0 || m.SizeZ <= 0)
                    break;
                if (count != m.SizeX * m.SizeY * m.SizeZ)
                    break;
                if (i + count > data.Length)
                    break;

                m.Blocks = new int[count];
                System.Array.Copy(data, i, m.Blocks, 0, count);
                i += count;

                if (versioned)
                {
                    if (cellCount <= 0 || cellCount % 3 != 0 || i + cellCount > data.Length)
                        break;

                    m.Cells = new int[cellCount];
                    System.Array.Copy(data, i, m.Cells, 0, cellCount);
                    i += cellCount;
                }
                else
                {
                    // 老格式只存了中心格，它自己就是唯一的触发格
                    m.Cells = new[] { m.X, m.Y, m.Z };
                }

                _memories.Add(m);
            }
        }
    }
}
