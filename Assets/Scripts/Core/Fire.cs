using System.Collections.Generic;
using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>
    /// 着火：木头、羊毛、树叶这些可燃方块挨着岩浆就被点着，烧一阵子烧没了；
    /// 烧着的方块还会把火传给旁边同样可燃的方块。
    ///
    /// 跟原版最大的差别是「火不是一个方块」：原版火占一格、能自己灭也能自己飘，
    /// 这里火只是方块身上的一个状态（记在 Burning 列表里）。
    /// 好处是坐标不会跟着重力 / 消行错位（TNT 引信也是这么处理的），
    /// 代价是看不到飘在空中的火苗 —— 视觉上改成在这一格露在外面的几个面上
    /// 贴原版的火贴图（见 GridView 的火苗面片）。
    ///
    /// 三条规则：
    /// - 点燃：六向相邻有岩浆（岩浆是流体、不写网格，所以查 Fluids 的份数）。斜对角不算。
    /// - 燃烧：每块随机烧 2.2 ~ 4.4 秒。原版是一块木头烧 30 ~ 90 秒，
    ///   一局俄罗斯方块里那样太慢了，缩到「看得见它在烧、又来得及反应」这个量级。
    ///   烧完那一格直接空掉，上面压着的方块照常塌下来。
    /// - 蔓延：每 0.45 秒往随机一个方向传一格火，只传六向里的可燃邻居。
    ///   一次只传一格，是怕一片木头一秒内全没 —— 那就不像烧，像爆炸。
    ///
    /// 另外一条（原版也有、这里顺手做掉）：水能灭火。
    /// 旁边有水的方块点不着，已经烧着的水漫过来就浇灭，方块本身留着。
    /// </summary>
    public static class Fire
    {
        /// <summary>一块方块从点着到烧光的时间范围（秒）。</summary>
        public const float BurnSecondsMin = 2.2f;
        public const float BurnSecondsMax = 4.4f;

        /// <summary>每隔多久掷一次蔓延的骰子（秒）。</summary>
        public const float SpreadInterval = 0.45f;

        /// <summary>每轮蔓延点着几个邻居（都是随机挑方向的）。</summary>
        public const int SpreadPerRound = 1;

        /// <summary>烧掉一格得多少分。比 TNT 的 4 分低：火是慢慢烧的，也不挑地方。</summary>
        public const int ScorePerBlock = 2;

        public struct Burning
        {
            public int X, Y, Z;
            /// <summary>还要烧多久（秒）。</summary>
            public float Left;
            /// <summary>点着时抽到的总时长（视图层拿它算「烧了多少」）。</summary>
            public float Total;
        }

        static readonly List<Burning> _burning = new List<Burning>();
        static readonly List<Vector3Int> _ignited = new List<Vector3Int>();
        static readonly List<Vector3Int> _burnedOut = new List<Vector3Int>();
        static readonly List<int> _burnedBlocks = new List<int>();

        static float _spreadTimer;

        /// <summary>正在烧的格子（视图层每帧读它画火光）。</summary>
        public static List<Burning> BurningCells { get { return _burning; } }

        /// <summary>这一帧新点着的格子（播点火声、开始冒火光）。</summary>
        public static List<Vector3Int> LastIgnited { get { return _ignited; } }

        /// <summary>这一帧烧光消失的格子。</summary>
        public static List<Vector3Int> LastBurnedOut { get { return _burnedOut; } }

        /// <summary>和 LastBurnedOut 一一对应的方块编号（烧掉之前是什么，粒子要用）。</summary>
        public static List<int> LastBurnedBlocks { get { return _burnedBlocks; } }

        // 六向邻居：下、上、左、右、后、前。和 Melt / Fluids 里的顺序一致。
        static readonly int[] Dx = { 0, 0, -1, 1, 0, 0 };
        static readonly int[] Dy = { -1, 1, 0, 0, 0, 0 };
        static readonly int[] Dz = { 0, 0, 0, 0, -1, 1 };

        /// <summary>
        /// 可燃方块的名字特征：木头（原木 / 木板 / 去皮木头 / 木制品）、羊毛、树叶。
        ///
        /// 用「名字里含什么」而不是逐个列编号，是因为方块表是脚本生成的（354 种），
        /// 手写编号一动表就错位。反例单独排除：铁门、末影箱含 door / chest 但不是木头，
        /// 下界的绯红 / 诡异木在原版就是抗火的，一并排除。
        /// </summary>
        static readonly string[] FlammableWords =
        {
            "log", "wood", "planks", "leaves", "wool",
            "fence", "door", "trapdoor", "sign", "chest", "bookshelf", "ladder",
            "bamboo", "crafting_table", "note_block", "jukebox", "barrel", "beehive",
            "composter", "lectern", "loom", "cartography_table", "fletching_table",
            "smithing_table", "scaffolding",
        };

        /// <summary>名字里带这些的一律不算可燃（木质特征词会误伤它们）。</summary>
        static readonly string[] NonFlammableWords = { "iron", "ender", "crimson", "warped" };

        static bool[] _flammable;

        /// <summary>这块砖烧得着吗（木头 / 羊毛 / 树叶这类）。</summary>
        public static bool IsFlammable(int blockId)
        {
            if (blockId <= 0)
                return false;

            if (_flammable == null)
            {
                BlockDef[] blocks = BlockCatalog.Blocks;
                _flammable = new bool[blocks.Length + 1];
                for (int i = 0; i < blocks.Length; i++)
                {
                    string name = blocks[i].Name;
                    bool skip = false;
                    for (int k = 0; k < NonFlammableWords.Length; k++)
                    {
                        if (name.Contains(NonFlammableWords[k]))
                        {
                            skip = true;
                            break;
                        }
                    }
                    if (skip)
                        continue;

                    for (int k = 0; k < FlammableWords.Length; k++)
                    {
                        if (name.Contains(FlammableWords[k]))
                        {
                            _flammable[i + 1] = true;
                            break;
                        }
                    }
                }
            }

            return blockId < _flammable.Length && _flammable[blockId];
        }

        /// <summary>重开一局 / 演示场景重摆时清干净。</summary>
        public static void Reset()
        {
            _burning.Clear();
            _ignited.Clear();
            _burnedOut.Clear();
            _burnedBlocks.Clear();
            _spreadTimer = 0f;
        }

        /// <summary>这一格现在烧着吗。</summary>
        public static bool IsBurning(int x, int y, int z)
        {
            for (int i = 0; i < _burning.Count; i++)
            {
                if (_burning[i].X == x && _burning[i].Y == y && _burning[i].Z == z)
                    return true;
            }
            return false;
        }

        /// <summary>这一格的燃烧进度 0~1（1 = 快烧光了）。没烧返回 -1。</summary>
        public static float BurnProgress(int x, int y, int z)
        {
            for (int i = 0; i < _burning.Count; i++)
            {
                if (_burning[i].X == x && _burning[i].Y == y && _burning[i].Z == z)
                {
                    Burning b = _burning[i];
                    if (b.Total <= 0.001f)
                        return 1f;
                    return Mathf.Clamp01(1f - b.Left / b.Total);
                }
            }
            return -1f;
        }

        /// <summary>
        /// 全场扫一遍，把所有「六向挨着岩浆的可燃方块」点着。
        /// 和 TNT 的 PrimeContact 一样：方块落定后扫一次，岩浆淌动后再扫一次
        /// （岩浆不写网格，只有它漫到旁边那一刻才看得见接触）。返回这一轮点着了几个。
        /// </summary>
        public static int IgniteContact(Grid grid)
        {
            if (grid == null || !GameSettings.FireBurns)
                return 0;
            if (Fluids.LavaId <= 0)
                return 0;

            int fired = 0;
            for (int x = 0; x < grid.SizeX; x++)
            {
                for (int y = 0; y < grid.SizeY; y++)
                {
                    for (int z = 0; z < grid.SizeZ; z++)
                    {
                        if (!IsFlammable(grid.Get(x, y, z)))
                            continue;
                        if (IsBurning(x, y, z))
                            continue;
                        if (!TouchesLava(x, y, z))
                            continue;
                        if (Ignite(grid, x, y, z))
                            fired++;
                    }
                }
            }
            return fired;
        }

        /// <summary>直接点着一格（要求那格确实是可燃方块）。返回是否点着了。</summary>
        public static bool Ignite(Grid grid, int x, int y, int z)
        {
            if (grid == null || !GameSettings.FireBurns || !grid.InBounds(x, y, z))
                return false;
            if (!IsFlammable(grid.Get(x, y, z)))
                return false;
            if (IsBurning(x, y, z))
                return false;
            // 湿的烧不着（水就在旁边）
            if (TouchesWater(x, y, z))
                return false;

            float life = Random.Range(BurnSecondsMin, BurnSecondsMax);
            _burning.Add(new Burning { X = x, Y = y, Z = z, Left = life, Total = life });
            _ignited.Add(new Vector3Int(x, y, z));
            return true;
        }

        /// <summary>
        /// 推进燃烧。返回这一帧有没有方块烧光（烧光了要落重力、刷新画面）。
        /// 结果在 LastBurnedOut / LastIgnited / LastBurnedBlocks 里。
        /// </summary>
        public static bool Tick(Grid grid, float dt)
        {
            _ignited.Clear();
            _burnedOut.Clear();
            _burnedBlocks.Clear();

            // 中途关掉开关：已经烧着的直接掐灭，不留到下次打开时再烧
            if (grid == null || !GameSettings.FireBurns)
            {
                if (_burning.Count > 0)
                    _burning.Clear();
                return false;
            }

            if (_burning.Count == 0)
                return false;

            bool spreadNow = false;
            _spreadTimer += dt;
            if (_spreadTimer >= SpreadInterval)
            {
                _spreadTimer = 0f;
                spreadNow = true;
            }

            bool burnedOut = false;

            // 倒着走：烧光的要从列表里摘掉
            for (int i = _burning.Count - 1; i >= 0; i--)
            {
                Burning b = _burning[i];
                int id = grid.Get(b.X, b.Y, b.Z);

                // 这一格已经不是可燃方块了（被消行带走 / 被炸没了 / 被重力挪走了）：
                // 默默去掉这一处的火，坐标对不上的火留着只会烧掉不该烧的东西。
                if (!IsFlammable(id))
                {
                    _burning.RemoveAt(i);
                    continue;
                }

                // 水漫过来 → 浇灭，方块留着（原版也是这样：火灭了木头还在）
                if (TouchesWater(b.X, b.Y, b.Z))
                {
                    _burning.RemoveAt(i);
                    continue;
                }

                b.Left -= dt;
                if (b.Left > 0f)
                {
                    _burning[i] = b;
                    if (spreadNow)
                        TrySpread(grid, b);
                    continue;
                }

                _burning.RemoveAt(i);
                BurnOut(grid, b, id);
                burnedOut = true;
            }

            return burnedOut;
        }

        /// <summary>烧光了：那一格空掉，记下来给视图层撒碎块。</summary>
        static void BurnOut(Grid grid, Burning b, int id)
        {
            // 结构方块被烧掉了 → 排一次还原（要在清掉这一格之前说，清完就读不到它是什么了）
            Structure.NotifyCleared(grid, b.X, b.Y, b.Z);

            grid.Set(b.X, b.Y, b.Z, 0);
            _burnedOut.Add(new Vector3Int(b.X, b.Y, b.Z));
            _burnedBlocks.Add(id);
        }

        /// <summary>
        /// 把火传给旁边同样可燃的方块：每轮只点着一格，方向随机。
        ///
        /// 为什么不掷骰子决定「这一轮传不传」：那样一片木头有可能连着几轮都没中，
        /// 火看着像卡住了；一次只传一格已经够慢了，方向随机则保证它一直在爬。
        /// </summary>
        static void TrySpread(Grid grid, Burning b)
        {
            // 从随机一个方向开始绕一圈，避免火总是先往同一个方向走
            int first = Random.Range(0, 6);
            for (int k = 0; k < 6; k++)
            {
                int d = (first + k) % 6;
                int nx = b.X + Dx[d];
                int ny = b.Y + Dy[d];
                int nz = b.Z + Dz[d];
                if (!grid.InBounds(nx, ny, nz))
                    continue;
                if (!IsFlammable(grid.Get(nx, ny, nz)))
                    continue;
                if (IsBurning(nx, ny, nz))
                    continue;

                // 新点着的排在 _burning 末尾。外层是倒序遍历，末尾的新元素这一轮
                // 不会被再访问一次，等于「刚点着的下一轮才自己开始烧」，正合适。
                Ignite(grid, nx, ny, nz);
                return;
            }
        }

        static bool TouchesLava(int x, int y, int z)
        {
            for (int k = 0; k < 6; k++)
            {
                if (LavaAt(x + Dx[k], y + Dy[k], z + Dz[k]))
                    return true;
            }
            return false;
        }

        static bool TouchesWater(int x, int y, int z)
        {
            if (Fluids.WaterId <= 0)
                return false;

            for (int k = 0; k < 6; k++)
            {
                if (WaterAt(x + Dx[k], y + Dy[k], z + Dz[k]))
                    return true;
            }
            return false;
        }

        /// <summary>Fluids.AmountAt / IdAt 自己判了越界，越界返回 0，这里不用再判一次。</summary>
        static bool LavaAt(int x, int y, int z)
        {
            return Fluids.AmountAt(x, y, z) > 0 && Fluids.IdAt(x, y, z) == Fluids.LavaId;
        }

        static bool WaterAt(int x, int y, int z)
        {
            return Fluids.AmountAt(x, y, z) > 0 && Fluids.IdAt(x, y, z) == Fluids.WaterId;
        }
    }
}
