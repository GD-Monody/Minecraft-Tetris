using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>
    /// 方块材质池。特殊方块单独一组，不混进普通随机材质池里 ——
    /// 普通池抽的是“随便一块砖”，特殊池抽的是会触发额外规则的方块（沙会掉、以后 TNT 会炸）。
    /// </summary>
    public static class BlockPools
    {
        // 特殊方块：按方块英文名认。混凝土粉末全是 *_concrete_powder，用后缀判。
        static readonly string[] SpecialNames =
        {
            "sand", "red_sand", "gravel",
            "suspicious_sand", "suspicious_gravel",
        };

        // 每 SpecialEvery 个方块里，有一个整块用特殊材质
        public const int SpecialEvery = 12;

        static int[] _special;
        static int[] _normal;
        static int _waterId = -1;
        static int _lavaId = -1;
        static int _tntId = -1;
        static int _redstoneId = -1;
        static int _count;
        /// <summary>抽到 TNT 之后，下一次抽必然给红石块 —— 见 NextRandomId。</summary>
        static bool _forceRedstone;

        public static int[] Special { get { Ensure(); return _special; } }
        public static int[] Normal { get { Ensure(); return _normal; } }

        public static bool IsSpecial(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            for (int i = 0; i < SpecialNames.Length; i++)
            {
                if (SpecialNames[i] == name)
                    return true;
            }
            return name.EndsWith("_concrete_powder");
        }

        static void Ensure()
        {
            if (_normal != null)
                return;
            Build();
        }

        public static void Build()
        {
            var special = new System.Collections.Generic.List<int>();
            var normal = new System.Collections.Generic.List<int>();
            var blocks = BlockCatalog.Blocks;
            _waterId = -1;
            _lavaId = -1;
            _tntId = -1;
            _redstoneId = -1;
            _forceRedstone = false;
            for (int i = 0; i < blocks.Length; i++)
            {
                string n = blocks[i].Name;
                if (n == "water")
                {
                    _waterId = i + 1;
                    continue;   // 水和岩浆单独处理，不进任何一个池子
                }
                if (n == "lava")
                {
                    _lavaId = i + 1;
                    continue;
                }
                if (n == "tnt")
                {
                    // TNT 不进普通池：它是「道具」不是「砖」，出现频率要按件数控制。
                    _tntId = i + 1;
                    continue;
                }
                if (n == "redstone_block")
                {
                    // 红石块同时留在普通池里（偶尔自然出现，能顺手点着场上的 TNT），
                    // 另外记下编号，抽到 TNT 之后强制跟一块。
                    _redstoneId = i + 1;
                }
                if (IsSpecial(n))
                    special.Add(i + 1);
                else
                    normal.Add(i + 1);
            }
            _special = special.ToArray();
            _normal = normal.ToArray();
            _count = 0;
            Debug.Log("[pools] 普通 " + _normal.Length + " 种 / 特殊 " + _special.Length +
                      " 种 / 水 " + _waterId + " 岩浆 " + _lavaId +
                      " / TNT " + _tntId + " 红石 " + _redstoneId);
        }

        /// <summary>抽一个材质编号。每隔 SpecialEvery 个方块抽一次特殊池，水和岩浆在特殊池里占比较高。</summary>
        public static int NextRandomId()
        {
            Ensure();

            // TNT 后面必跟一块红石块。只给 TNT 不给红石的话，红石块在普通池里是三百多分之一，
            // 这条机制基本等于不存在；配上一块，玩家才有得点。
            if (_forceRedstone)
            {
                _forceRedstone = false;
                if (_redstoneId > 0)
                    return _redstoneId;
            }

            _count++;
            if (_special.Length > 0 && _count % SpecialEvery == 0)
            {
                // 特殊方块里：水 30%、岩浆 20%、TNT 20%，剩下 30% 给沙类
                float r = Random.value;
                if (_waterId > 0 && r < 0.30f)
                    return _waterId;
                if (_lavaId > 0 && r < 0.50f)
                    return _lavaId;
                if (_tntId > 0 && r < 0.70f)
                {
                    _forceRedstone = true;
                    return _tntId;
                }
                return _special[Random.Range(0, _special.Length)];
            }
            if (_normal.Length == 0)
                return 1;
            return _normal[Random.Range(0, _normal.Length)];
        }
    }
}
