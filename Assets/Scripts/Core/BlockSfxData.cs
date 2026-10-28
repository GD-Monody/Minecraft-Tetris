// 由 tools/build_place_sfx.py 自动生成，不要手改。
// 数据来源：32.json 里的 minecraft/sounds.json（block.<组>.place 事件，水和岩浆用 item.bucket.*）。
namespace MinecraftTetris
{
    /// <summary>
    /// 每个方块的「放置音效」分组，和 MC 原版的 SoundType 一一对应。
    /// 一条数据是一组的候选 clip（MC 里同一事件有多个随机变体），
    /// 播放时随机挑一条，和原版一样不会每次都是同一个声音。
    /// </summary>
    public static class BlockSfxData
    {
        public const int GroupCount = 60;

        /// <summary>组名，下标即方块表里的组编号。</summary>
        public static readonly string[] Groups =
        {
            "stone",
            "wood",
            "gravel",
            "grass",
            "sand",
            "wool",
            "metal",
            "iron",
            "netherite_block",
            "ancient_debris",
            "copper",
            "copper_bulb",
            "copper_grate",
            "basalt",
            "deepslate",
            "deepslate_bricks",
            "deepslate_tiles",
            "tuff",
            "tuff_bricks",
            "polished_tuff",
            "calcite",
            "dripstone_block",
            "bone_block",
            "netherrack",
            "nether_bricks",
            "soul_sand",
            "soul_soil",
            "nylium",
            "wart_block",
            "amethyst_block",
            "sculk",
            "sculk_catalyst",
            "lodestone",
            "sponge",
            "wet_sponge",
            "cherry_wood",
            "bamboo_wood",
            "nether_wood",
            "stem",
            "mud",
            "mud_bricks",
            "packed_mud",
            "muddy_mangrove_roots",
            "moss",
            "rooted_dirt",
            "nether_ore",
            "gilded_blackstone",
            "creaking_heart",
            "resin",
            "resin_bricks",
            "chiseled_bookshelf",
            "sulfur",
            "cinnabar",
            "potent_sulfur",
            "suspicious_gravel",
            "suspicious_sand",
            "snow",
            "coral_block",
            "fluid_water",
            "fluid_lava",
        };

