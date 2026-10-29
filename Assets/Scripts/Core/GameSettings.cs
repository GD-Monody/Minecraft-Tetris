using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>
    /// 全局设置：场地尺寸与消除模式（都存 PlayerPrefs）。
    /// </summary>
    public static class GameSettings
    {
        const string SizeXKey = "size.x";
        const string SizeYKey = "size.y";
        const string SizeZKey = "size.z";
        const string ElimModeKey = "elim.mode";
        const string FallSpeedKey = "fall.speed";
        const string MusicVolumeKey = "music.volume";

        public const int MinSizeX = 5;
        public const int MaxSizeX = 20;
        public const int MinSizeY = 10;
        public const int MaxSizeY = 40;
        public const int MinSizeZ = 1;
        public const int MaxSizeZ = 10;

        /// <summary>行消：每个 (y, z) 的 x 方向一行填满就消。</summary>
        public const int ElimRows = 0;
        /// <summary>层消：整个 x × z 平面填满才消。</summary>
        public const int ElimLayer = 1;

        /// <summary>
        /// 下限取 0.01 而不是 0：0 意味着永远掉不下来，没意义；(0, 2] 里最小的两位小数就是 0.01。
        /// </summary>
        public const float MinFallSpeed = 0.01f;
        /// <summary>上限 2 倍速（0.4 秒一格）。</summary>
        public const float MaxFallSpeed = 2f;
        public const float DefaultFallSpeed = 1f;

        public static int SizeX
        {
            get { return Mathf.Clamp(PlayerPrefs.GetInt(SizeXKey, 10), MinSizeX, MaxSizeX); }
        }

        public static int SizeY
        {
            get { return Mathf.Clamp(PlayerPrefs.GetInt(SizeYKey, 20), MinSizeY, MaxSizeY); }
        }

        public static int SizeZ
        {
            get { return Mathf.Clamp(PlayerPrefs.GetInt(SizeZKey, 1), MinSizeZ, MaxSizeZ); }
        }

        public static int ElimMode
        {
            get { return ResolveElimMode(SizeZ, PlayerPrefs.GetInt(ElimModeKey, ElimRows)); }
        }

        /// <summary>
        /// 按深度定下真正生效的消除模式：z = 1 时一层就是一行，层消和行消在这里完全等价，
        /// 一律按行消处理，免得界面上写着「层消」实际干的是行消的事。
        /// 抽成纯函数是为了自检能直接验，不用去动玩家存档。
        /// </summary>
        public static int ResolveElimMode(int sizeZ, int stored)
        {
            if (sizeZ <= 1)
                return ElimRows;
            return Mathf.Clamp(stored, ElimRows, ElimLayer);
        }

        /// <summary>当前尺寸能不能选层消（深度 1 只能行消）。设置页据此把按钮置灰。</summary>
        public static bool CanUseLayerMode
        {
            get { return SizeZ > 1; }
        }

        /// <summary>
        /// 下落速度倍率，范围 (0, 2]，两位小数。1 = 标准（0.8 秒一格），越大掉得越快。
        /// 3D 场地要前后看落点，1 倍速经常来不及，所以做成可调。
        /// </summary>
        public static float FallSpeed
        {
            get { return ClampFallSpeed(PlayerPrefs.GetFloat(FallSpeedKey, DefaultFallSpeed)); }
        }

        /// <summary>夹到 (0, 2] 上并四舍五入到两位小数。</summary>
        public static float ClampFallSpeed(float v)
        {
            if (float.IsNaN(v))
                return DefaultFallSpeed;
            v = Mathf.Clamp(v, MinFallSpeed, MaxFallSpeed);
            return Mathf.Round(v * 100f) / 100f;
        }

        /// <summary>
        /// 改下落速度。不清存档——它不影响已有局面的合法性，中途改了立刻生效就行。
        /// </summary>
        public static void SetFallSpeed(float v)
        {
            PlayerPrefs.SetFloat(FallSpeedKey, ClampFallSpeed(v));
            PlayerPrefs.Save();
        }

        /// <summary>背景音乐音量上限（1 = 满）。默认 0.45：音乐是垫底的，不能盖过放置音。</summary>
        public const float DefaultMusicVolume = 0.45f;

        /// <summary>背景音乐音量 0 ~ 1，0 = 关。两位小数。</summary>
        public static float MusicVolume
        {
            get { return ClampMusicVolume(PlayerPrefs.GetFloat(MusicVolumeKey, DefaultMusicVolume)); }
        }

        public static float ClampMusicVolume(float v)
        {
            if (float.IsNaN(v))
                return DefaultMusicVolume;
            v = Mathf.Clamp01(v);
            return Mathf.Round(v * 100f) / 100f;
        }

        /// <summary>改音乐音量。和下落速度一样不清存档——纯听感，和局面无关。</summary>
        public static void SetMusicVolume(float v)
        {
            PlayerPrefs.SetFloat(MusicVolumeKey, ClampMusicVolume(v));
            PlayerPrefs.Save();
        }

        public static void SetSize(int x, int y, int z)
        {
            int cx = Mathf.Clamp(x, MinSizeX, MaxSizeX);
            int cy = Mathf.Clamp(y, MinSizeY, MaxSizeY);
            int cz = Mathf.Clamp(z, MinSizeZ, MaxSizeZ);
            PlayerPrefs.SetInt(SizeXKey, cx);
            PlayerPrefs.SetInt(SizeYKey, cy);
            PlayerPrefs.SetInt(SizeZKey, cz);
            // 深度缩到 1 就把消除模式一并落成行消。只靠 getter 兜着的话，
            // 存档里还留着「层消」，等玩家把深度再调大时会莫名其妙跳回层消。
            if (cz <= 1)
                PlayerPrefs.SetInt(ElimModeKey, ElimRows);
            PlayerPrefs.Save();
            // 尺寸变了，旧存档的格子数对不上，直接清掉
            GameSave.Clear();
        }

        public static void SetElimMode(int mode)
        {
            // 走一遍 Resolve：深度 1 时传进来的层消会被按行消存下，界面和存档不会对不上
            PlayerPrefs.SetInt(ElimModeKey, ResolveElimMode(SizeZ, mode));
            PlayerPrefs.Save();
            // 消除规则变了，旧存档里的局面按新规则继续容易出错，清掉
            GameSave.Clear();
        }

        // ---------- 特殊方块机制开关 ----------

        /// <summary>
        /// 特殊方块各机制的开关。默认全开。
        ///
        /// 关掉的含义是「这块砖不再按特殊规则动」，方块本身照常进池：
        /// TNT 关掉就成一块普通砖，沙子关掉就不下落，玻璃关掉就压不碎。
        /// 这样玩家想玩哪种规则自己挑，池子里也不会突然少一批方块。
        /// </summary>
        public enum MechKind
        {
            TntExplode = 0,
            ContactIgnite = 1,
            SandFall = 2,
            LeafGrow = 3,
            WaterFlow = 4,
            LavaFlow = 5,
            StructureRestore = 6,
            BedrockAnchor = 7,
            BlastProof = 8,
            GlassCrush = 9,
            SnowMelt = 10,
            Golem = 11,
        }

        /// <summary>开关总数。设置页和自检都按它遍历。</summary>
        public const int MechCount = 12;

        static readonly string[] MechKeys =
        {
            "mech.tnt", "mech.ignite", "mech.sand", "mech.leaves", "mech.water",
            "mech.lava", "mech.structure", "mech.bedrock", "mech.blastproof", "mech.glass",
            "mech.melt", "mech.golem",
        };

        static readonly string[] MechLabels =
        {
            "TNT 爆炸", "接触点火", "沙子与沙砾下落", "原木长树叶", "水会流动",
            "岩浆会流动", "结构方块还原", "基岩钉住不动", "防爆方块炸不掉", "玻璃被压碎",
            "雪与冰遇岩浆融化", "傀儡召唤",
        };

        public static string MechLabel(MechKind kind)
        {
            int i = (int)kind;
            return i >= 0 && i < MechLabels.Length ? MechLabels[i] : "";
        }

        public static bool Mech(MechKind kind)
        {
            int i = (int)kind;
            if (i < 0 || i >= MechKeys.Length)
                return true;
            return PlayerPrefs.GetInt(MechKeys[i], 1) != 0;
        }

        public static void SetMech(MechKind kind, bool on)
        {
            int i = (int)kind;
            if (i < 0 || i >= MechKeys.Length)
                return;
            PlayerPrefs.SetInt(MechKeys[i], on ? 1 : 0);
            PlayerPrefs.Save();
            // 机制开关不影响已经摆好的局面，也不影响池子里有什么，所以不清存档、不重建池
        }

        public static bool TntExplodes { get { return Mech(MechKind.TntExplode); } }
        public static bool ContactIgnites { get { return Mech(MechKind.ContactIgnite); } }
        public static bool SandFalls { get { return Mech(MechKind.SandFall); } }
        public static bool LeavesGrow { get { return Mech(MechKind.LeafGrow); } }
        public static bool WaterFlows { get { return Mech(MechKind.WaterFlow); } }
        public static bool LavaFlows { get { return Mech(MechKind.LavaFlow); } }
        public static bool StructureRestores { get { return Mech(MechKind.StructureRestore); } }
        public static bool BedrockAnchors { get { return Mech(MechKind.BedrockAnchor); } }
        public static bool BlastProofOn { get { return Mech(MechKind.BlastProof); } }
        public static bool GlassCrushes { get { return Mech(MechKind.GlassCrush); } }
        public static bool SnowMelts { get { return Mech(MechKind.SnowMelt); } }
        public static bool GolemsSummon { get { return Mech(MechKind.Golem); } }
    }
}
