using System.Collections.Generic;
using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>出块池里的一个形状：默认 7 种方块，或者玩家在创意工坊里做的模具。</summary>
    public class ShapeEntry
    {
        public Vector3Int[] Cells;
        public string Label;
        public PieceType Type;
        public bool IsMold;

        public ShapeEntry Clone()
        {
            return new ShapeEntry
            {
                Cells = (Vector3Int[])Cells.Clone(),
                Label = Label,
                Type = Type,
                IsMold = IsMold,
            };
        }
    }

    /// <summary>
    /// 形状池：7 种默认方块 + 创意工坊里勾选启用的模具，混在一起洗牌。
    /// 池子里一个都不剩时才重新洗一轮，保证每一轮里每种形状各出现一次。
    /// </summary>
    public class PieceBag
    {
        readonly List<ShapeEntry> _queue = new List<ShapeEntry>(7);
        readonly System.Random _rng;

        public PieceBag(int seed = 0)
        {
            _rng = seed == 0 ? new System.Random() : new System.Random(seed);
        }

        public ShapeEntry Next()
        {
            if (_queue.Count == 0)
                Refill();

            ShapeEntry e = _queue[0];
            _queue.RemoveAt(0);
            return e;
        }

        void Refill()
        {
            var all = new List<ShapeEntry>();

            for (int i = 0; i < 7; i++)
            {
                var type = (PieceType)i;
                all.Add(new ShapeEntry
                {
                    Cells = Tetromino.GetShape(type),
                    Label = type.ToString(),
                    Type = type,
                });
            }

            foreach (MoldEntry mold in MoldData.EnabledMolds())
            {
                Vector3Int[] cells = MoldData.Parse(mold.cells);
                if (cells.Length == 0)
                    continue;
                all.Add(new ShapeEntry
                {
                    Cells = Normalize(cells),
                    Label = mold.name,
                    Type = PieceType.I,
                    IsMold = true,
                });
            }

            for (int i = all.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (all[i], all[j]) = (all[j], all[i]);
            }

            _queue.AddRange(all);
        }

        /// <summary>把形状平移到最小角的三条轴上都贴 0，方便按最小角定位生成。</summary>
        public static Vector3Int[] Normalize(Vector3Int[] cells)
        {
            int minX = int.MaxValue;
            int minY = int.MaxValue;
            int minZ = int.MaxValue;
            for (int i = 0; i < cells.Length; i++)
            {
                minX = Mathf.Min(minX, cells[i].x);
                minY = Mathf.Min(minY, cells[i].y);
                minZ = Mathf.Min(minZ, cells[i].z);
            }

            var result = new Vector3Int[cells.Length];
            for (int i = 0; i < cells.Length; i++)
                result[i] = new Vector3Int(cells[i].x - minX, cells[i].y - minY, cells[i].z - minZ);
            return result;
        }
    }
}
