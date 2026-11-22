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

        // 南瓜头是「道具」，和 TNT 一样不进普通池：召傀儡得靠它在场上凑摆法，
        // 埋在三百多分之一的普通池里等于这个玩法不存在。到点的时候先把身体
        // （雪块 / 铁块 / 铜块）放下来，南瓜头紧跟其后 —— 顺序见 NextRandomId。
        // 间隔比玻璃长：傀儡是收藏品，一场游戏见两三个就够了，满地南瓜头反而怪。
        public const int PumpkinEvery = 90;

        /// <summary>「身体落地补南瓜头」两次之间至少隔多少块。</summary>
        public const int PumpkinFollowGap = 16;

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
        /// <summary>南瓜头编号。不进普通池，按块数定期发，见 PumpkinEvery。</summary>
        static int _pumpkinId = -1;
        /// <summary>铁块 / 铜块编号。它们照旧留在普通池里，只是南瓜头会拉一块过来。</summary>
        static int _ironId = -1;
        static int _copperId = -1;
        static int _count;
        /// <summary>距上一块玻璃又抽了几块（只数普通池那一路）。</summary>
        static int _glassCount;
        /// <summary>距上一块雪/冰又抽了几块。</summary>
        static int _meltCount;
        /// <summary>距上一块南瓜头又抽了几块。</summary>
        static int _pumpkinCount;
        /// <summary>抽到 TNT 之后，下一次抽必然给红石块 —— 见 NextRandomId。</summary>
        static bool _forceRedstone;
        /// <summary>南瓜头要用的「身体」是哪种方块、还欠几块（身体在前，见 NextRandomId）。</summary>
        static int _bodyId;
        static int _bodyLeft;
        /// <summary>身体给完了，下一次抽就是南瓜头。</summary>
        static bool _pendingPumpkin;

        /// <summary>
        /// 距上一块南瓜头又抽了几块。
        ///
        /// 以前南瓜头只靠 PumpkinEvery（90 块）定期发，玩家自己抽到铁块 / 铜块、
        /// 把身体搭好了，还得等几十块才可能等到南瓜头 —— 那时候摆法多半已经被
        /// 后面的方块埋掉了，机制等于玩不到。现在身体一落地就补南瓜头，
        /// 但用这个计数挡一下，免得雪块每三十块来一次、满场都是南瓜。
        /// </summary>
        static int _sincePumpkin;

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
            _pumpkinId = -1;
            _ironId = -1;
            _copperId = -1;
            _glassCount = 0;
            _meltCount = 0;
            _pumpkinCount = 0;
            _forceRedstone = false;
            _bodyId = 0;
            _bodyLeft = 0;
            _pendingPumpkin = false;
            _sincePumpkin = PumpkinFollowGap;   // 开局第一块身体就能接上南瓜
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
                if (n == "carved_pumpkin" || n == "pumpkin")
                {
                    // 南瓜头单独发（见 PumpkinEvery）。两种都认，雕刻南瓜优先：
                    // 原版能召傀儡的是它，普通南瓜只是同一件东西的另一张贴图，
                    // 万一哪天方块表里只剩一种也不至于失效。
                    if (n == "carved_pumpkin" || _pumpkinId <= 0)
                        _pumpkinId = i + 1;
                    continue;
                }
                if (n == "iron_block")
                {
                    // 铁块 / 铜块不拿出来：它们本来就是普通砖，偶尔自然出现也能拿去凑傀儡。
                    // 这里只记编号，南瓜头出场时按编号拉一块过来。
                    _ironId = i + 1;
                }
                if (n == "copper_block")
                {
                    _copperId = i + 1;
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
                      " 雪 " + _snowId + " 冰 " + _iceId +
                      " / 南瓜 " + _pumpkinId + " 铁块 " + _ironId + " 铜块 " + _copperId);
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

            // 傀儡是「身体先落、南瓜头后压」：南瓜头必须压在身体上面才召得出来，
            // 先给南瓜头的话它落在底下，后到的身体反倒堆在它头上，这套摆法永远凑不成。
            if (_bodyLeft > 0)
            {
                _bodyLeft--;
                return _bodyId;
            }
            if (_pendingPumpkin)
            {
                _pendingPumpkin = false;
                _sincePumpkin = 0;
                return _pumpkinId;
            }

            _count++;
            _sincePumpkin++;
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
            // 玻璃 / 雪与冰 / 南瓜头按块数发：普通池抽一次，三个计数器各加一，攒够各自的间隔就给一块。
            // 放在特殊池之后，特殊方块那一抽不参与计数，间隔才稳定。
            // 三个计数器先一起加，再决定给谁；谁先轮到谁先发，被挡下的那个当场清零、
            // 下一块马上补上。原来用「% 间隔 == 0」判断，撞在同一次时被挡的那个要再等一整轮
            // 才轮得到（雪块 30、南瓜头 90，每 90 块必撞一次），攒三路之后这个偏差就明显了。
            if (_glassId > 0)
                _glassCount++;
            if (_snowId > 0 || _iceId > 0)
                _meltCount++;
            if (_pumpkinId > 0)
                _pumpkinCount++;

            if (_glassId > 0 && _glassCount >= GlassEvery)
            {
                _glassCount = 0;
                return _glassId;
            }

            if (_meltCount >= MeltEvery)
            {
                // 雪块和冰各一半：两个都给的话一轮里就能凑出「雪挨着冰」的局面，看不出融化
                _meltCount = 0;
                if (_snowId > 0 && _iceId > 0)
                    return Random.value < 0.5f ? _snowId : _iceId;
                return _snowId > 0 ? _snowId : _iceId;
            }

            if (_pumpkinId > 0 && _pumpkinCount >= PumpkinEvery)
            {
                // 开关关掉也照发：池子不跟着设置变（见上面基岩 / 结构方块那段）。
                // 身体还是给，不然南瓜头孤零零落在场上，看着像 bug。
                _pumpkinCount = 0;
                _sincePumpkin = 0;
                int need = PickGolemBody(out _bodyId);
                if (need > 0)
                {
                    // 这一抽先给身体，剩下的欠着；南瓜头排在最后 —— 顺序见函数开头那段
                    _bodyLeft = need - 1;
                    _pendingPumpkin = true;
                    return _bodyId;
                }
                return _pumpkinId;   // 方块表里一种身体都没有，只好单独给个南瓜头
            }

            if (_normal.Length == 0)
                return 1;
            return _normal[Random.Range(0, _normal.Length)];
        }

        /// <summary>
        /// 一块「傀儡身体」（雪块 / 铁块 / 铜块）落定了：接下来给一块南瓜头。
        ///
        /// 为什么挂在落定而不是出块：出块时还不知道玩家会不会用它搭身体，
        /// 落定之后才有意义 —— 刚好搭好的身体，下一块就能被南瓜头压上。
        /// 出块和落定之间隔一块（出块顺序是「先定下一块的材质」），
        /// 所以玩家看到的是「身体 → 一块别的 → 南瓜头」，手感上仍然是紧跟。
        /// </summary>
        public static void OnPieceLanded(int blockId)
        {
            Ensure();
            if (blockId <= 0 || _pumpkinId <= 0)
                return;
            if (blockId != _snowId && blockId != _ironId && blockId != _copperId)
                return;
            if (_pendingPumpkin || _bodyLeft > 0)
                return;                       // 已经在排队，别叠两层
            if (_sincePumpkin < PumpkinFollowGap)
                return;                       // 刚发过，缓一缓
            _pendingPumpkin = true;
            _pumpkinCount = 0;                // 定期发那一路别紧接着又发一次
        }

        /// <summary>
        /// 挑一种傀儡身体，返回「要塞几块」，bodyId 给出方块编号（挑不到就是 0 块）。
        ///
        /// 铁傀儡的摆法是倒 T 的四块铁，一块四格方块只有正好抽成 T 型才拼得出来，
        /// 所以给它两块；雪傀儡两块竖着、铜傀儡一块，一块四格方块都够用。
        /// </summary>
        static int PickGolemBody(out int bodyId)
        {
            int pick = Random.Range(0, 3);
            for (int i = 0; i < 3; i++)
            {
                // 从随机起点往后找一个方块表里真有的：缺哪种就顺延到下一个，不至于整套落空
                int kind = (pick + i) % 3;
                bodyId = kind == 0 ? _snowId : (kind == 1 ? _ironId : _copperId);
                if (bodyId > 0)
                    return kind == 1 ? 2 : 1;
            }
            bodyId = 0;
            return 0;
        }
    }
}