        /// <summary>每组可用的 clip（Resources/Sfx/Place 下的文件名）。</summary>
        public static readonly string[][] GroupClips =
        {
            new[] { "dig_stone1", "dig_stone2", "dig_stone3", "dig_stone4" },   // stone
            new[] { "dig_wood1", "dig_wood2", "dig_wood3", "dig_wood4" },   // wood
            new[] { "dig_gravel1", "dig_gravel2", "dig_gravel3", "dig_gravel4" },   // gravel
            new[] { "dig_grass1", "dig_grass2", "dig_grass3", "dig_grass4" },   // grass
            new[] { "dig_sand1", "dig_sand2", "dig_sand3", "dig_sand4" },   // sand
            new[] { "dig_cloth1", "dig_cloth2", "dig_cloth3", "dig_cloth4" },   // wool
            new[] { "dig_stone1", "dig_stone2", "dig_stone3", "dig_stone4" },   // metal
            new[] { "block_iron_break1", "block_iron_break2", "block_iron_break3", "block_iron_break4", "block_iron_break5", "block_iron_break6", "block_iron_break7", "block_iron_break8" },   // iron
            new[] { "block_netherite_break1", "block_netherite_break1", "block_netherite_break2", "block_netherite_break2", "block_netherite_break3", "block_netherite_break3", "block_netherite_break4", "block_netherite_break4" },   // netherite_block
            new[] { "block_ancient_debris_break1", "block_ancient_debris_break2", "block_ancient_debris_break3", "block_ancient_debris_break4", "block_ancient_debris_break5" },   // ancient_debris
            new[] { "block_copper_break1", "block_copper_break2", "block_copper_break3", "block_copper_break4" },   // copper
            new[] { "block_copper_bulb_place1", "block_copper_bulb_place2", "block_copper_bulb_place3", "block_copper_bulb_place4" },   // copper_bulb
            new[] { "block_copper_grate_break1", "block_copper_grate_break1", "block_copper_grate_break2", "block_copper_grate_break3", "block_copper_grate_break4" },   // copper_grate
            new[] { "block_basalt_break1", "block_basalt_break2", "block_basalt_break3", "block_basalt_break4", "block_basalt_break5" },   // basalt
            new[] { "block_deepslate_place1", "block_deepslate_place2", "block_deepslate_place3", "block_deepslate_place4", "block_deepslate_place5", "block_deepslate_place6" },   // deepslate
            new[] { "block_deepslate_bricks_place1", "block_deepslate_bricks_place2", "block_deepslate_bricks_place3", "block_deepslate_bricks_place4", "block_deepslate_bricks_place5", "block_deepslate_bricks_place6" },   // deepslate_bricks
            new[] { "block_deepslate_bricks_place1", "block_deepslate_bricks_place2", "block_deepslate_bricks_place3", "block_deepslate_bricks_place4", "block_deepslate_bricks_place5", "block_deepslate_bricks_place6" },   // deepslate_tiles
            new[] { "block_tuff_break1", "block_tuff_break2", "block_tuff_break3", "block_tuff_break4" },   // tuff
            new[] { "block_tuff_bricks_place1", "block_tuff_bricks_place2", "block_tuff_bricks_place3", "block_tuff_bricks_place4", "block_tuff_bricks_place5" },   // tuff_bricks
            new[] { "block_tuff_break1", "block_tuff_break2", "block_tuff_break3", "block_tuff_break4", "block_tuff_break5" },   // polished_tuff
            new[] { "block_calcite_place1", "block_calcite_place2", "block_calcite_place3", "block_calcite_place4" },   // calcite
            new[] { "block_dripstone_break1", "block_dripstone_break2", "block_dripstone_break3", "block_dripstone_break4", "block_dripstone_break5" },   // dripstone_block
            new[] { "block_bone_block_break1", "block_bone_block_break2", "block_bone_block_break3", "block_bone_block_break4", "block_bone_block_break5" },   // bone_block
            new[] { "block_netherrack_break1", "block_netherrack_break2", "block_netherrack_break3", "block_netherrack_break4", "block_netherrack_break5", "block_netherrack_break6" },   // netherrack
            new[] { "block_nether_bricks_break1", "block_nether_bricks_break2", "block_nether_bricks_break3", "block_nether_bricks_break4", "block_nether_bricks_break5", "block_nether_bricks_break6" },   // nether_bricks
            new[] { "block_soul_sand_break1", "block_soul_sand_break2", "block_soul_sand_break3", "block_soul_sand_break4", "block_soul_sand_break5", "block_soul_sand_break6", "block_soul_sand_break7", "block_soul_sand_break8", "block_soul_sand_break9" },   // soul_sand
            new[] { "block_soul_soil_break1", "block_soul_soil_break2", "block_soul_soil_break3", "block_soul_soil_break4", "block_soul_soil_break5", "block_soul_soil_break6" },   // soul_soil
            new[] { "block_nylium_break1", "block_nylium_break2", "block_nylium_break3", "block_nylium_break4", "block_nylium_break5", "block_nylium_break6" },   // nylium
            new[] { "block_netherwart_break1", "block_netherwart_break2", "block_netherwart_break3", "block_netherwart_break4", "block_netherwart_break5", "block_netherwart_break6" },   // wart_block
            new[] { "block_amethyst_place1", "block_amethyst_place2", "block_amethyst_place3", "block_amethyst_place4" },   // amethyst_block
            new[] { "block_sculk_place1", "block_sculk_place2", "block_sculk_place3", "block_sculk_place4", "block_sculk_place5" },   // sculk
            new[] { "block_sculk_catalyst_place1", "block_sculk_catalyst_place2", "block_sculk_catalyst_place3", "block_sculk_catalyst_place4", "block_sculk_catalyst_place5" },   // sculk_catalyst
            new[] { "block_lodestone_place1", "block_lodestone_place2", "block_lodestone_place3", "block_lodestone_place4" },   // lodestone
            new[] { "block_sponge_break1", "block_sponge_break2", "block_sponge_break3", "block_sponge_break4" },   // sponge
            new[] { "block_sponge_wet_sponge_break1", "block_sponge_wet_sponge_break2", "block_sponge_wet_sponge_break3", "block_sponge_wet_sponge_break4" },   // wet_sponge
            new[] { "block_cherry_wood_break1", "block_cherry_wood_break2", "block_cherry_wood_break3", "block_cherry_wood_break4", "block_cherry_wood_break5" },   // cherry_wood
            new[] { "block_bamboo_wood_break1", "block_bamboo_wood_break2", "block_bamboo_wood_break3", "block_bamboo_wood_break4", "block_bamboo_wood_break5" },   // bamboo_wood
            new[] { "block_nether_wood_break1", "block_nether_wood_break2", "block_nether_wood_break3", "block_nether_wood_break4" },   // nether_wood
            new[] { "block_stem_break1", "block_stem_break2", "block_stem_break3", "block_stem_break4", "block_stem_break5", "block_stem_break6" },   // stem
            new[] { "block_mud_break1", "block_mud_break2", "block_mud_break3", "block_mud_break4", "block_mud_break5", "block_mud_break6" },   // mud
            new[] { "block_mud_bricks_break1", "block_mud_bricks_break2", "block_mud_bricks_break3", "block_mud_bricks_break4", "block_mud_bricks_break5", "block_mud_bricks_break6" },   // mud_bricks
            new[] { "block_packed_mud_break1", "block_packed_mud_break2", "block_packed_mud_break3", "block_packed_mud_break4", "block_packed_mud_break5", "block_packed_mud_break6" },   // packed_mud
            new[] { "block_muddy_mangrove_roots_break1", "block_muddy_mangrove_roots_break2", "block_muddy_mangrove_roots_break3", "block_muddy_mangrove_roots_break4", "block_muddy_mangrove_roots_break5", "block_muddy_mangrove_roots_break6" },   // muddy_mangrove_roots
            new[] { "block_moss_break1", "block_moss_break2", "block_moss_break3", "block_moss_break4", "block_moss_break5" },   // moss
            new[] { "block_rooted_dirt_break1", "block_rooted_dirt_break2", "block_rooted_dirt_break3", "block_rooted_dirt_break4" },   // rooted_dirt
            new[] { "block_nether_ore_break1", "block_nether_ore_break2", "block_nether_ore_break3", "block_nether_ore_break4" },   // nether_ore
            new[] { "block_nether_ore_break1", "block_nether_ore_break2", "block_nether_ore_break3", "block_nether_ore_break4" },   // gilded_blackstone
            new[] { "block_creaking_heart_place_creaking_heart_place1", "block_creaking_heart_place_creaking_heart_place2", "block_creaking_heart_place_creaking_heart_place3", "block_creaking_heart_place_creaking_heart_place4" },   // creaking_heart
            new[] { "block_resin_resin_place1", "block_resin_resin_place2", "block_resin_resin_place3", "block_resin_resin_place4" },   // resin
            new[] { "block_resin_bricks_resin_brick_place1", "block_resin_bricks_resin_brick_place2", "block_resin_bricks_resin_brick_place3", "block_resin_bricks_resin_brick_place4", "block_resin_bricks_resin_brick_place5" },   // resin_bricks
            new[] { "block_chiseled_bookshelf_break1", "block_chiseled_bookshelf_break2", "block_chiseled_bookshelf_break3", "block_chiseled_bookshelf_break4", "block_chiseled_bookshelf_break5", "block_chiseled_bookshelf_break6" },   // chiseled_bookshelf
            new[] { "block_sulfur_place1", "block_sulfur_place2", "block_sulfur_place3", "block_sulfur_place4", "block_sulfur_place5", "block_sulfur_place6", "block_sulfur_place7", "block_sulfur_place8" },   // sulfur
            new[] { "block_cinnabar_place1", "block_cinnabar_place2", "block_cinnabar_place3", "block_cinnabar_place4", "block_cinnabar_place5", "block_cinnabar_place6", "block_cinnabar_place7" },   // cinnabar
            new[] { "block_potent_sulfur_place1", "block_potent_sulfur_place2", "block_potent_sulfur_place3", "block_potent_sulfur_place4", "block_potent_sulfur_place5", "block_potent_sulfur_place6", "block_potent_sulfur_place7", "block_potent_sulfur_place8" },   // potent_sulfur
            new[] { "block_suspicious_gravel_place1", "block_suspicious_gravel_place2", "block_suspicious_gravel_place3", "block_suspicious_gravel_place4" },   // suspicious_gravel
            new[] { "block_suspicious_sand_place1", "block_suspicious_sand_place2", "block_suspicious_sand_place3", "block_suspicious_sand_place4", "block_suspicious_sand_place5" },   // suspicious_sand
            new[] { "dig_snow1", "dig_snow2", "dig_snow3", "dig_snow4" },   // snow
            new[] { "dig_coral1", "dig_coral2", "dig_coral3", "dig_coral4" },   // coral_block
            new[] { "item_bucket_empty1", "item_bucket_empty1", "item_bucket_empty2", "item_bucket_empty3" },   // fluid_water
            new[] { "item_bucket_empty_lava1", "item_bucket_empty_lava2", "item_bucket_empty_lava3" },   // fluid_lava
        };

