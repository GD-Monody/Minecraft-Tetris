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

        // 玻璃不放普通池：普通池三百多种，抽中一次要等几百块，玩家基本见不到它。
        // 改成按块数定期给一块，大约每 GlassEvery 个方块里有一块是玻璃。
        public const int GlassEvery = 40;

        // 雪块和冰同理：普通池三百多种抽不到，而且它俩要碰上岩浆才有戏，
        // 所以定期发，间隔比玻璃短一点（融化是消耗品，化一次就没了）。
        public const int MeltEvery = 30;

        static int[] _special;
        static int[] _normal;
        static int _waterId = -1;
        static int _lavaId = -1;
        static int _tntId = -1;
        static int _redstoneId = -1;
        /// <summary>基岩编号。基岩常驻特殊池。</summary>
        static int _bedrockId = -1;
        /// <summary>结构方块编号。同样常驻特殊池。</summary>
        static int _structureId = -1;
        /// <summary>玻璃编号。不进普通池，按块数定期给一块，见 GlassEvery。</summary>
        static int _glassId = -1;
        /// <summary>雪块 / 冰的编号。同样不进普通池，按块数定期发，见 MeltEvery。</summary>
        static int _snowId = -1;
        static int _iceId = -1;
        static int _count;
        /// <summary>距上一块玻璃又抽了几块（只数普通池那一路）。</summary>
        static int _glassCount;
        /// <summary>距上一块雪/冰又抽了几块。</summary>
        static int _meltCount;
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
            _bedrockId = -1;
            _structureId = -1;
            _glassId = -1;
            _snowId = -1;
            _iceId = -1;
            _glassCount = 0;
            _meltCount = 0;
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
                if (n == "bedrock")
                {
                    // 基岩消除时不下落，掉下来就是一根柱子。要不要这个行为由设置页的
                    // 「基岩钉住不动」开关决定；方块本身常驻特殊池，不然关掉开关就等于把它从游戏里删了。
                    _bedrockId = i + 1;
                    continue;
                }
                if (n == "structure_block")
                {
                    // 结构方块是「道具」：落定时记住一片构造，被消掉时还原。
                    // 一样常驻特殊池，还原与否交给「结构方块还原」开关。
                    _structureId = i + 1;
                    continue;
                }
                if (n.EndsWith("_leaves"))
                {
                    // 树叶是原木长出来的，不该当成一整块方块的材质掉下来
                    continue;
                }
                if (n == "glass")
                {
                    // 玻璃从普通池里拿出来单独发，不然一块难求（见 GlassEvery）。
                    // 它照旧是普通方块，只是出现频率被钉死了。
                    _glassId = i + 1;
                    continue;
                }
                if (n == "snow_block" || n == "ice")
                {
                    // 雪块 / 冰也单独发（见 MeltEvery）：它俩的机制要碰上岩浆才触发，
                    // 埋在三百多种的普通池里等于没有。行为本身由「雪与冰遇岩浆融化」开关控制。
                    if (n == "snow_block")
                        _snowId = i + 1;
                    else
                        _iceId = i + 1;
                    continue;
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
                      " / TNT " + _tntId + " 红石 " + _redstoneId +
                      " 基岩 " + _bedrockId + " 结构 " + _structureId + " 玻璃 " + _glassId +
                      " 雪 " + _snowId + " 冰 " + _iceId);
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
                // 特殊方块里：水 30%、岩浆 20%、TNT 20%、基岩 12%、结构方块 10%，剩下的是沙类。
                // 基岩和结构方块常驻；其它机制关不关都不影响池子，关掉只是行为变普通。
                float r = Random.value;
                float acc = 0f;

                if (_waterId > 0)
                {
                    acc += 0.30f;
                    if (r < acc) return _waterId;
                }
                if (_lavaId > 0)
                {
                    acc += 0.20f;
                    if (r < acc) return _lavaId;
                }
                if (_tntId > 0)
                {
                    acc += 0.20f;
                    if (r < acc)
                    {
                        _forceRedstone = true;
                        return _tntId;
                    }
                }
                if (_bedrockId > 0)
                {
                    acc += 0.12f;
                    if (r < acc) return _bedrockId;
                }
                if (_structureId > 0)
                {
                    acc += 0.10f;
                    if (r < acc) return _structureId;
                }
                return _special[Random.Range(0, _special.Length)];
            }
            // 玻璃和雪/冰按块数发：普通池抽一次，两个计数器各加一，攒够各自的间隔就给一块。
            // 放在特殊池之后，特殊方块那一抽不参与计数，间隔才稳定。
            // 两个计数器先一起加，再决定给谁 —— 写完一个 return 再数另一个的话，
            // 撞在同一次（每 120 块）的时候雪/冰的计数会少加一回，节奏慢慢就歪了。
            if (_glassId > 0)
                _glassCount++;
            if (_snowId > 0 || _iceId > 0)
                _meltCount++;

            if (_glassId > 0 && _glassCount % GlassEvery == 0)
                return _glassId;

            if (_meltCount > 0 && _meltCount % MeltEvery == 0)
            {
                // 雪块和冰各一半：两个都给的话一轮里就能凑出「雪挨着冰」的局面，看不出融化
                if (_snowId > 0 && _iceId > 0)
                    return Random.value < 0.5f ? _snowId : _iceId;
                return _snowId > 0 ? _snowId : _iceId;
            }

            if (_normal.Length == 0)
                return 1;
            return _normal[Random.Range(0, _normal.Length)];
        }
    }
}
