using System.IO;
using System.Linq;
using RPGStarter.Data;
using RPGStarter.UI;
using RPGStarter.World;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace RPGStarter.EditorTools
{
    /// <summary>
    /// W3 mining-system assets — counterpart to <see cref="W2_AssetBuilder"/> for combat:
    ///
    ///   * ToolKind_Pickaxe / ToolKind_Hammer / ToolKind_Axe — tag SOs used to gate
    ///     which weapon kinds count as hits on a node;
    ///   * Item_Rock + Item_Wood — material items the inventory tracks;
    ///   * Inventory_Player — runtime inventory store (counts are NonSerialized);
    ///   * RewardPopup.prefab — pooled "+N" popup, gold + bigger on the special roll;
    ///   * RockPickup.prefab — bobbing pickup the player walks over;
    ///   * RockChipBurst_Hit.prefab / RockChipBurst_Break.prefab — chip-burst VFX,
    ///     small/few/short on every successful strike vs large/many/long on shatter;
    ///   * Rock_Mineable.prefab — breakable rock node (collider + Rigidbody + RockMineable),
    ///     visual sourced from Polyquest's Rock_Blocky_A and scaled to ~half player height.
    ///
    /// Idempotent. Re-running refreshes refs but won't trample artist tuning on pre-existing
    /// SOs (the `firstRun` pattern from W2_WeaponBuilder).
    /// </summary>
    public static class W3_AssetBuilder
    {
        // ── SO paths ─────────────────────────────────────────────────────────
        public const string TOOLKIND_PICKAXE       = "Assets/_Project/ScriptableObjects/GameConfig/ToolKind_Pickaxe.asset";
        public const string TOOLKIND_HAMMER        = "Assets/_Project/ScriptableObjects/GameConfig/ToolKind_Hammer.asset";
        public const string TOOLKIND_AXE           = "Assets/_Project/ScriptableObjects/GameConfig/ToolKind_Axe.asset";
        public const string ITEM_ROCK              = "Assets/_Project/ScriptableObjects/Items/Item_Rock.asset";
        public const string ITEM_WOOD              = "Assets/_Project/ScriptableObjects/Items/Item_Wood.asset";
        public const string ITEM_CRAFTING_TABLE    = "Assets/_Project/ScriptableObjects/Items/Item_CraftingTable.asset";
        public const string INVENTORY_PLAYER       = "Assets/_Project/ScriptableObjects/Items/Inventory_Player.asset";
        public const string RECIPE_CRAFTING_TABLE  = "Assets/_Project/ScriptableObjects/Recipes/Recipe_CraftingTable.asset";
        public const string ABILITY_PLACE_TABLE    = "Assets/_Project/ScriptableObjects/Abilities/Ability_PlaceCraftingTable.asset";
        public const string WEAPON_CRAFTING_TABLE  = "Assets/_Project/ScriptableObjects/Weapons/Weapon_CraftingTable.asset";

        // ── Prefab paths ─────────────────────────────────────────────────────
        public const string PREFAB_REWARD_POPUP      = "Assets/_Project/Prefabs/UI/RewardPopup.prefab";
        public const string PREFAB_ROCK_PICKUP       = "Assets/_Project/Prefabs/Environment/RockPickup.prefab";
        public const string PREFAB_ROCK_NODE         = "Assets/_Project/Prefabs/Environment/Rock_Mineable.prefab";
        public const string PREFAB_CHIPS_HIT         = "Assets/_Project/Prefabs/VFX/RockChipBurst_Hit.prefab";
        public const string PREFAB_CHIPS_BREAK       = "Assets/_Project/Prefabs/VFX/RockChipBurst_Break.prefab";
        public const string PREFAB_INV_SLOT          = "Assets/_Project/Prefabs/UI/InventorySlotView.prefab";
        public const string PREFAB_TABLE_PLACED      = "Assets/_Project/Prefabs/Environment/CraftingTable_Placed.prefab";
        public const string PREFAB_TABLE_HELD        = "Assets/_Project/Prefabs/Weapons/CraftingTable_Held.prefab";
        public const string PREFAB_WOOD_PICKUP       = "Assets/_Project/Prefabs/Environment/WoodPickup.prefab";
        public const string PREFAB_TREE_NODE         = "Assets/_Project/Prefabs/Environment/Tree_Choppable.prefab";
        public const string PREFAB_WOOD_CHIPS_HIT    = "Assets/_Project/Prefabs/VFX/WoodChipBurst_Hit.prefab";
        public const string PREFAB_WOOD_CHIPS_BREAK  = "Assets/_Project/Prefabs/VFX/WoodChipBurst_Break.prefab";
        public const string PREFAB_TREE_STUMP        = "Assets/_Project/Prefabs/Environment/TreeStump.prefab";
        public const string PREFAB_ROCK_BASE         = "Assets/_Project/Prefabs/Environment/RockBase.prefab";

        // Demo branch: primitive stand-in replaces the licensed Table pack.
        private const string SOURCE_TABLE_PREFAB = Demo_PrimitiveAssets.SRC_TABLE;
        // Table textures no longer exist (Table pack removed). The table-material build
        // guards null textures, so M_Table just stays a flat colour — no pink.
        private const string TABLE_TEX_ALBEDO   = "";
        private const string TABLE_TEX_NORMAL   = "";
        private const string TABLE_TEX_EMISSION = "";
        private const string TABLE_MAT_PATH     = "Assets/_Project/Art/Materials/M_Table.mat";

        // Brown URP/Lit material for the tree stump. We build a dedicated material rather
        // than re-using the Synty tree-chunk material because the tree material's UV map
        // is tuned to the full tree mesh; on a Cylinder primitive it renders patchy / grey.
        // A flat brown reads cleanly as "stump in soil".
        private const string TREE_STUMP_MAT_PATH = "Assets/_Project/Art/Materials/M_TreeStump.mat";

        // ── Weapon-item paths (one per WeaponDefinitionSO) ───────────────────
        private const string DIR_ITEMS = "Assets/_Project/ScriptableObjects/Items";

        // ── Icon source paths (Pixel Art Icon Pack RPG) ──────────────────────
        // Note literal-space folder names — match disk paths verbatim.
        private const string ICON_DIR_TOOLS    = "Assets/_Project/Art/Icons/Pixel Art Icon Pack RPG/Weapon & Tool";
        private const string ICON_DIR_ORE      = "Assets/_Project/Art/Icons/Pixel Art Icon Pack RPG/Ore & Gem";
        private const string ICON_DIR_MATERIAL = "Assets/_Project/Art/Icons/Pixel Art Icon Pack RPG/Material";
        private const string ICON_DIR_MISC     = "Assets/_Project/Art/Icons/Pixel Art Icon Pack RPG/Misc";

        // Inventory layout — main grid is 10×4 = 40 slots, hotbar is 10 slots = 50 total.
        // Hotbar starts at slot 40 (digit 1). Hotbar keys are 1-9 + 0 (10 slots).
        public  const int INVENTORY_CAPACITY    = 50;
        public  const int INVENTORY_HOTBAR_START = 40;
        public  const int INVENTORY_HOTBAR_SIZE  = 10;

        // Ordered weapon list. Order = inventory slot priority for the starting roster.
        // Each entry: weapon SO path, item SO path, icon file name (under ICON_DIR_TOOLS).
        private static readonly (string weaponPath, string itemPath, string iconFile, string display)[] WEAPON_ITEMS =
        {
            ("Assets/_Project/ScriptableObjects/Weapons/Weapon_Bow.asset",         DIR_ITEMS + "/Item_Weapon_Bow.asset",         "Bow.png",          "Bow"),
            ("Assets/_Project/ScriptableObjects/Weapons/Weapon_Axe.asset",         DIR_ITEMS + "/Item_Weapon_Axe.asset",         "Axe.png",          "Axe"),
            ("Assets/_Project/ScriptableObjects/Weapons/Weapon_Mace.asset",        DIR_ITEMS + "/Item_Weapon_Mace.asset",        "Hammer.png",       "Mace"),
            ("Assets/_Project/ScriptableObjects/Weapons/Weapon_Spear.asset",       DIR_ITEMS + "/Item_Weapon_Spear.asset",       "Wooden Staff.png", "Spear"),
            ("Assets/_Project/ScriptableObjects/Weapons/Weapon_SwordShield.asset", DIR_ITEMS + "/Item_Weapon_SwordShield.asset", "Iron Sword.png",   "Sword & Shield"),
            ("Assets/_Project/ScriptableObjects/Weapons/Weapon_Hammer.asset",      DIR_ITEMS + "/Item_Weapon_Hammer.asset",      "Hammer.png",       "Hammer"),
            ("Assets/_Project/ScriptableObjects/Weapons/Weapon_Pickaxe.asset",     DIR_ITEMS + "/Item_Weapon_Pickaxe.asset",     "Pickaxe.png",      "Pickaxe"),
            ("Assets/_Project/ScriptableObjects/Weapons/Weapon_AxeTool.asset",     DIR_ITEMS + "/Item_Weapon_AxeTool.asset",     "Axe.png",          "Axe (Tool)"),
        };

        // ── Source visual (PolyquestWorlds) ──────────────────────────────────
        // Rock_Blocky_A is a chunky boulder ~1.5m tall in source — close enough to half
        // player height (~0.95m) that a uniform scale brings it into spec without losing
        // silhouette detail.
        // Demo branch: primitive stand-in replaces the licensed PolyquestWorlds rock.
        private const string SOURCE_ROCK_PREFAB = Demo_PrimitiveAssets.SRC_ROCK;

        // Demo branch: primitive stand-in (cylinder trunk + sphere canopy) replaces
        // the licensed Synty tree.
        private const string SOURCE_TREE_PREFAB = Demo_PrimitiveAssets.SRC_TREE;

        // Half player height target for the rock node — Phantom Archer rig is ~1.85m, so
        // 0.95m gives a chest-high mining target (CLAUDE.md spec: "half the height of the
        // player").
        private const float TARGET_ROCK_HEIGHT = 0.95f;

        // Smaller pickup — about a third of the node so it reads as "a chunk" of the rock.
        private const float TARGET_PICKUP_HEIGHT = 0.32f;

        // Trees stand a head above the player so they read as obstacles, not pickups.
        private const float TARGET_TREE_HEIGHT       = 3.6f;
        // Wood pickup is bigger than the rock pickup — wood logs aren't as dense.
        private const float TARGET_WOOD_PICKUP_HEIGHT = 0.4f;

        // Bumped whenever the layout changes. Logged at build start + end so we can spot
        // "stale compiled DLL ran an old menu" issues from the Console alone.
        private const string BUILDER_VERSION = "v11 (branch-falling tree hits, no break-burst, brown stump)";

        // [MenuItem stripped — single entry point is RPGStarter/Build Demo]
        public static void Build()
        {
            Debug.Log($"[W3_AssetBuilder] BEGIN — {BUILDER_VERSION}");

            EnsureDir(Path.GetDirectoryName(TOOLKIND_PICKAXE));
            EnsureDir(Path.GetDirectoryName(ITEM_ROCK));
            EnsureDir(Path.GetDirectoryName(PREFAB_REWARD_POPUP));
            EnsureDir(Path.GetDirectoryName(PREFAB_ROCK_PICKUP));
            EnsureDir(Path.GetDirectoryName(PREFAB_ROCK_NODE));
            EnsureDir(Path.GetDirectoryName(PREFAB_CHIPS_HIT));

            // Tool kinds (tag-only).
            var toolPickaxe = LoadOrCreate<ToolKindSO>(TOOLKIND_PICKAXE, t =>
            {
                t.displayName = "Pickaxe";
                t.description = "Strikes rocks. Required to mine stone nodes.";
            });
            LoadOrCreate<ToolKindSO>(TOOLKIND_HAMMER, t =>
            {
                t.displayName = "Hammer";
                t.description = "Heavy crushing blows. Future use: smith stations.";
            });
            var toolAxe = LoadOrCreate<ToolKindSO>(TOOLKIND_AXE, t =>
            {
                t.displayName = "Axe";
                t.description = "Chops wood. Required to fell tree nodes.";
            });

            // Reimport every icon used below as Sprite (2D and UI). Pixel Art icons ship
            // as Default textures by default — Image components silently render them as
            // empty quads if not flipped to Sprite mode.
            ConfigureSpriteImporter(IconPath(ICON_DIR_ORE,      "Silver Nugget.png"));
            ConfigureSpriteImporter(IconPath(ICON_DIR_MATERIAL, "Wood Log.png"));
            ConfigureSpriteImporter(IconPath(ICON_DIR_MISC,     "Crate.png"));
            foreach (var entry in WEAPON_ITEMS)
                ConfigureSpriteImporter(IconPath(ICON_DIR_TOOLS, entry.iconFile));

            // Material items (icons assigned every run so renamed/relocated PNGs don't
            // leave a stale ref).
            var rockItem = LoadOrCreate<ItemDefinitionSO>(ITEM_ROCK, i =>
            {
                i.displayName = "Rock";
                i.description = "A chunk of stone. Use it to craft, build, or weigh down a rope.";
                i.maxStack    = 99;
            });
            rockItem.icon = LoadSprite(IconPath(ICON_DIR_ORE, "Silver Nugget.png"));
            EditorUtility.SetDirty(rockItem);

            var woodItem = LoadOrCreate<ItemDefinitionSO>(ITEM_WOOD, i =>
            {
                i.displayName = "Wood";
                i.description = "A length of timber. Chopped from trees.";
                i.maxStack    = 99;
            });
            woodItem.icon = LoadSprite(IconPath(ICON_DIR_MATERIAL, "Wood Log.png"));
            EditorUtility.SetDirty(woodItem);

            // Crafted item: workbench / crafting table. Crate icon stands in for the
            // workbench in the inventory grid until we have a real workbench sprite.
            var craftingTableItem = LoadOrCreate<ItemDefinitionSO>(ITEM_CRAFTING_TABLE, i =>
            {
                i.displayName = "Crafting Table";
                i.description = "A wooden bench. Equip in the hotbar, click to place — interact (E) to craft 3×3 recipes.";
                i.maxStack    = 99;
            });
            craftingTableItem.icon = LoadSprite(IconPath(ICON_DIR_MISC, "Crate.png"));

            // Placeable prefab + held weapon + place ability. Wired here so the item asset
            // points back at the weapon it equips (linkedWeapon) and the player can put it
            // into their hotbar.
            var dtPhysical    = AssetDatabase.LoadAssetAtPath<DamageTypeSO>(W2_AssetBuilder.DT_PHYSICAL);
            var slashClip     = W2_AnimatorPatcher.LoadFirstClip(W2_AnimatorPatcher.CLIP_ATTACK_PLACEHOLDER);
            var tablePlaced   = BuildPlacedCraftingTablePrefab();
            var placeAbility  = EnsurePlaceAbility(tablePlaced, craftingTableItem, dtPhysical);
            var craftingTableWeapon = EnsureCraftingTableWeapon(slashClip, placeAbility);

            craftingTableItem.linkedWeapon = craftingTableWeapon;
            EditorUtility.SetDirty(craftingTableItem);

            // Recipes — first one: 4 wood → 1 crafting table.
            EnsureDir(Path.GetDirectoryName(RECIPE_CRAFTING_TABLE));
            var craftingTableRecipe = LoadOrCreate<RecipeSO>(RECIPE_CRAFTING_TABLE, r =>
            {
                r.displayName = "Crafting Table";
                r.description = "Lash four wood together into a workbench.";
            });
            craftingTableRecipe.outputItem  = craftingTableItem;
            craftingTableRecipe.outputCount = 1;
            craftingTableRecipe.inputs      = new[]
            {
                new RecipeSO.Ingredient { item = woodItem, count = 4 },
            };
            EditorUtility.SetDirty(craftingTableRecipe);

            // Weapon-items: one ItemDefinitionSO per WeaponDefinitionSO so the inventory
            // grid can render the player's roster alongside materials.
            var weaponItems = new System.Collections.Generic.List<ItemDefinitionSO>();
            foreach (var entry in WEAPON_ITEMS)
            {
                var weaponSO = AssetDatabase.LoadAssetAtPath<WeaponDefinitionSO>(entry.weaponPath);
                if (weaponSO == null)
                {
                    Debug.LogWarning($"[W3_AssetBuilder] Weapon SO missing at {entry.weaponPath} — skipping its item asset. " +
                                     "Run RPGStarter/W2/5 (and W3/2 for tools) first.");
                    continue;
                }
                var weaponItem = LoadOrCreate<ItemDefinitionSO>(entry.itemPath, _ => { });
                weaponItem.displayName  = entry.display;
                weaponItem.description  = "Equippable weapon.";
                weaponItem.maxStack     = 1;
                weaponItem.icon         = LoadSprite(IconPath(ICON_DIR_TOOLS, entry.iconFile));
                weaponItem.linkedWeapon = weaponSO;
                EditorUtility.SetDirty(weaponItem);
                weaponItems.Add(weaponItem);
            }

            // Inventory: 40 slots. Weapons go straight into the hotbar (slots 32-39 →
            // digits 1-8). Wood drops into the main inventory via Add. Crafting table
            // doesn't start populated — the player crafts it.
            // Re-bind every run so deleted/recreated items don't leave stale null entries.
            var inventory = LoadOrCreate<InventorySO>(INVENTORY_PLAYER, _ => { });
            inventory.capacity     = INVENTORY_CAPACITY;
            inventory.trackedItems = new System.Collections.Generic.List<ItemDefinitionSO> { rockItem, woodItem, craftingTableItem };
            inventory.startingItems = new System.Collections.Generic.List<InventorySO.StartingItem>();
            for (int i = 0; i < weaponItems.Count; i++)
            {
                inventory.startingItems.Add(new InventorySO.StartingItem
                {
                    item      = weaponItems[i],
                    count     = 1,
                    slotIndex = INVENTORY_HOTBAR_START + i,   // explicit hotbar position
                });
            }
            inventory.startingItems.Add(new InventorySO.StartingItem
            {
                item      = woodItem,
                count     = 10,
                slotIndex = -1,                               // -1 = first available via Add
            });
            EditorUtility.SetDirty(inventory);

            // VFX: harvest the source rock's mesh + material so chips look like the parent.
            (Mesh rockChunkMesh, Material rockChunkMaterial) = HarvestLargestRendererVisual(SOURCE_ROCK_PREFAB);
            var rockChipsHit   = BuildChipBurstPrefab(PREFAB_CHIPS_HIT,   rockChunkMesh, rockChunkMaterial, mode: BurstMode.HitSpeck);
            var rockChipsBreak = BuildChipBurstPrefab(PREFAB_CHIPS_BREAK, rockChunkMesh, rockChunkMaterial, mode: BurstMode.BreakPuff);

            // For wood, swap the chunk MESH to a cube primitive — the tree's full mesh
            // scaled tiny renders as miniature trees flying off, which reads as bizarre.
            // The tree material on a cube still looks bark-coloured.
            (Mesh _, Material treeChunkMaterial) = HarvestLargestRendererVisual(SOURCE_TREE_PREFAB);
            Mesh cubeMesh = GetPrimitiveMesh(PrimitiveType.Cube);
            // Wood "hit" uses the branch mode — chunks are large enough to read as little
            // branches, slow enough to look like they're FALLING (not exploding), and live
            // long enough to be seen.
            var woodChipsHit   = BuildChipBurstPrefab(PREFAB_WOOD_CHIPS_HIT,   cubeMesh, treeChunkMaterial, mode: BurstMode.HitBranches);
            // WoodChipBurst_Break is still authored so older scene refs / verification rows
            // don't go red, but the tree no longer references it on break — see below.
            BuildChipBurstPrefab(PREFAB_WOOD_CHIPS_BREAK, cubeMesh, treeChunkMaterial, mode: BurstMode.BreakPuff);

            // Residual props left after the node breaks. The stump gets a dedicated flat-
            // brown material — see TREE_STUMP_MAT_PATH note above.
            var stumpMaterial = EnsureSimpleColorMaterial(TREE_STUMP_MAT_PATH, new Color(0.35f, 0.22f, 0.12f));
            var treeStump = BuildTreeStumpPrefab(stumpMaterial);
            var rockBase  = BuildRockBasePrefab(rockChunkMaterial);

            // Prefabs.
            BuildRewardPopupPrefab();
            var rockPickup = BuildRockPickupPrefab(rockItem);
            BuildRockNodePrefab(toolPickaxe, rockItem, rockPickup, rockChipsHit, rockChipsBreak, rockBase);
            var woodPickup = BuildWoodPickupPrefab(woodItem);
            // Trees: no break-burst — the user found the explosion of shards unrealistic.
            // Per-hit chips still fire (woodChipsHit) for feedback; on the breaking hit the
            // tree simply vanishes and the stump remains. Cleaner read.
            BuildTreeNodePrefab(toolAxe, woodItem, woodPickup, woodChipsHit, /*chipsBreak*/ null, treeStump);
            BuildInventorySlotViewPrefab();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // Post-build verification — surfaces the exact symptom the user saw last time
            // (Item_Wood missing, chip prefabs missing) directly in the Console.
            VerifyAssetExists("Item_Rock",            ITEM_ROCK);
            VerifyAssetExists("Item_Wood",            ITEM_WOOD);
            VerifyAssetExists("Item_CraftingTable",   ITEM_CRAFTING_TABLE);
            VerifyAssetExists("Recipe_CraftingTable", RECIPE_CRAFTING_TABLE);
            VerifyAssetExists("Inventory_Player",     INVENTORY_PLAYER);
            VerifyAssetExists("Weapon_CraftingTable", WEAPON_CRAFTING_TABLE);
            VerifyAssetExists("Ability_PlaceTable",   ABILITY_PLACE_TABLE);
            VerifyAssetExists("RockChipBurst_Hit",    PREFAB_CHIPS_HIT);
            VerifyAssetExists("RockChipBurst_Break",  PREFAB_CHIPS_BREAK);
            VerifyAssetExists("Rock_Mineable",        PREFAB_ROCK_NODE);
            VerifyAssetExists("WoodChipBurst_Hit",    PREFAB_WOOD_CHIPS_HIT);
            VerifyAssetExists("WoodChipBurst_Break",  PREFAB_WOOD_CHIPS_BREAK);
            VerifyAssetExists("WoodPickup",           PREFAB_WOOD_PICKUP);
            VerifyAssetExists("Tree_Choppable",       PREFAB_TREE_NODE);
            VerifyAssetExists("TreeStump",            PREFAB_TREE_STUMP);
            VerifyAssetExists("RockBase",             PREFAB_ROCK_BASE);
            VerifyAssetExists("CraftingTable_Placed", PREFAB_TABLE_PLACED);
            VerifyAssetExists("InventorySlotView",    PREFAB_INV_SLOT);
            foreach (var entry in WEAPON_ITEMS)
                VerifyAssetExists(System.IO.Path.GetFileNameWithoutExtension(entry.itemPath), entry.itemPath);
            Debug.Log($"[W3_AssetBuilder] END — {BUILDER_VERSION}. " +
                      "If Item_Wood / chip-burst rows say MISSING you're running a stale compiled DLL — " +
                      "switch to Unity, wait for 'Compiling…' to finish, then re-run RPGStarter/W3/Build Everything.");
        }

        private static void VerifyAssetExists(string label, string path)
        {
            bool ok = AssetDatabase.LoadAssetAtPath<Object>(path) != null;
            Debug.Log($"[W3_AssetBuilder]   {(ok ? "OK     " : "MISSING")} {label} → {path}");
        }

        // ── Prefab builders ──────────────────────────────────────────────────

        private static GameObject BuildRewardPopupPrefab()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_REWARD_POPUP) != null)
                AssetDatabase.DeleteAsset(PREFAB_REWARD_POPUP);

            var go = new GameObject("RewardPopup");
            try
            {
                var tmpGo = new GameObject("Text");
                tmpGo.transform.SetParent(go.transform, false);
                var tmp = tmpGo.AddComponent<TextMeshPro>();
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.fontSize  = 5f;
                tmp.text      = "0";
                tmp.color     = Color.white;

                var rect = tmpGo.GetComponent<RectTransform>();
                if (rect != null) rect.sizeDelta = new Vector2(3f, 1.4f);

                var popup = go.AddComponent<RewardPopup>();
                SetSerializedRef(popup, "tmp", tmp);

                return PrefabUtility.SaveAsPrefabAsset(go, PREFAB_REWARD_POPUP);
            }
            finally { Object.DestroyImmediate(go); }
        }

        private static GameObject BuildRockPickupPrefab(ItemDefinitionSO rockItem)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_ROCK_PICKUP) != null)
                AssetDatabase.DeleteAsset(PREFAB_ROCK_PICKUP);

            var sourceRock = AssetDatabase.LoadAssetAtPath<GameObject>(SOURCE_ROCK_PREFAB);
            if (sourceRock == null)
            {
                Debug.LogError($"[W3_AssetBuilder] Source rock prefab missing at {SOURCE_ROCK_PREFAB}.");
                return null;
            }

            var go = new GameObject("RockPickup");
            try
            {
                go.layer = W2_LayerSetup.LAYER_ENVIRONMENT;

                var visual = (GameObject)PrefabUtility.InstantiatePrefab(sourceRock, go.transform);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                FitVisualToHeight(visual, TARGET_PICKUP_HEIGHT);

                var pickup = go.AddComponent<ItemPickup>();
                SetSerializedRef(pickup, "item",   rockItem);
                SetSerializedInt(pickup, "amount", 1);

                return PrefabUtility.SaveAsPrefabAsset(go, PREFAB_ROCK_PICKUP);
            }
            finally { Object.DestroyImmediate(go); }
        }

        /// <summary>
        /// Tuning preset selector for <see cref="BuildChipBurstPrefab"/>. Three distinct
        /// "feels":
        ///   • HitSpeck    — tiny stone shards on each pickaxe hit. Barely-there flecks.
        ///   • HitBranches — small wood chunks falling on each axe hit. Readable as
        ///                   "branches dropping" — bigger + slower + longer-lived than
        ///                   HitSpeck so the player actually sees them on a tree.
        ///   • BreakPuff   — final shatter on rock break. Tree break uses none (stump only).
        /// </summary>
        private enum BurstMode { HitSpeck, HitBranches, BreakPuff }

        private static GameObject BuildChipBurstPrefab(
            string path, Mesh chunkMesh, Material chunkMaterial, BurstMode mode)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
                AssetDatabase.DeleteAsset(path);

            var go = new GameObject(Path.GetFileNameWithoutExtension(path));
            try
            {
                go.layer = W2_LayerSetup.LAYER_ENVIRONMENT;
                var burst = go.AddComponent<RockChipBurst>();

                SetSerializedRef(burst, "chunkMesh",     chunkMesh);
                SetSerializedRef(burst, "chunkMaterial", chunkMaterial);

                switch (mode)
                {
                    case BurstMode.BreakPuff:
                        // Final shatter — a small puff, not a confetti cannon. Player has
                        // already seen 4 hits' worth of chips; this just punctuates the break.
                        SetSerializedInt  (burst, "chunkCount",     4);
                        SetSerializedFloat(burst, "chunkScaleMin",  0.10f);
                        SetSerializedFloat(burst, "chunkScaleMax",  0.18f);
                        SetSerializedFloat(burst, "burstSpeed",     4f);
                        SetSerializedFloat(burst, "burstUpBias",    1.2f);
                        SetSerializedFloat(burst, "spawnRadius",    0.2f);
                        SetSerializedFloat(burst, "chunkLifetime",  1.4f);
                        break;

                    case BurstMode.HitBranches:
                        // Per-hit on a tree — three small branch-sized chunks that gently
                        // drop downward + outward. Cubes at 0.12-0.20 read as little
                        // branches on a tree-sized silhouette; lower upward bias + slower
                        // burst speed sells "falling" rather than "exploding".
                        SetSerializedInt  (burst, "chunkCount",     3);
                        SetSerializedFloat(burst, "chunkScaleMin",  0.12f);
                        SetSerializedFloat(burst, "chunkScaleMax",  0.20f);
                        SetSerializedFloat(burst, "burstSpeed",     2.2f);
                        SetSerializedFloat(burst, "burstUpBias",    0.5f);
                        SetSerializedFloat(burst, "spawnRadius",    0.15f);
                        SetSerializedFloat(burst, "chunkLifetime",  1.6f);
                        break;

                    case BurstMode.HitSpeck:
                    default:
                        // Per-hit on a rock — 2 specks flying off, short-lived. Anything
                        // more reads as "a hundred mini-rocks exploded" instead of
                        // "shard flew off the rock".
                        SetSerializedInt  (burst, "chunkCount",     2);
                        SetSerializedFloat(burst, "chunkScaleMin",  0.05f);
                        SetSerializedFloat(burst, "chunkScaleMax",  0.09f);
                        SetSerializedFloat(burst, "burstSpeed",     3f);
                        SetSerializedFloat(burst, "burstUpBias",    1.2f);
                        SetSerializedFloat(burst, "spawnRadius",    0.06f);
                        SetSerializedFloat(burst, "chunkLifetime",  0.7f);
                        break;
                }

                return PrefabUtility.SaveAsPrefabAsset(go, path);
            }
            finally { Object.DestroyImmediate(go); }
        }

        private static GameObject BuildRockNodePrefab(
            ToolKindSO        requiredTool,
            ItemDefinitionSO  rockItem,
            GameObject        rockPickupPrefab,
            GameObject        chipsHit,
            GameObject        chipsBreak,
            GameObject        residualPrefab)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_ROCK_NODE) != null)
                AssetDatabase.DeleteAsset(PREFAB_ROCK_NODE);

            var sourceRock = AssetDatabase.LoadAssetAtPath<GameObject>(SOURCE_ROCK_PREFAB);
            if (sourceRock == null)
            {
                Debug.LogError($"[W3_AssetBuilder] Source rock prefab missing at {SOURCE_ROCK_PREFAB}.");
                return null;
            }

            var go = new GameObject("Rock_Mineable");
            try
            {
                go.layer = W2_LayerSetup.LAYER_ENVIRONMENT;

                var visual = (GameObject)PrefabUtility.InstantiatePrefab(sourceRock, go.transform);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                Bounds visualBounds = FitVisualToHeight(visual, TARGET_ROCK_HEIGHT);

                // Box collider sized + centred from the post-scale visual bounds. Box is
                // forgiving for mining sweeps; per-mesh colliders are overkill here and
                // would punish the physics graph for static decoration nodes.
                var col = go.AddComponent<BoxCollider>();
                col.center = visualBounds.center;
                col.size   = visualBounds.size;

                // Kinematic Rigidbody so PlayerCombat's melee fallback resolves
                // col.attachedRigidbody → this root, and TryGetComponent finds RockMineable
                // (which implements IDamageable). Without the Rigidbody, the fallback misses.
                var rb = go.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity  = false;

                var mineable = go.AddComponent<RockMineable>();
                SetSerializedRef(mineable, "requiredToolKind",  requiredTool);
                SetSerializedInt(mineable, "hitsToBreak",       4);
                SetSerializedRef(mineable, "dropItem",          rockItem);
                SetSerializedRef(mineable, "pickupPrefab",      rockPickupPrefab);
                SetSerializedInt(mineable, "minDrop",           2);
                SetSerializedInt(mineable, "maxDrop",           5);
                SetSerializedInt(mineable, "specialDrop",       8);
                SetSerializedFloat(mineable, "specialDropChance", 1f / 20f);
                SetSerializedFloat(mineable, "pickupSpawnHeight", 0.4f);
                SetSerializedRef(mineable, "hitChipPrefab",     chipsHit);
                SetSerializedRef(mineable, "breakDebrisPrefab", chipsBreak);
                SetSerializedFloat(mineable, "breakDebrisHeight", visualBounds.center.y);
                SetSerializedRef(mineable, "residualPrefab",    residualPrefab);

                return PrefabUtility.SaveAsPrefabAsset(go, PREFAB_ROCK_NODE);
            }
            finally { Object.DestroyImmediate(go); }
        }

        // ── Tree / wood pipeline ─────────────────────────────────────────────

        private static GameObject BuildWoodPickupPrefab(ItemDefinitionSO woodItem)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_WOOD_PICKUP) != null)
                AssetDatabase.DeleteAsset(PREFAB_WOOD_PICKUP);

            var sourceTree = AssetDatabase.LoadAssetAtPath<GameObject>(SOURCE_TREE_PREFAB);
            if (sourceTree == null)
            {
                Debug.LogError($"[W3_AssetBuilder] Source tree prefab missing at {SOURCE_TREE_PREFAB}.");
                return null;
            }

            var go = new GameObject("WoodPickup");
            try
            {
                go.layer = W2_LayerSetup.LAYER_ENVIRONMENT;

                var visual = (GameObject)PrefabUtility.InstantiatePrefab(sourceTree, go.transform);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                // Shrunk to log-chunk size so the pickup reads as a single piece of timber
                // rather than a tiny tree on the ground.
                FitVisualToHeight(visual, TARGET_WOOD_PICKUP_HEIGHT);

                var pickup = go.AddComponent<ItemPickup>();
                SetSerializedRef(pickup, "item",   woodItem);
                SetSerializedInt(pickup, "amount", 1);

                return PrefabUtility.SaveAsPrefabAsset(go, PREFAB_WOOD_PICKUP);
            }
            finally { Object.DestroyImmediate(go); }
        }

        private static GameObject BuildTreeNodePrefab(
            ToolKindSO        requiredTool,
            ItemDefinitionSO  woodItem,
            GameObject        woodPickupPrefab,
            GameObject        chipsHit,
            GameObject        chipsBreak,
            GameObject        residualPrefab)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_TREE_NODE) != null)
                AssetDatabase.DeleteAsset(PREFAB_TREE_NODE);

            var sourceTree = AssetDatabase.LoadAssetAtPath<GameObject>(SOURCE_TREE_PREFAB);
            if (sourceTree == null)
            {
                Debug.LogError($"[W3_AssetBuilder] Source tree prefab missing at {SOURCE_TREE_PREFAB}.");
                return null;
            }

            var go = new GameObject("Tree_Choppable");
            try
            {
                go.layer = W2_LayerSetup.LAYER_ENVIRONMENT;

                var visual = (GameObject)PrefabUtility.InstantiatePrefab(sourceTree, go.transform);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                Bounds visualBounds = FitVisualToHeight(visual, TARGET_TREE_HEIGHT);

                // Capsule sized to the trunk only. A box collider that's tall + thin (3.6m
                // x 0.6m) made the CharacterController eject upward when the player walked
                // into the tree's bottom corner — the player visibly "jumped" mid-swing.
                // Capsule's hemispherical caps give the CC a smooth slide instead of a
                // hard corner, and we cap the height at ~trunk-only (~2m) so the leaves
                // zone is non-blocking. The melee OverlapSphere at chest height still
                // intersects the capsule cleanly.
                var col = go.AddComponent<CapsuleCollider>();
                col.center    = new Vector3(0f, 1.0f, 0f);
                col.height    = 2.0f;
                col.radius    = 0.3f;
                col.direction = 1; // Y-axis

                var rb = go.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity  = false;

                // Same component as rocks — the gate is data, not behaviour. Trees take
                // one more swing than rocks (5 vs 4) so the axe feels heavier than the
                // pickaxe; drops 3-6 wood with a 1/20 chance of 9 logs.
                var node = go.AddComponent<RockMineable>();
                SetSerializedRef(node, "requiredToolKind",  requiredTool);
                SetSerializedInt(node, "hitsToBreak",       5);
                SetSerializedRef(node, "dropItem",          woodItem);
                SetSerializedRef(node, "pickupPrefab",      woodPickupPrefab);
                SetSerializedInt(node, "minDrop",           3);
                SetSerializedInt(node, "maxDrop",           6);
                SetSerializedInt(node, "specialDrop",       9);
                SetSerializedFloat(node, "specialDropChance", 1f / 20f);
                SetSerializedFloat(node, "pickupSpawnHeight", 0.5f);
                SetSerializedRef(node, "hitChipPrefab",     chipsHit);
                SetSerializedRef(node, "breakDebrisPrefab", chipsBreak);
                // Spawn the break shatter at the trunk's mid-height so chunks fly outward
                // from where the tree visually snaps, not from the base.
                SetSerializedFloat(node, "breakDebrisHeight", visualBounds.size.y * 0.4f);
                SetSerializedRef(node, "residualPrefab",    residualPrefab);

                return PrefabUtility.SaveAsPrefabAsset(go, PREFAB_TREE_NODE);
            }
            finally { Object.DestroyImmediate(go); }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// Pulls the largest mesh + first material off a source prefab so a chip burst
        /// spawns "pieces of THIS thing" instead of generic cubes. Used for both rock
        /// chunks and wood chunks — the harvest target is just whatever source prefab
        /// you pass in.
        /// </summary>
        private static (Mesh mesh, Material mat) HarvestLargestRendererVisual(string sourcePrefabPath)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePrefabPath);
            if (source == null) return (null, null);

            var meshFilters = source.GetComponentsInChildren<MeshFilter>(true);
            Mesh largest = null;
            int  bestVerts = -1;
            foreach (var mf in meshFilters)
            {
                if (mf.sharedMesh == null) continue;
                if (mf.sharedMesh.vertexCount > bestVerts)
                {
                    largest = mf.sharedMesh;
                    bestVerts = mf.sharedMesh.vertexCount;
                }
            }

            var renderer = source.GetComponentsInChildren<MeshRenderer>(true).FirstOrDefault();
            Material mat = renderer != null ? renderer.sharedMaterial : null;
            return (largest, mat);
        }

        /// <summary>
        /// Uniform-scales the visual so its tallest extent matches <paramref name="targetHeight"/>
        /// (in metres) and returns the resulting world-space bounds (relative to the parent root).
        /// Walks the renderers to handle prefabs whose mesh isn't on the immediate child.
        /// </summary>
        private static Bounds FitVisualToHeight(GameObject visual, float targetHeight)
        {
            Bounds raw = ComputeLocalRendererBounds(visual);
            float h = Mathf.Max(0.0001f, raw.size.y);
            float scale = targetHeight / h;
            visual.transform.localScale = new Vector3(scale, scale, scale);

            // The collider on the parent root needs bounds in *root*-local space (which is the
            // visual's localScale * visualLocalBounds, and the visual sits at localPosition.zero).
            Bounds scaled = new(raw.center * scale, raw.size * scale);
            return scaled;
        }

        private static Bounds ComputeLocalRendererBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            if (renderers.Length == 0)
                return new Bounds(Vector3.zero, Vector3.one);

            // Use the mesh.bounds (local) instead of renderer.bounds (world) so we operate in
            // the visual root's local space and ignore parent transforms applied at instantiation.
            Bounds combined = default;
            bool initialised = false;
            foreach (var r in renderers)
            {
                if (!r.TryGetComponent(out MeshFilter mf) || mf.sharedMesh == null) continue;
                Bounds b = mf.sharedMesh.bounds;
                // Translate by the renderer's local-to-root offset so the root receives the
                // union in its own frame.
                Vector3 offset = root.transform.InverseTransformPoint(r.transform.position);
                b.center += offset;
                if (!initialised) { combined = b; initialised = true; }
                else               { combined.Encapsulate(b); }
            }
            return initialised ? combined : new Bounds(Vector3.zero, Vector3.one);
        }

        private static T LoadOrCreate<T>(string path, System.Action<T> initialiser) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;
            EnsureDir(Path.GetDirectoryName(path));
            var so = ScriptableObject.CreateInstance<T>();
            initialiser?.Invoke(so);
            AssetDatabase.CreateAsset(so, path);
            return so;
        }

        private static void SetSerializedRef(Object target, string fieldName, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(fieldName);
            if (prop == null) { Debug.LogWarning($"Field '{fieldName}' not found on {target}"); return; }
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetSerializedInt(Object target, string fieldName, int value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(fieldName);
            if (prop == null) { Debug.LogWarning($"Field '{fieldName}' not found on {target}"); return; }
            prop.intValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetSerializedFloat(Object target, string fieldName, float value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(fieldName);
            if (prop == null) { Debug.LogWarning($"Field '{fieldName}' not found on {target}"); return; }
            prop.floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsureDir(string path)
        {
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        }

        /// <summary>
        /// Builds (or refreshes) a URP/Lit material at <paramref name="path"/> set to a flat
        /// base colour. Used by props that need a deliberate solid-colour look (e.g. the
        /// tree stump's "brown soil-stub" silhouette) rather than borrowing a textured
        /// material from a Synty prefab.
        /// </summary>
        private static Material EnsureSimpleColorMaterial(string path, Color color)
        {
            EnsureDir(Path.GetDirectoryName(path));
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                var urp = Shader.Find("Universal Render Pipeline/Lit");
                if (urp != null && mat.shader != urp) mat.shader = urp;
            }
            // URP/Lit uses _BaseColor; Standard fallback uses _Color. Set both so we don't
            // care which shader resolved.
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color"))     mat.SetColor("_Color",     color);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ── Inventory slot-view prefab ───────────────────────────────────────

        /// <summary>
        /// Builds the cell prefab the InventoryHUD instantiates 40× into its grid.
        /// One root with: a slot background image + a child Icon (Image) + a child Count
        /// (TextMeshProUGUI bottom-right) + an InventorySlotView component referencing the
        /// two children. UI Layout-friendly — sized via the grid's CellSize, not its own RT.
        /// </summary>
        private static GameObject BuildInventorySlotViewPrefab()
        {
            EnsureDir(Path.GetDirectoryName(PREFAB_INV_SLOT));
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_INV_SLOT) != null)
                AssetDatabase.DeleteAsset(PREFAB_INV_SLOT);

            var go = new GameObject("InventorySlotView", typeof(RectTransform));
            try
            {
                go.layer = LayerMask.NameToLayer("UI");

                // Fixed cell size for HorizontalLayoutGroup users (the crafting bar). The
                // inventory's GridLayoutGroup overrides the size via its cellSize, so this
                // doesn't conflict there.
                var layout = go.AddComponent<UnityEngine.UI.LayoutElement>();
                layout.preferredWidth  = 80f;
                layout.preferredHeight = 80f;

                // Cell background — slightly lighter than the panel so empty slots are visible.
                // raycastTarget = true so the Button below picks up clicks anywhere on the cell.
                var bg = go.AddComponent<UnityEngine.UI.Image>();
                bg.color = new Color(1f, 1f, 1f, 0.08f);
                bg.raycastTarget = true;

                // Button — uses the bg Image as targetGraphic. Color tint on hover/press
                // gives the player feedback that slots are clickable.
                var button = go.AddComponent<UnityEngine.UI.Button>();
                button.targetGraphic = bg;
                var colors = button.colors;
                colors.normalColor      = new Color(1f, 1f, 1f, 1f);
                colors.highlightedColor = new Color(1f, 1f, 1f, 1.4f);
                colors.pressedColor     = new Color(0.7f, 0.7f, 0.7f, 1f);
                colors.selectedColor    = new Color(1f, 1f, 1f, 1f);
                colors.disabledColor    = new Color(0.4f, 0.4f, 0.4f, 0.5f);
                button.colors = colors;

                // Selection highlight — gold border-ish overlay, disabled by default. The
                // InventorySlotView toggles its enabled state based on whether the owner
                // (HUD/CraftingPanel) marks this slot selected.
                var selGo = new GameObject("Selection", typeof(RectTransform));
                selGo.layer = LayerMask.NameToLayer("UI");
                selGo.transform.SetParent(go.transform, false);
                var selRt = (RectTransform)selGo.transform;
                selRt.anchorMin = Vector2.zero;
                selRt.anchorMax = Vector2.one;
                selRt.offsetMin = Vector2.zero;
                selRt.offsetMax = Vector2.zero;
                var selImg = selGo.AddComponent<UnityEngine.UI.Image>();
                selImg.color = new Color(1f, 0.84f, 0.2f, 0.35f);   // gold tint
                selImg.raycastTarget = false;
                selImg.enabled = false;

                // Icon child.
                var iconGo = new GameObject("Icon", typeof(RectTransform));
                iconGo.layer = LayerMask.NameToLayer("UI");
                iconGo.transform.SetParent(go.transform, false);
                var iconRt = (RectTransform)iconGo.transform;
                iconRt.anchorMin = Vector2.zero;
                iconRt.anchorMax = Vector2.one;
                iconRt.offsetMin = new Vector2(6f, 6f);
                iconRt.offsetMax = new Vector2(-6f, -6f);
                var icon = iconGo.AddComponent<UnityEngine.UI.Image>();
                icon.preserveAspect = true;
                icon.raycastTarget  = false;
                icon.enabled        = false;

                // Count text child — bottom-right corner of the cell.
                var countGo = new GameObject("Count", typeof(RectTransform));
                countGo.layer = LayerMask.NameToLayer("UI");
                countGo.transform.SetParent(go.transform, false);
                var countRt = (RectTransform)countGo.transform;
                countRt.anchorMin = new Vector2(0f, 0f);
                countRt.anchorMax = new Vector2(1f, 0f);
                countRt.pivot     = new Vector2(0.5f, 0f);
                countRt.offsetMin = new Vector2(2f, 2f);
                countRt.offsetMax = new Vector2(-2f, 22f);
                var count = countGo.AddComponent<TMPro.TextMeshProUGUI>();
                count.alignment        = TMPro.TextAlignmentOptions.BottomRight;
                count.fontSize         = 16f;
                count.color            = Color.white;
                count.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
                count.raycastTarget    = false;

                // Hotbar slot-number label — top-left corner. InventorySlotView toggles this
                // via SetSlotNumber, off by default for non-hotbar slots.
                var numGo = new GameObject("Number", typeof(RectTransform));
                numGo.layer = LayerMask.NameToLayer("UI");
                numGo.transform.SetParent(go.transform, false);
                var numRt = (RectTransform)numGo.transform;
                numRt.anchorMin = new Vector2(0f, 1f);
                numRt.anchorMax = new Vector2(0f, 1f);
                numRt.pivot     = new Vector2(0f, 1f);
                numRt.anchoredPosition = new Vector2(4f, -2f);
                numRt.sizeDelta = new Vector2(28f, 22f);
                var numText = numGo.AddComponent<TMPro.TextMeshProUGUI>();
                numText.text             = string.Empty;
                numText.alignment        = TMPro.TextAlignmentOptions.TopLeft;
                numText.fontSize         = 18f;
                numText.color            = new Color(1f, 0.9f, 0.4f, 1f);
                numText.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
                numText.raycastTarget    = false;
                numGo.SetActive(false);

                // Bind the view component.
                var view = go.AddComponent<InventorySlotView>();
                SetSerializedRef(view, "iconImage",          icon);
                SetSerializedRef(view, "countText",          count);
                SetSerializedRef(view, "button",             button);
                SetSerializedRef(view, "selectionHighlight", selImg);
                SetSerializedRef(view, "numberText",         numText);

                return PrefabUtility.SaveAsPrefabAsset(go, PREFAB_INV_SLOT);
            }
            finally { Object.DestroyImmediate(go); }
        }

        // ── Sprite icon helpers ──────────────────────────────────────────────

        private static string IconPath(string dir, string fileName) => $"{dir}/{fileName}";

        /// <summary>
        /// Forces a PNG to import as Sprite (single, 2D-and-UI) with point filtering so
        /// pixel-art icons stay crisp in the inventory grid. No-op if already configured.
        /// </summary>
        private static void ConfigureSpriteImporter(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;

            bool changed = false;
            if (importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                changed = true;
            }
            if (importer.spriteImportMode != SpriteImportMode.Single)
            {
                importer.spriteImportMode = SpriteImportMode.Single;
                changed = true;
            }
            if (importer.filterMode != FilterMode.Point)
            {
                importer.filterMode = FilterMode.Point;
                changed = true;
            }
            if (importer.textureCompression != TextureImporterCompression.Uncompressed)
            {
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                changed = true;
            }
            if (changed) importer.SaveAndReimport();
        }

        private static Sprite LoadSprite(string path)
        {
            // Sprite asset hangs off the texture import — LoadAssetAtPath<Sprite> returns
            // the Single sprite once the importer is in Sprite mode.
            var sp = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sp == null) Debug.LogWarning($"[W3_AssetBuilder] Sprite not loadable at {path} — icon will be blank.");
            return sp;
        }

        /// <summary>Get a built-in primitive mesh (Cube, Cylinder, etc.) without leaving a
        /// stray GameObject in the scene.</summary>
        private static Mesh GetPrimitiveMesh(PrimitiveType type)
        {
            var temp = GameObject.CreatePrimitive(type);
            var mesh = temp.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(temp);
            return mesh;
        }

        // ── Residual props ───────────────────────────────────────────────────

        /// <summary>
        /// Short cylinder + tree material = a stump-shaped marker left where a tree
        /// stood. No collider — purely visual.
        /// </summary>
        private static GameObject BuildTreeStumpPrefab(Material woodMaterial)
        {
            EnsureDir(Path.GetDirectoryName(PREFAB_TREE_STUMP));
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_TREE_STUMP) != null)
                AssetDatabase.DeleteAsset(PREFAB_TREE_STUMP);

            var go = new GameObject("TreeStump");
            try
            {
                go.layer = W2_LayerSetup.LAYER_ENVIRONMENT;
                go.transform.localScale = Vector3.one;

                // Cylinder primitive: default Unity cylinder is 2m tall × 1m diameter. Scale
                // it down + flatten for a stump silhouette.
                var visualGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.DestroyImmediate(visualGo.GetComponent<Collider>());
                visualGo.transform.SetParent(go.transform, false);
                visualGo.transform.localPosition = new Vector3(0f, 0.15f, 0f);
                visualGo.transform.localScale    = new Vector3(0.5f, 0.15f, 0.5f);
                if (woodMaterial != null)
                    visualGo.GetComponent<MeshRenderer>().sharedMaterial = woodMaterial;

                return PrefabUtility.SaveAsPrefabAsset(go, PREFAB_TREE_STUMP);
            }
            finally { Object.DestroyImmediate(go); }
        }

        /// <summary>
        /// Smaller version of the source rock visual + same material. Leaves a "rubble
        /// pile" silhouette after the rock breaks. No collider.
        /// </summary>
        private static GameObject BuildRockBasePrefab(Material rockMaterial)
        {
            EnsureDir(Path.GetDirectoryName(PREFAB_ROCK_BASE));
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_ROCK_BASE) != null)
                AssetDatabase.DeleteAsset(PREFAB_ROCK_BASE);

            var sourceRock = AssetDatabase.LoadAssetAtPath<GameObject>(SOURCE_ROCK_PREFAB);
            if (sourceRock == null) return null;

            var go = new GameObject("RockBase");
            try
            {
                go.layer = W2_LayerSetup.LAYER_ENVIRONMENT;

                var visual = (GameObject)PrefabUtility.InstantiatePrefab(sourceRock, go.transform);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                // ~0.3m tall — looks like a chunk left over after the boulder cracked apart.
                FitVisualToHeight(visual, 0.3f);

                // Strip any collider that came on the source — residual is visual-only so
                // it doesn't compete with the player CC or block follow-up swings.
                foreach (var col in go.GetComponentsInChildren<Collider>(true))
                    Object.DestroyImmediate(col);

                if (rockMaterial != null)
                    foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
                        r.sharedMaterial = rockMaterial;

                return PrefabUtility.SaveAsPrefabAsset(go, PREFAB_ROCK_BASE);
            }
            finally { Object.DestroyImmediate(go); }
        }

        // ── Crafting table prefab + SOs ──────────────────────────────────────

        private static GameObject BuildPlacedCraftingTablePrefab()
        {
            EnsureDir(Path.GetDirectoryName(PREFAB_TABLE_PLACED));
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_TABLE_PLACED) != null)
                AssetDatabase.DeleteAsset(PREFAB_TABLE_PLACED);

            var sourceTable = AssetDatabase.LoadAssetAtPath<GameObject>(SOURCE_TABLE_PREFAB);
            if (sourceTable == null)
            {
                Debug.LogError($"[W3_AssetBuilder] Source table prefab missing at {SOURCE_TABLE_PREFAB}.");
                return null;
            }

            var urpMat = EnsureTableUrpMaterial();

            var go = new GameObject("CraftingTable_Placed");
            try
            {
                go.layer = W2_LayerSetup.LAYER_ENVIRONMENT;

                // Visual = source table model under the wrapper. Held + placed share the
                // same visual; the wrapper carries the gameplay components.
                var visual = (GameObject)PrefabUtility.InstantiatePrefab(sourceTable, go.transform);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;

                // Override the source's Standard-shader material with our URP/Lit one.
                // Without this the table renders bright pink in the URP pipeline.
                ApplyMaterialOverride(visual, urpMat);

                // Box collider sized from the visual's renderer bounds so the player can
                // walk up to it but not pass through. PlacedCraftingTable handles the
                // interact-on-press logic.
                Bounds bounds = ComputeLocalRendererBounds(visual);
                if (bounds.size.sqrMagnitude < 0.0001f) bounds = new Bounds(Vector3.up * 0.5f, Vector3.one);
                var col = go.AddComponent<BoxCollider>();
                col.center = bounds.center;
                col.size   = bounds.size;

                go.AddComponent<PlacedCraftingTable>();

                return PrefabUtility.SaveAsPrefabAsset(go, PREFAB_TABLE_PLACED);
            }
            finally { Object.DestroyImmediate(go); }
        }

        /// <summary>
        /// Builds (or refreshes) <c>M_Table.mat</c> as URP/Lit using the textures bundled
        /// with the ThirdParty Table pack. ThirdParty's STOL.mat ships with the Built-in
        /// Standard shader, which the project's URP pipeline can't render — magenta in
        /// Play mode. CLAUDE.md §2 forbids editing ThirdParty, so we keep the override
        /// material in _Project/ and apply it via PrefabUtility on the wrapper prefab.
        /// </summary>
        private static Material EnsureTableUrpMaterial()
        {
            EnsureDir(Path.GetDirectoryName(TABLE_MAT_PATH));
            ConfigureNormalMap(TABLE_TEX_NORMAL);

            var albedo   = AssetDatabase.LoadAssetAtPath<Texture2D>(TABLE_TEX_ALBEDO);
            var normal   = AssetDatabase.LoadAssetAtPath<Texture2D>(TABLE_TEX_NORMAL);
            var emission = AssetDatabase.LoadAssetAtPath<Texture2D>(TABLE_TEX_EMISSION);

            var mat = AssetDatabase.LoadAssetAtPath<Material>(TABLE_MAT_PATH);
            if (mat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, TABLE_MAT_PATH);
            }
            else
            {
                var urp = Shader.Find("Universal Render Pipeline/Lit");
                if (urp != null && mat.shader != urp) mat.shader = urp;
            }

            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            if (albedo != null && mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", albedo);
            if (albedo != null && mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", albedo);
            if (normal != null && mat.HasProperty("_BumpMap"))
            {
                mat.SetTexture("_BumpMap", normal);
                mat.EnableKeyword("_NORMALMAP");
            }
            if (emission != null && mat.HasProperty("_EmissionMap"))
            {
                mat.SetTexture("_EmissionMap", emission);
                mat.SetColor("_EmissionColor", Color.white);
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }

            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Sets <paramref name="mat"/> on every MeshRenderer beneath the given root.</summary>
        private static void ApplyMaterialOverride(GameObject root, Material mat)
        {
            if (mat == null) return;
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                int n = r.sharedMaterials.Length > 0 ? r.sharedMaterials.Length : 1;
                var arr = new Material[n];
                for (int i = 0; i < n; i++) arr[i] = mat;
                r.sharedMaterials = arr;
            }
        }

        /// <summary>Forces a PNG to import as a NormalMap so URP/Lit reads it correctly.</summary>
        private static void ConfigureNormalMap(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;
            if (importer.textureType != TextureImporterType.NormalMap)
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.SaveAndReimport();
            }
        }

        private static AbilityDefinitionSO EnsurePlaceAbility(GameObject placedPrefab,
                                                              ItemDefinitionSO consumeItem,
                                                              DamageTypeSO     dtPhysical)
        {
            EnsureDir(Path.GetDirectoryName(ABILITY_PLACE_TABLE));
            var ab = LoadOrCreate<AbilityDefinitionSO>(ABILITY_PLACE_TABLE, a =>
            {
                a.displayName       = "Place Crafting Table";
                a.description       = "Put down a crafting table at the cursor.";
                a.damage            = 0f;
                a.range             = 6f;
                a.cooldown          = 0.5f;
                a.animatorStateName = string.Empty; // no swing anim — purely a place action
                a.animatorLayer     = 0;
            });
            ab.placedPrefab    = placedPrefab;
            ab.consumeOnPlace  = consumeItem;
            ab.projectilePrefab = null;
            ab.damageType       = dtPhysical;
            // Match the Slash semantics for cast-time movement so the player isn't yanked
            // around when planting a table.
            ab.faceAimOnCast      = false;
            ab.aimYawOffset       = 0f;
            ab.movementSlowFactor = 0f;
            EditorUtility.SetDirty(ab);
            return ab;
        }

        private static WeaponDefinitionSO EnsureCraftingTableWeapon(AnimationClip attackClip,
                                                                    AbilityDefinitionSO placeAbility)
        {
            var w = AssetDatabase.LoadAssetAtPath<WeaponDefinitionSO>(WEAPON_CRAFTING_TABLE);
            bool firstRun = w == null;
            if (firstRun)
            {
                w = ScriptableObject.CreateInstance<WeaponDefinitionSO>();
                AssetDatabase.CreateAsset(w, WEAPON_CRAFTING_TABLE);
            }

            // Held visual is a wrapper prefab in _Project/ that re-binds the table mesh to
            // our URP/Lit material. Cannot patch the ThirdParty source (CLAUDE.md §2),
            // and WeaponEquipment doesn't apply runtime material overrides — so we build
            // a held variant once at editor time.
            var held = BuildCraftingTableHeldPrefab();
            w.displayName = "Crafting Table (Held)";
            w.mainPrefab  = held;
            w.mainHand    = HumanBodyBones.RightHand;

            if (firstRun)
            {
                w.mainLocalPos   = new Vector3(0f, 0.05f, 0.02f);
                w.mainLocalEuler = new Vector3(0f, 0f, 0f);
                w.mainLocalScale = new Vector3(0.25f, 0.25f, 0.25f);
            }

            w.attackClip = attackClip;
            w.ability    = placeAbility;
            w.toolKind   = null;

            EditorUtility.SetDirty(w);
            return w;
        }

        private static GameObject BuildCraftingTableHeldPrefab()
        {
            EnsureDir(Path.GetDirectoryName(PREFAB_TABLE_HELD));
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_TABLE_HELD) != null)
                AssetDatabase.DeleteAsset(PREFAB_TABLE_HELD);

            var sourceTable = AssetDatabase.LoadAssetAtPath<GameObject>(SOURCE_TABLE_PREFAB);
            if (sourceTable == null) return null;

            var urpMat = EnsureTableUrpMaterial();
            var go = new GameObject("CraftingTable_Held");
            try
            {
                var visual = (GameObject)PrefabUtility.InstantiatePrefab(sourceTable, go.transform);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                ApplyMaterialOverride(visual, urpMat);
                return PrefabUtility.SaveAsPrefabAsset(go, PREFAB_TABLE_HELD);
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