        /// <summary>每组的基础音量（sounds.json 里个别条目自带 volume）。</summary>
        public static readonly float[] GroupVolume =
        {
            1f,   // stone
            1f,   // wood
            1f,   // gravel
            1f,   // grass
            1f,   // sand
            1f,   // wool
            1f,   // metal
            1f,   // iron
            0.95f,   // netherite_block
            1f,   // ancient_debris
            1f,   // copper
            1f,   // copper_bulb
            1f,   // copper_grate
            1f,   // basalt
            1f,   // deepslate
            0.85f,   // deepslate_bricks
            0.85f,   // deepslate_tiles
            1f,   // tuff
            1f,   // tuff_bricks
            1f,   // polished_tuff
            0.94f,   // calcite
            1f,   // dripstone_block
            0.9f,   // bone_block
            1f,   // netherrack
            0.8f,   // nether_bricks
            1f,   // soul_sand
            0.8f,   // soul_soil
            0.85f,   // nylium
            0.8f,   // wart_block
            1f,   // amethyst_block
            1f,   // sculk
            1f,   // sculk_catalyst
            1f,   // lodestone
            1f,   // sponge
            1f,   // wet_sponge
            1f,   // cherry_wood
            0.9f,   // bamboo_wood
            1f,   // nether_wood
            0.8f,   // stem
            0.5f,   // mud
            0.5f,   // mud_bricks
            0.3f,   // packed_mud
            0.25f,   // muddy_mangrove_roots
            0.93f,   // moss
            1f,   // rooted_dirt
            1f,   // nether_ore
            1f,   // gilded_blackstone
            1f,   // creaking_heart
            1f,   // resin
            1f,   // resin_bricks
            1f,   // chiseled_bookshelf
            1f,   // sulfur
            1f,   // cinnabar
            1f,   // potent_sulfur
            1f,   // suspicious_gravel
            1f,   // suspicious_sand
            1f,   // snow
            1f,   // coral_block
            1f,   // fluid_water
            1f,   // fluid_lava
        };

