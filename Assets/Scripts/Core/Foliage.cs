using System.Collections.Generic;
using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>
    /// 原木长树叶。
    ///
    /// 原版 MC 里原木不会长树叶（树是世界生成时整棵一起生成的），这是本项目的自创规则：
    /// 原木方块落定之后，在它周围找个空格长一片树叶。规则：
    ///
    /// - 认谁：名字以 `_log` / `_wood` 结尾的方块（自然原木、六面树皮的「木」）。
    ///   `stripped_*`（剥皮）不长——树皮都剥了；菌柄 / 菌核（`_stem` / `_hyphae`）不长——菌类没有树叶；
    ///   `bamboo_block` 也不长。
    /// - 长什么：同树种的 `<木头>_leaves`，比如 oak_log → oak_leaves。
    /// - 长哪：木头正上方、斜上方一圈、同一层的四个侧面，共 13 个候选位；只挑空的（
    ///   被方块占着、或者积着水都不行）。候选里随机挑一个，不然叶片永远朝着同一个方向，
    ///   看起来像贴上去的。
    /// - 一片一个：每个原木格长一片，整块一次最多 6 片（`MaxLeavesPerLock`）——
    ///   玩家在创意工坊里画个 25 格的原木模具，不该一次刷出 25 片叶子。
    ///
    /// 树叶是普通方块：进网格、挡方块、能凑满行、能被消除、跟着存档走。
    /// 不会自己凋落（原版的「没有原木就枯萎」不做），也不长别的树叶。
    /// </summary>
    public static class Foliage
    {
        /// <summary>一次落定最多长几片树叶。</summary>
        public const int MaxLeavesPerLock = 6;

        /// <summary>这一次落定长出来的格子（视图层照着画）。每次 Grow 会先清空。</summary>
        public static readonly List<Vector3Int> LastGrown = new List<Vector3Int>();

        /// <summary>
        /// 候选位置：正上方、斜上方一圈（8 个）、同一层四个侧面（4 个）。
        /// 不含正下方和斜下方——树叶长在树干底下不像话。
        /// </summary>
        static readonly Vector3Int[] Offsets =
        {
            new Vector3Int(0, 1, 0),

            new Vector3Int(-1, 1, 0), new Vector3Int(1, 1, 0),
            new Vector3Int(0, 1, -1), new Vector3Int(0, 1, 1),
            new Vector3Int(-1, 1, -1), new Vector3Int(1, 1, -1),
            new Vector3Int(-1, 1, 1), new Vector3Int(1, 1, 1),

            new Vector3Int(-1, 0, 0), new Vector3Int(1, 0, 0),
            new Vector3Int(0, 0, -1), new Vector3Int(0, 0, 1),
        };

        static readonly List<Vector3Int> _candidates = new List<Vector3Int>(Offsets.Length);

        /// <summary>方块编号 → 会长的树叶编号（0 = 不长）。按下标存，查起来是一次数组访问。</summary>
        static int[] _leafOf;
        static bool _ready;

        public static void Init()
        {
            if (_ready)
                return;
            _ready = true;

            BlockDef[] blocks = BlockCatalog.Blocks;
            _leafOf = new int[blocks.Length];

            var byName = new Dictionary<string, int>(blocks.Length);
            for (int i = 0; i < blocks.Length; i++)
                byName[blocks[i].Name] = i + 1;

            for (int i = 0; i < blocks.Length; i++)
            {
                string n = blocks[i].Name;
                if (n == null || n.StartsWith("stripped_"))
                    continue;

                string wood;
                if (n.EndsWith("_log"))
                    wood = n.Substring(0, n.Length - 4);
                else if (n.EndsWith("_wood"))
                    wood = n.Substring(0, n.Length - 5);
                else
                    continue;

                int leafId;
                if (byName.TryGetValue(wood + "_leaves", out leafId))
                    _leafOf[i] = leafId;
            }
        }

        /// <summary>这种方块会长出的树叶编号；不长返回 0。</summary>
        public static int LeafIdFor(int blockId)
        {
            Init();
            int index = blockId - 1;
            if (index < 0 || index >= _leafOf.Length)
                return 0;
            return _leafOf[index];
        }

        public static bool GrowsLeaves(int blockId)
        {
            return LeafIdFor(blockId) > 0;
        }

        /// <summary>某一格周围长一片树叶。返回长了几片（0 或 1）。</summary>
        public static int GrowAround(Grid grid, int x, int y, int z, int logId)
        {
            int leafId = LeafIdFor(logId);
            if (leafId <= 0 || grid == null)
                return 0;

            _candidates.Clear();
            for (int i = 0; i < Offsets.Length; i++)
            {
                int nx = x + Offsets[i].x;
                int ny = y + Offsets[i].y;
                int nz = z + Offsets[i].z;

                if (!grid.InBounds(nx, ny, nz))
                    continue;
                if (grid.Get(nx, ny, nz) != 0)
                    continue;
                // 有水的格子不长：长进去等于把水挤没了，那格水会凭空消失
                if (Fluids.AmountAt(nx, ny, nz) > 0)
                    continue;

                _candidates.Add(new Vector3Int(nx, ny, nz));
            }

            if (_candidates.Count == 0)
                return 0;

            Vector3Int pick = _candidates[Random.Range(0, _candidates.Count)];
            grid.Set(pick.x, pick.y, pick.z, leafId);
            LastGrown.Add(pick);
            return 1;
        }

        /// <summary>
        /// 整块方块落定后结算：每个原木格长一片，最多 MaxLeavesPerLock 片。
        /// 返回这次一共长了几片（结果也记在 LastGrown 里）。
        /// </summary>
        public static int Grow(Grid grid, Piece piece)
        {
            LastGrown.Clear();
            if (grid == null || piece == null)
                return 0;
            if (!GrowsLeaves(piece.BlockId))
                return 0;

            int grown = 0;
            for (int i = 0; i < piece.Cells.Length && grown < MaxLeavesPerLock; i++)
            {
                Vector3Int c = piece.WorldCell(i);
                if (!grid.InBounds(c.x, c.y, c.z))
                    continue;
                // 落定那几格可能已经被改写过（水方块落进岩浆会当场变黑曜石），
                // 只剩真正还是原木的格子才长叶子
                if (grid.Get(c.x, c.y, c.z) != piece.BlockId)
                    continue;

                grown += GrowAround(grid, c.x, c.y, c.z, piece.BlockId);
            }

            return grown;
        }
    }
}
