using System.Collections.Generic;

namespace MinecraftTetris
{
    /// <summary>7-bag：七种方块洗一次，抽完之后再洗。</summary>
    public class PieceBag
    {
        readonly List<PieceType> _queue = new List<PieceType>(7);
        readonly System.Random _rng;

        public PieceBag(int seed = 0)
        {
            _rng = seed == 0 ? new System.Random() : new System.Random(seed);
        }

        public PieceType Next()
        {
            if (_queue.Count == 0)
                Refill();

            PieceType t = _queue[0];
            _queue.RemoveAt(0);
            return t;
        }

        void Refill()
        {
            var all = new List<PieceType>
            {
                PieceType.I, PieceType.O, PieceType.T, PieceType.S,
                PieceType.Z, PieceType.J, PieceType.L,
            };

            for (int i = all.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (all[i], all[j]) = (all[j], all[i]);
            }

            _queue.AddRange(all);
        }
    }
}