        /// <summary>每个方块属于哪一组（下标和 BlockCatalogData.Names 对齐）。</summary>
        public static readonly string[] BlockGroup =
        {
            "wood",   // acacia_log
            "wood",   // acacia_planks
            "wood",   // acacia_wood
            "amethyst_block",   // amethyst_block
            "ancient_debris",   // ancient_debris
            "stone",   // andesite
            "bamboo_wood",   // bamboo_block
            "bamboo_wood",   // bamboo_mosaic
            "bamboo_wood",   // bamboo_planks
            "wood",   // barrel
            "basalt",   // basalt
            "wood",   // birch_log
            "wood",   // birch_planks
            "wood",   // birch_wood
            "stone",   // black_concrete
            "sand",   // black_concrete_powder
            "stone",   // black_glazed_terracotta
            "stone",   // black_terracotta
            "wool",   // black_wool
            "stone",   // blackstone
            "stone",   // blast_furnace
            "stone",   // blue_concrete
            "sand",   // blue_concrete_powder
            "stone",   // blue_glazed_terracotta
            "stone",   // blue_terracotta
            "wool",   // blue_wool
            "bone_block",   // bone_block
            "wood",   // bookshelf
            "coral_block",   // brain_coral_block
            "stone",   // bricks
            "stone",   // brown_concrete
            "sand",   // brown_concrete_powder
            "stone",   // brown_glazed_terracotta
            "stone",   // brown_terracotta
            "wool",   // brown_wool
            "coral_block",   // bubble_coral_block
            "amethyst_block",   // budding_amethyst
            "calcite",   // calcite
            "wood",   // cartography_table
            "wood",   // carved_pumpkin
            "stone",   // chain_command_block
            "cherry_wood",   // cherry_log
            "cherry_wood",   // cherry_planks
            "cherry_wood",   // cherry_wood
            "chiseled_bookshelf",   // chiseled_bookshelf
            "cinnabar",   // chiseled_cinnabar
            "copper",   // chiseled_copper
            "deepslate_bricks",   // chiseled_deepslate
            "nether_bricks",   // chiseled_nether_bricks
            "stone",   // chiseled_polished_blackstone
            "stone",   // chiseled_quartz_block
            "stone",   // chiseled_red_sandstone
            "resin_bricks",   // chiseled_resin_bricks
            "stone",   // chiseled_sandstone
            "stone",   // chiseled_stone_bricks
            "sulfur",   // chiseled_sulfur
            "polished_tuff",   // chiseled_tuff
            "tuff_bricks",   // chiseled_tuff_bricks
            "cinnabar",   // cinnabar
            "cinnabar",   // cinnabar_bricks
            "stone",   // clay
            "stone",   // coal_block
            "stone",   // coal_ore
            "grass",   // coarse_dirt
            "deepslate",   // cobbled_deepslate
            "stone",   // cobblestone
            "stone",   // command_block
            "copper",   // copper_block
            "copper_bulb",   // copper_bulb
            "copper_grate",   // copper_grate
            "stone",   // copper_ore
            "deepslate_bricks",   // cracked_deepslate_bricks
            "deepslate_tiles",   // cracked_deepslate_tiles
            "nether_bricks",   // cracked_nether_bricks
            "stone",   // cracked_polished_blackstone_bricks
            "stone",   // cracked_stone_bricks
            "wood",   // crafter
            "wood",   // crafting_table
            "creaking_heart",   // creaking_heart
            "stem",   // crimson_hyphae
            "nylium",   // crimson_nylium
            "nether_wood",   // crimson_planks
            "stem",   // crimson_stem
            "stone",   // crying_obsidian
            "copper",   // cut_copper
            "stone",   // cut_red_sandstone
            "stone",   // cut_sandstone
            "stone",   // cyan_concrete
            "sand",   // cyan_concrete_powder
            "stone",   // cyan_glazed_terracotta
            "stone",   // cyan_terracotta
            "wool",   // cyan_wool
            "wood",   // dark_oak_log
            "wood",   // dark_oak_planks
            "wood",   // dark_oak_wood
            "stone",   // dark_prismarine
            "coral_block",   // dead_brain_coral_block
            "coral_block",   // dead_bubble_coral_block
            "coral_block",   // dead_fire_coral_block
            "coral_block",   // dead_horn_coral_block
            "coral_block",   // dead_tube_coral_block
            "deepslate",   // deepslate
            "deepslate_bricks",   // deepslate_bricks
            "deepslate",   // deepslate_coal_ore
            "deepslate",   // deepslate_copper_ore
            "deepslate",   // deepslate_diamond_ore
            "deepslate",   // deepslate_emerald_ore
            "deepslate",   // deepslate_gold_ore
            "deepslate",   // deepslate_iron_ore
            "deepslate",   // deepslate_lapis_ore
            "deepslate",   // deepslate_redstone_ore
            "deepslate_tiles",   // deepslate_tiles
            "metal",   // diamond_block
            "stone",   // diamond_ore
            "stone",   // diorite
            "grass",   // dirt
            "stone",   // dispenser
            "stone",   // dispenser_vertical
            "grass",   // dried_kelp_block
            "dripstone_block",   // dripstone_block
            "stone",   // dropper
            "stone",   // dropper_vertical
            "metal",   // emerald_block
            "stone",   // emerald_ore
            "stone",   // end_stone
            "stone",   // end_stone_bricks
            "copper",   // exposed_chiseled_copper
            "copper",   // exposed_copper
            "copper_bulb",   // exposed_copper_bulb
            "copper_grate",   // exposed_copper_grate
            "copper",   // exposed_cut_copper
            "coral_block",   // fire_coral_block
            "wood",   // fletching_table
            "stone",   // furnace
            "gilded_blackstone",   // gilded_blackstone
            "metal",   // gold_block
            "stone",   // gold_ore
            "stone",   // granite
            "grass",   // grass_block
            "grass",   // grass_block_snow
            "gravel",   // gravel
            "stone",   // gray_concrete
            "sand",   // gray_concrete_powder
            "stone",   // gray_glazed_terracotta
            "stone",   // gray_terracotta
            "wool",   // gray_wool
            "stone",   // green_concrete
            "sand",   // green_concrete_powder
            "stone",   // green_glazed_terracotta
            "stone",   // green_terracotta
            "wool",   // green_wool
            "grass",   // hay_block
            "coral_block",   // horn_coral_block
            "iron",   // iron_block
            "stone",   // iron_ore
            "wood",   // jack_o_lantern
            "wood",   // jukebox
            "wood",   // jungle_log
            "wood",   // jungle_planks
            "wood",   // jungle_wood
            "stone",   // lapis_block
            "stone",   // lapis_ore
            "fluid_lava",   // lava
            "stone",   // lime_concrete
            "sand",   // lime_concrete_powder
            "stone",   // lime_glazed_terracotta
            "stone",   // lime_terracotta
            "wool",   // lime_wool
            "lodestone",   // lodestone
            "wood",   // loom
            "stone",   // magenta_concrete
            "sand",   // magenta_concrete_powder
            "stone",   // magenta_glazed_terracotta
            "stone",   // magenta_terracotta
            "wool",   // magenta_wool
            "wood",   // mangrove_log
            "wood",   // mangrove_planks
            "wood",   // mangrove_wood
            "wood",   // melon
            "moss",   // moss_block
            "stone",   // mossy_cobblestone
            "stone",   // mossy_stone_bricks
            "mud",   // mud
            "mud_bricks",   // mud_bricks
            "muddy_mangrove_roots",   // muddy_mangrove_roots
            "grass",   // mycelium
            "nether_bricks",   // nether_bricks
            "nether_ore",   // nether_gold_ore
            "nether_ore",   // nether_quartz_ore
            "wart_block",   // nether_wart_block
            "netherite_block",   // netherite_block
            "netherrack",   // netherrack
            "wood",   // note_block
            "wood",   // oak_log
            "wood",   // oak_planks
            "wood",   // oak_wood
            "stone",   // observer
            "stone",   // obsidian
            "stone",   // orange_concrete
            "sand",   // orange_concrete_powder
            "stone",   // orange_glazed_terracotta
            "stone",   // orange_terracotta
            "wool",   // orange_wool
            "copper",   // oxidized_chiseled_copper
            "copper",   // oxidized_copper
            "copper_bulb",   // oxidized_copper_bulb
            "copper_grate",   // oxidized_copper_grate
            "copper",   // oxidized_cut_copper
            "packed_mud",   // packed_mud
            "moss",   // pale_moss_block
            "wood",   // pale_oak_log
            "wood",   // pale_oak_planks
            "wood",   // pale_oak_wood
            "stone",   // pink_concrete
            "sand",   // pink_concrete_powder
            "stone",   // pink_glazed_terracotta
            "stone",   // pink_terracotta
            "wool",   // pink_wool
            "stone",   // piston
            "grass",   // podzol
            "stone",   // polished_andesite
            "basalt",   // polished_basalt
            "stone",   // polished_blackstone
            "stone",   // polished_blackstone_bricks
            "cinnabar",   // polished_cinnabar
            "deepslate",   // polished_deepslate
            "stone",   // polished_diorite
            "stone",   // polished_granite
            "sulfur",   // polished_sulfur
            "polished_tuff",   // polished_tuff
            "potent_sulfur",   // potent_sulfur
            "stone",   // prismarine
            "stone",   // prismarine_bricks
            "wood",   // pumpkin
            "stone",   // purple_concrete
            "sand",   // purple_concrete_powder
            "stone",   // purple_glazed_terracotta
            "stone",   // purple_terracotta
            "wool",   // purple_wool
            "stone",   // purpur_block
            "stone",   // purpur_pillar
            "stone",   // quartz_block
            "stone",   // quartz_bricks
            "stone",   // quartz_pillar
            "stone",   // raw_copper_block
            "stone",   // raw_gold_block
            "stone",   // raw_iron_block
            "stone",   // red_concrete
            "sand",   // red_concrete_powder
            "stone",   // red_glazed_terracotta
            "nether_bricks",   // red_nether_bricks
            "sand",   // red_sand
            "stone",   // red_sandstone
            "stone",   // red_terracotta
            "wool",   // red_wool
            "stone",   // redstone_block
            "stone",   // redstone_lamp
            "stone",   // redstone_ore
            "deepslate",   // reinforced_deepslate
            "stone",   // repeating_command_block
            "resin",   // resin_block
            "resin_bricks",   // resin_bricks
            "stone",   // respawn_anchor_0
            "rooted_dirt",   // rooted_dirt
            "sand",   // sand
            "stone",   // sandstone
            "sculk",   // sculk
            "sculk_catalyst",   // sculk_catalyst
            "wood",   // smithing_table
            "stone",   // smoker
            "basalt",   // smooth_basalt
            "stone",   // smooth_quartz
            "stone",   // smooth_red_sandstone
            "stone",   // smooth_sandstone
            "stone",   // smooth_stone
            "stone",   // smooth_stone_slab_double
            "snow",   // snow_block
            "soul_sand",   // soul_sand
            "soul_soil",   // soul_soil
            "sponge",   // sponge
            "wood",   // spruce_log
            "wood",   // spruce_planks
            "wood",   // spruce_wood
            "stone",   // sticky_piston
            "stone",   // stone
            "stone",   // stone_bricks
            "wood",   // stripped_acacia_log
            "wood",   // stripped_acacia_wood
            "bamboo_wood",   // stripped_bamboo_block
            "wood",   // stripped_birch_log
            "wood",   // stripped_birch_wood
            "cherry_wood",   // stripped_cherry_log
            "cherry_wood",   // stripped_cherry_wood
            "stem",   // stripped_crimson_hyphae
            "stem",   // stripped_crimson_stem
            "wood",   // stripped_dark_oak_log
            "wood",   // stripped_dark_oak_wood
            "wood",   // stripped_jungle_log
            "wood",   // stripped_jungle_wood
            "wood",   // stripped_mangrove_log
            "wood",   // stripped_mangrove_wood
            "wood",   // stripped_oak_log
            "wood",   // stripped_oak_wood
            "wood",   // stripped_pale_oak_log
            "wood",   // stripped_pale_oak_wood
            "wood",   // stripped_spruce_log
            "wood",   // stripped_spruce_wood
            "stem",   // stripped_warped_hyphae
            "stem",   // stripped_warped_stem
            "stone",   // structure_block
            "sulfur",   // sulfur
            "sulfur",   // sulfur_bricks
            "suspicious_gravel",   // suspicious_gravel_0
            "suspicious_sand",   // suspicious_sand_0
            "grass",   // target
            "stone",   // terracotta
            "grass",   // tnt
            "coral_block",   // tube_coral_block
            "tuff",   // tuff
            "tuff_bricks",   // tuff_bricks
            "stem",   // warped_hyphae
            "nylium",   // warped_nylium
            "nether_wood",   // warped_planks
            "stem",   // warped_stem
            "wart_block",   // warped_wart_block
            "fluid_water",   // water
            "copper",   // weathered_chiseled_copper
            "copper",   // weathered_copper
            "copper_bulb",   // weathered_copper_bulb
            "copper_grate",   // weathered_copper_grate
            "copper",   // weathered_cut_copper
            "wet_sponge",   // wet_sponge
            "stone",   // white_concrete
            "sand",   // white_concrete_powder
            "stone",   // white_glazed_terracotta
            "stone",   // white_terracotta
            "wool",   // white_wool
            "stone",   // yellow_concrete
            "sand",   // yellow_concrete_powder
            "stone",   // yellow_glazed_terracotta
            "stone",   // yellow_terracotta
            "wool",   // yellow_wool
            "grass",   // oak_leaves
            "grass",   // spruce_leaves
            "grass",   // birch_leaves
            "grass",   // jungle_leaves
            "grass",   // acacia_leaves
            "grass",   // dark_oak_leaves
            "grass",   // mangrove_leaves
            "grass",   // cherry_leaves
            "grass",   // pale_oak_leaves
            "stone",   // bedrock
        };
    }
}
