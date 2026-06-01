using Celestia.Data;
using Celestia.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Celestia.EditorTools
{
    /// <summary>
    /// W3 scene patches:
    ///  * Bootstrap.unity → adds <see cref="RewardPopupSpawner"/> and the Inventory HUD
    ///    canvas (hidden until the player presses I) under GameManager. Both are scene-
    ///    persistent — they must outlive additive scene swaps just like DamageNumberSpawner.
    ///  * Test_PlayerMovement.unity → drops a small line of Rock_Mineable nodes near the
    ///    player spawn so the W3 mining loop is testable from the existing dev scene.
    ///
    /// Idempotent — strips its own children before adding so re-runs don't duplicate.
    /// </summary>
    public static class W3_SceneBuilder
    {
        private const string ROCKS_PARENT_NAME    = "MineableRocks";
        private const string TREES_PARENT_NAME    = "ChoppableTrees";
        private const string POPUP_SPAWNER_NAME   = "RewardPopupSpawner";
        private const string INVENTORY_HUD_NAME   = "InventoryHUDCanvas";
        private const string TABLE_HUD_NAME       = "CraftingTableUICanvas";
        private const string EVENT_SYSTEM_NAME    = "EventSystem";

        // World sizing for the dev test scene. Plane primitive is 10m at scale 1, so
        // FLOOR_SCALE * 10 == metres per axis. 80×80m gives the player room to roam
        // without drowning the spawn point in foliage.
        private const float FLOOR_SCALE         = 8f;       // 80m × 80m
        private const float WORLD_HALF_EXTENT   = FLOOR_SCALE * 5f - 4f; // 36m, with 4m edge buffer
        private const float SAFE_RADIUS         = 6f;       // no nodes within 6m of origin (spawn)
        private const int   ROCK_COUNT          = 14;
        private const int   TREE_COUNT          = 16;
        private const int   SCATTER_SEED        = 20260507; // stable scatter so re-runs match

        // The W2 dummies sit on the +Z line (see W2_SceneBuilder), so we cluster rocks
        // in the -X half and trees in the -Z half — clear quadrants per tool.
        private static readonly Bounds2D ROCK_AREA = new(minX: -WORLD_HALF_EXTENT, maxX: -2f,
                                                         minZ: -WORLD_HALF_EXTENT, maxZ:  WORLD_HALF_EXTENT);
        private static readonly Bounds2D TREE_AREA = new(minX: -WORLD_HALF_EXTENT, maxX:  WORLD_HALF_EXTENT,
                                                         minZ: -WORLD_HALF_EXTENT, maxZ: -2f);

        private readonly struct Bounds2D
        {
            public readonly float MinX, MaxX, MinZ, MaxZ;
            public Bounds2D(float minX, float maxX, float minZ, float maxZ)
            { MinX = minX; MaxX = maxX; MinZ = minZ; MaxZ = maxZ; }
        }

        [MenuItem("Celestia/W3/4 - Patch Scenes (rocks + spawner + HUD)")]
        public static void Patch()
        {
            Debug.Log("[W3_SceneBuilder] BEGIN — patching Test_PlayerMovement.unity + Bootstrap.unity.");
            PatchTestScene();
            PatchBootstrap();
            Debug.Log("[W3_SceneBuilder] END — open Bootstrap.unity → Play. " +
                      "Pickaxe (7) → rocks (negative-X side); Axe (8) → trees (negative-Z side). " +
                      "Press I to view inventory.");
        }

        // ── Test scene: rocks ────────────────────────────────────────────────

        private static void PatchTestScene()
        {
            var rockPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(W3_AssetBuilder.PREFAB_ROCK_NODE);
            var treePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(W3_AssetBuilder.PREFAB_TREE_NODE);
            if (rockPrefab == null)
            {
                Debug.LogError("[W3_SceneBuilder] Rock_Mineable prefab missing. Run Celestia/W3/1 first.");
                return;
            }
            if (treePrefab == null)
                Debug.LogWarning("[W3_SceneBuilder] Tree_Choppable prefab missing — skipping trees. Re-run Celestia/W3/1 once it's built.");

            var scene = EditorSceneManager.OpenScene(W1_SceneBuilder.TEST_PATH, OpenSceneMode.Single);

            // Resize the floor to 80×80m so there's room for clusters of rocks and trees
            // without crowding the player spawn or the W2 dummies on +Z.
            ResizeFloor(scene, FLOOR_SCALE);

            // Wipe both groups so re-runs don't duplicate. Cache the name to a local before
            // calling DestroyImmediate — accessing root.name after destroy throws a
            // "MissingReferenceException", which would abort the rest of PatchTestScene
            // (the bug that left scenes with rocks-only / no trees after re-runs).
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root == null) continue;
                var rootName = root.name;
                if (rootName == ROCKS_PARENT_NAME || rootName == TREES_PARENT_NAME)
                    Object.DestroyImmediate(root);
            }

            // Seeded scatter so the world layout is deterministic across re-runs — stable
            // for screenshots / regression checks without needing to commit a hand-placed
            // scene. Restore the global RNG state after to avoid bleed-through.
            var savedRandomState = Random.state;
            Random.InitState(SCATTER_SEED);

            var placed = new System.Collections.Generic.List<Vector2>();

            var rocksParent = new GameObject(ROCKS_PARENT_NAME);
            int rocksPlaced = ScatterNodes(rockPrefab, rocksParent.transform, ROCK_AREA, ROCK_COUNT,
                                            minSpacing: 3.5f, placed);

            int treesPlaced = 0;
            if (treePrefab != null)
            {
                var treesParent = new GameObject(TREES_PARENT_NAME);
                treesPlaced = ScatterNodes(treePrefab, treesParent.transform, TREE_AREA, TREE_COUNT,
                                           minSpacing: 4.5f, placed);
            }

            Random.state = savedRandomState;

            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[W3_SceneBuilder] {rocksPlaced} rocks + {treesPlaced} trees scattered on an 80×80m floor in {W1_SceneBuilder.TEST_PATH}.");
        }

        // ── World scatter helpers ────────────────────────────────────────────

        private static void ResizeFloor(UnityEngine.SceneManagement.Scene scene, float scale)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name != "Floor") continue;
                root.transform.localScale = new Vector3(scale, 1f, scale);
                return;
            }
            Debug.LogWarning("[W3_SceneBuilder] No 'Floor' root in test scene — skipping resize. Run Celestia/W1/4 first.");
        }

        private static int ScatterNodes(GameObject prefab, Transform parent, Bounds2D area, int count,
                                        float minSpacing, System.Collections.Generic.List<Vector2> placed)
        {
            if (prefab == null) return 0;
            int spawned = 0;
            int safety  = count * 30; // bail-out so a too-tight area can't loop forever
            while (spawned < count && safety-- > 0)
            {
                float x = Random.Range(area.MinX, area.MaxX);
                float z = Random.Range(area.MinZ, area.MaxZ);

                // Keep clear of the spawn so the player isn't hugged by foliage on Play.
                if (x * x + z * z < SAFE_RADIUS * SAFE_RADIUS) continue;

                bool tooClose = false;
                for (int i = 0; i < placed.Count; i++)
                {
                    float dx = placed[i].x - x;
                    float dz = placed[i].y - z;
                    if (dx * dx + dz * dz < minSpacing * minSpacing) { tooClose = true; break; }
                }
                if (tooClose) continue;

                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                inst.transform.position = new Vector3(x, 0f, z);
                inst.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                placed.Add(new Vector2(x, z));
                spawned++;
            }
            return spawned;
        }

        // ── Bootstrap: spawner + HUD canvas ──────────────────────────────────

        private static void PatchBootstrap()
        {
            Debug.Log("[W3_SceneBuilder] PatchBootstrap — loading required assets…");
            var popupPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(W3_AssetBuilder.PREFAB_REWARD_POPUP);
            var inventorySO = AssetDatabase.LoadAssetAtPath<InventorySO>(W3_AssetBuilder.INVENTORY_PLAYER);
            if (popupPrefab == null)
            {
                Debug.LogError($"[W3_SceneBuilder] Reward popup prefab missing at {W3_AssetBuilder.PREFAB_REWARD_POPUP}. Aborting Bootstrap patch.");
                return;
            }
            if (inventorySO == null)
            {
                Debug.LogError($"[W3_SceneBuilder] Inventory SO missing at {W3_AssetBuilder.INVENTORY_PLAYER}. Aborting Bootstrap patch.");
                return;
            }

            Debug.Log($"[W3_SceneBuilder] PatchBootstrap — opening {W1_SceneBuilder.BOOTSTRAP_PATH}");
            var scene = EditorSceneManager.OpenScene(W1_SceneBuilder.BOOTSTRAP_PATH, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                Debug.LogError("[W3_SceneBuilder] OpenScene returned an invalid Scene — Bootstrap.unity wasn't loaded. " +
                               "If Unity prompted to save unsaved Bootstrap changes and you cancelled, the open is aborted. " +
                               "Save or discard manually, then re-run Celestia/W3/4.");
                return;
            }

            GameObject gm = null;
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == "GameManager") { gm = root; break; }
            if (gm == null)
            {
                Debug.LogError("[W3_SceneBuilder] GameManager root not found in Bootstrap.unity. Run Celestia/W1/4 first.");
                return;
            }

            // Reward popup spawner.
            var existingSpawner = gm.transform.Find(POPUP_SPAWNER_NAME);
            if (existingSpawner != null) Object.DestroyImmediate(existingSpawner.gameObject);

            var spawnerGo = new GameObject(POPUP_SPAWNER_NAME);
            spawnerGo.transform.SetParent(gm.transform, false);
            var spawner = spawnerGo.AddComponent<RewardPopupSpawner>();
            SetSerialized(spawner, "prefab", popupPrefab.GetComponent<RewardPopup>());

            // EventSystem — Bootstrap doesn't ship with one, but the inventory HUD's
            // Buttons need it to receive clicks. Use the new-Input-System UI module
            // (CLAUDE.md §1 stack lock).
            EnsureEventSystem(gm.transform);

            // Inventory HUD canvas (toggle with I).
            var existingHud = gm.transform.Find(INVENTORY_HUD_NAME);
            if (existingHud != null) Object.DestroyImmediate(existingHud.gameObject);
            BuildInventoryHud(gm.transform, inventorySO);

            // Crafting-table 3×3 canvas (toggle by interacting with a placed table).
            var existingTableHud = gm.transform.Find(TABLE_HUD_NAME);
            if (existingTableHud != null) Object.DestroyImmediate(existingTableHud.gameObject);
            BuildCraftingTableCanvas(gm.transform, inventorySO);

            // Mark scene dirty so SaveScene actually writes — we only modified components
            // via SerializedObject which doesn't always set the scene's dirty flag.
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);

            bool saved = EditorSceneManager.SaveScene(scene);
            if (!saved)
            {
                Debug.LogError($"[W3_SceneBuilder] SaveScene FAILED for {W1_SceneBuilder.BOOTSTRAP_PATH}. " +
                               "If Bootstrap.unity is read-only or open in another editor instance, that's why.");
                return;
            }
            Debug.Log($"[W3_SceneBuilder] RewardPopupSpawner + InventoryHUD saved into {W1_SceneBuilder.BOOTSTRAP_PATH}.");
        }

        private static void BuildInventoryHud(Transform parent, InventorySO inventory)
        {
            // Slot-view prefab: instantiated 40× into the grid at runtime.
            var slotPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(W3_AssetBuilder.PREFAB_INV_SLOT);
            if (slotPrefab == null)
            {
                Debug.LogError($"[W3_SceneBuilder] InventorySlotView prefab missing at {W3_AssetBuilder.PREFAB_INV_SLOT}. " +
                               "Run Celestia/W3/1 first.");
                return;
            }

            var craftingTableRecipe = AssetDatabase.LoadAssetAtPath<RecipeSO>(W3_AssetBuilder.RECIPE_CRAFTING_TABLE);
            if (craftingTableRecipe == null)
                Debug.LogWarning($"[W3_SceneBuilder] Recipe missing at {W3_AssetBuilder.RECIPE_CRAFTING_TABLE} — Craft button will no-op.");

            // Canvas + scaler.
            var canvasGo = new GameObject(INVENTORY_HUD_NAME);
            canvasGo.transform.SetParent(parent, false);
            canvasGo.layer = LayerMask.NameToLayer("UI");

            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode     = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight  = 0.5f;

            canvasGo.AddComponent<GraphicRaycaster>();

            // Layout constants. Sections (top → bottom): Title / Crafting bar / Inventory
            // grid (4 rows × 10 cols = 40 slots) / Hotbar row (10 slots, numbered 1-9+0).
            const int   COLS          = 10;
            const int   INV_ROWS      = 4;
            const float CELL          = 80f;
            const float SPACING       = 6f;
            const float TITLE_HEIGHT  = 44f;
            const float CRAFT_HEIGHT  = CELL + 24f; // crafting row height
            const float HOTBAR_HEIGHT = CELL + 16f;
            const float SECTION_GAP   = 16f;
            const float PAD           = 24f;
            const float SIDE_PAD      = 60f;       // breathing room on each side so the
                                                   // crafting bar doesn't kiss the panel edge

            float gridW   = COLS * CELL + (COLS - 1) * SPACING;
            float gridH   = INV_ROWS * CELL + (INV_ROWS - 1) * SPACING;
            float panelW  = gridW + (PAD + SIDE_PAD) * 2;
            float panelH  = TITLE_HEIGHT
                           + CRAFT_HEIGHT  + SECTION_GAP
                           + gridH         + SECTION_GAP
                           + HOTBAR_HEIGHT
                           + PAD * 2;

            // Anchor to bottom-center of the screen with a comfortable margin so the
            // crafting-table panel (top-anchored, see BuildCraftingTableCanvas) doesn't
            // overlap when both UIs are visible.
            var panelGo = new GameObject("Panel");
            panelGo.transform.SetParent(canvasGo.transform, false);
            var panelRt = panelGo.AddComponent<RectTransform>();
            panelRt.anchorMin = new Vector2(0.5f, 0f);
            panelRt.anchorMax = new Vector2(0.5f, 0f);
            panelRt.pivot     = new Vector2(0.5f, 0f);
            panelRt.anchoredPosition = new Vector2(0f, 40f); // 40 px above bottom edge
            panelRt.sizeDelta = new Vector2(panelW, panelH);
            var panelImg = panelGo.AddComponent<Image>();
            panelImg.color = new Color(0f, 0f, 0f, 0.78f);

            // Title bar at the top.
            var titleGo = MakeUIChild(panelGo.transform, "Title");
            var titleRt = (RectTransform)titleGo.transform;
            titleRt.anchorMin = new Vector2(0f, 1f);
            titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.pivot     = new Vector2(0.5f, 1f);
            titleRt.anchoredPosition = new Vector2(0f, -PAD * 0.25f);
            titleRt.sizeDelta = new Vector2(0f, TITLE_HEIGHT);
            var titleText = titleGo.AddComponent<TextMeshProUGUI>();
            titleText.text      = "Inventory";
            titleText.alignment = TextAlignmentOptions.Center;
            titleText.fontSize  = 28f;
            titleText.color     = Color.white;

            // Crafting row — sits just below the title.
            var craftRowGo = MakeUIChild(panelGo.transform, "CraftingRow");
            var craftRowRt = (RectTransform)craftRowGo.transform;
            craftRowRt.anchorMin = new Vector2(0.5f, 1f);
            craftRowRt.anchorMax = new Vector2(0.5f, 1f);
            craftRowRt.pivot     = new Vector2(0.5f, 1f);
            craftRowRt.anchoredPosition = new Vector2(0f, -(PAD + TITLE_HEIGHT));
            craftRowRt.sizeDelta = new Vector2(panelW - PAD * 2, CRAFT_HEIGHT);
            var craftLayout = craftRowGo.AddComponent<HorizontalLayoutGroup>();
            craftLayout.childAlignment      = TextAnchor.MiddleCenter;
            craftLayout.spacing             = SPACING;
            craftLayout.childControlWidth   = false;
            craftLayout.childControlHeight  = false;
            craftLayout.childForceExpandWidth  = false;
            craftLayout.childForceExpandHeight = false;

            var inputViews = new InventorySlotView[CraftingPanel.DEFAULT_INPUT_COUNT];
            for (int i = 0; i < CraftingPanel.DEFAULT_INPUT_COUNT; i++)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(slotPrefab, craftRowGo.transform);
                go.name = $"CraftInput_{i}";
                inputViews[i] = go.GetComponent<InventorySlotView>();
            }

            // Arrow label between inputs and output.
            var arrowGo = MakeUIChild(craftRowGo.transform, "Arrow");
            var arrowLayout = arrowGo.AddComponent<LayoutElement>();
            arrowLayout.preferredWidth  = 60f;
            arrowLayout.preferredHeight = CELL;
            var arrowText = arrowGo.AddComponent<TextMeshProUGUI>();
            arrowText.text      = "→";
            arrowText.alignment = TextAlignmentOptions.Center;
            arrowText.fontSize  = 48f;
            arrowText.color     = Color.white;

            // Output slot.
            var outputGo  = (GameObject)PrefabUtility.InstantiatePrefab(slotPrefab, craftRowGo.transform);
            outputGo.name = "CraftOutput";
            var outputView = outputGo.GetComponent<InventorySlotView>();

            // Craft button.
            var craftBtnGo = MakeUIChild(craftRowGo.transform, "CraftButton");
            var craftBtnLayout = craftBtnGo.AddComponent<LayoutElement>();
            craftBtnLayout.preferredWidth  = 140f;
            craftBtnLayout.preferredHeight = CELL;
            var craftBtnImg = craftBtnGo.AddComponent<Image>();
            craftBtnImg.color = new Color(0.15f, 0.45f, 0.2f, 0.9f);
            var craftBtn = craftBtnGo.AddComponent<Button>();
            craftBtn.targetGraphic = craftBtnImg;
            var craftLabelGo = MakeUIChild(craftBtnGo.transform, "Label");
            var craftLabelRt = (RectTransform)craftLabelGo.transform;
            craftLabelRt.anchorMin = Vector2.zero;
            craftLabelRt.anchorMax = Vector2.one;
            craftLabelRt.offsetMin = Vector2.zero;
            craftLabelRt.offsetMax = Vector2.zero;
            var craftLabelText = craftLabelGo.AddComponent<TextMeshProUGUI>();
            craftLabelText.text      = "Craft";
            craftLabelText.alignment = TextAlignmentOptions.Center;
            craftLabelText.fontSize  = 28f;
            craftLabelText.color     = Color.white;
            craftLabelText.raycastTarget = false;

            // Hotbar row at the bottom of the panel (visual hint that these are the
            // numbered/usable slots) — built first so we can position the main grid
            // relative to its top edge.
            var hotbarGo = MakeUIChild(panelGo.transform, "Hotbar");
            var hotbarRt = (RectTransform)hotbarGo.transform;
            hotbarRt.anchorMin = new Vector2(0.5f, 0f);
            hotbarRt.anchorMax = new Vector2(0.5f, 0f);
            hotbarRt.pivot     = new Vector2(0.5f, 0f);
            hotbarRt.anchoredPosition = new Vector2(0f, PAD);
            hotbarRt.sizeDelta = new Vector2(gridW, HOTBAR_HEIGHT);
            var hotbarLayout = hotbarGo.AddComponent<GridLayoutGroup>();
            hotbarLayout.cellSize        = new Vector2(CELL, CELL);
            hotbarLayout.spacing         = new Vector2(SPACING, SPACING);
            hotbarLayout.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
            hotbarLayout.constraintCount = COLS;
            hotbarLayout.startAxis       = GridLayoutGroup.Axis.Horizontal;
            hotbarLayout.startCorner     = GridLayoutGroup.Corner.UpperLeft;
            hotbarLayout.childAlignment  = TextAnchor.UpperCenter;
            // Subtle background to read as a separate band.
            var hotbarBg = hotbarGo.AddComponent<Image>();
            hotbarBg.color = new Color(0.2f, 0.18f, 0.12f, 0.5f);
            hotbarBg.raycastTarget = false;

            // Inventory grid sits directly above the hotbar.
            var gridGo = MakeUIChild(panelGo.transform, "Slots");
            var gridRt = (RectTransform)gridGo.transform;
            gridRt.anchorMin = new Vector2(0.5f, 0f);
            gridRt.anchorMax = new Vector2(0.5f, 0f);
            gridRt.pivot     = new Vector2(0.5f, 0f);
            gridRt.anchoredPosition = new Vector2(0f, PAD + HOTBAR_HEIGHT + SECTION_GAP);
            gridRt.sizeDelta = new Vector2(gridW, gridH);
            var grid = gridGo.AddComponent<GridLayoutGroup>();
            grid.cellSize        = new Vector2(CELL, CELL);
            grid.spacing         = new Vector2(SPACING, SPACING);
            grid.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = COLS;
            grid.startAxis       = GridLayoutGroup.Axis.Horizontal;
            grid.startCorner     = GridLayoutGroup.Corner.UpperLeft;
            grid.childAlignment  = TextAnchor.UpperCenter;

            // HUD controller — wires the slot-view prefab + both containers so the HUD
            // can route slots 0-31 into the main grid and 32-39 into the hotbar row.
            // craftingBarSection lets the HUD hide its 4-slot bar when the table UI opens.
            var hud = canvasGo.AddComponent<InventoryHUD>();
            SetSerialized(hud, "inventory",          inventory);
            SetSerialized(hud, "panel",              panelGo);
            SetSerialized(hud, "slotsContainer",     gridRt);
            SetSerialized(hud, "hotbarContainer",    hotbarRt);
            SetSerialized(hud, "slotViewPrefab",     slotPrefab);
            SetSerialized(hud, "craftingBarSection", craftRowGo);

            // Crafting controller — same canvas root, references the input/output slot views
            // we just instantiated + the recipe + the inventory.
            var craftingPanel = canvasGo.AddComponent<CraftingPanel>();
            SetSerialized(craftingPanel, "inventory",      inventory);
            SetSerialized(craftingPanel, "inventoryHud",   hud);
            SetSerialized(craftingPanel, "outputSlotView", outputView);
            SetSerialized(craftingPanel, "craftButton",    craftBtn);
            SetSerializedObjectList(craftingPanel, "inputSlotViews", inputViews);
            if (craftingTableRecipe != null)
                SetSerializedObjectList(craftingPanel, "recipes", new[] { craftingTableRecipe });

            // Same loud-fail diagnostic as before — catches stale-DLL field-type mismatches
            // where SerializedObject silently drops the assignment.
            var hudSerialized = new SerializedObject(hud);
            var inventoryProp = hudSerialized.FindProperty("inventory");
            if (inventoryProp != null && inventoryProp.objectReferenceValue == null && inventory != null)
            {
                Debug.LogError("[W3_SceneBuilder] InventoryHUD.inventory wouldn't accept the InventorySO ref — " +
                               "almost certainly a STALE compiled DLL. Switch to Unity, wait for compile, re-run Celestia/W3/Build Everything.");
            }
            else
            {
                Debug.Log($"[W3_SceneBuilder]   InventoryHUD.inventory ← {(inventory != null ? inventory.name : "<null>")} (OK)");
            }

            // Hidden by default — only shows on I press.
            panelGo.SetActive(false);
        }

        // ── Crafting table UI (unified panel) ────────────────────────────────

        private static void BuildCraftingTableCanvas(Transform parent, InventorySO inventory)
        {
            var slotPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(W3_AssetBuilder.PREFAB_INV_SLOT);
            if (slotPrefab == null)
            {
                Debug.LogError("[W3_SceneBuilder] Slot prefab missing — table UI not built.");
                return;
            }

            var canvasGo = new GameObject(TABLE_HUD_NAME);
            canvasGo.transform.SetParent(parent, false);
            canvasGo.layer = LayerMask.NameToLayer("UI");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 110; // above the inventory canvas

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight  = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            // Sections stacked top → bottom inside ONE panel:
            //   Title bar | Crafting row | Inventory grid | Hotbar row
            // The hotbar / inventory share the regular HUD's column count + cell size so
            // the same InventorySlotView prefab fits cleanly. Crafting uses larger cells
            // for a focused, full-attention feel.
            const int   INV_COLS         = 10, INV_ROWS = 4;
            const int   CRAFT_COLS       = 3,  CRAFT_ROWS = 3;
            const float INV_CELL         = 80f, INV_SPACING = 6f;
            const float CRAFT_CELL       = 96f, CRAFT_SPACING = 10f;
            const float TITLE_HEIGHT     = 52f, SECTION_GAP = 18f, PAD = 28f;
            const float ARROW_W          = 80f, BUTTON_W   = 168f;
            const float CLOSE_BTN_SIZE   = 36f;

            float craftBodyW   = CRAFT_COLS * CRAFT_CELL + (CRAFT_COLS - 1) * CRAFT_SPACING;
            float craftBodyH   = CRAFT_ROWS * CRAFT_CELL + (CRAFT_ROWS - 1) * CRAFT_SPACING;
            float craftRightW  = ARROW_W + CRAFT_SPACING * 2 + CRAFT_CELL + CRAFT_SPACING * 2 + BUTTON_W;
            float invGridW     = INV_COLS * INV_CELL + (INV_COLS - 1) * INV_SPACING;
            float invGridH     = INV_ROWS * INV_CELL + (INV_ROWS - 1) * INV_SPACING;
            float hotbarH      = INV_CELL;

            // Panel must fit the WIDEST section (the inventory grid) with breathing room.
            float panelW = Mathf.Max(invGridW, craftBodyW + craftRightW + CRAFT_SPACING * 4) + PAD * 2;
            float panelH = TITLE_HEIGHT + SECTION_GAP + craftBodyH + SECTION_GAP + invGridH + SECTION_GAP + hotbarH + PAD * 2;

            // Centred screen panel.
            var panelGo = MakeUIChild(canvasGo.transform, "Panel");
            var panelRt = (RectTransform)panelGo.transform;
            panelRt.anchorMin = panelRt.anchorMax = panelRt.pivot = new Vector2(0.5f, 0.5f);
            panelRt.anchoredPosition = Vector2.zero;
            panelRt.sizeDelta = new Vector2(panelW, panelH);
            var panelImg = panelGo.AddComponent<Image>();
            panelImg.color = new Color(0f, 0f, 0f, 0.88f);

            // Title bar.
            var titleGo = MakeUIChild(panelGo.transform, "Title");
            var titleRt = (RectTransform)titleGo.transform;
            titleRt.anchorMin = new Vector2(0f, 1f);
            titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.pivot     = new Vector2(0.5f, 1f);
            titleRt.anchoredPosition = new Vector2(0f, -PAD * 0.25f);
            titleRt.sizeDelta = new Vector2(0f, TITLE_HEIGHT);
            var titleText = titleGo.AddComponent<TextMeshProUGUI>();
            titleText.text      = "Crafting Table";
            titleText.alignment = TextAlignmentOptions.Center;
            titleText.fontSize  = 28f;
            titleText.color     = Color.white;

            // Close button — top-right "X".
            var closeBtn = BuildCloseButton(panelGo.transform, CLOSE_BTN_SIZE);

            // Y-offsets for each section, measured from the panel's TOP edge downward.
            float yCrafting  = -(PAD + TITLE_HEIGHT + SECTION_GAP);
            float yInventory = yCrafting - craftBodyH - SECTION_GAP;
            float yHotbar    = yInventory - invGridH - SECTION_GAP;

            // ── Crafting row (3×3 inputs + arrow + output + Craft button) ────
            var craftRowGo = MakeUIChild(panelGo.transform, "CraftingRow");
            var craftRowRt = (RectTransform)craftRowGo.transform;
            craftRowRt.anchorMin = new Vector2(0.5f, 1f);
            craftRowRt.anchorMax = new Vector2(0.5f, 1f);
            craftRowRt.pivot     = new Vector2(0.5f, 1f);
            craftRowRt.anchoredPosition = new Vector2(0f, yCrafting);
            craftRowRt.sizeDelta = new Vector2(craftBodyW + craftRightW + CRAFT_SPACING * 4, craftBodyH);

            // 3×3 input grid — left of the row.
            var inputsGo = MakeUIChild(craftRowGo.transform, "Inputs");
            var inputsRt = (RectTransform)inputsGo.transform;
            inputsRt.anchorMin = new Vector2(0f, 0f);
            inputsRt.anchorMax = new Vector2(0f, 1f);
            inputsRt.pivot     = new Vector2(0f, 0.5f);
            inputsRt.anchoredPosition = new Vector2(0f, 0f);
            inputsRt.sizeDelta = new Vector2(craftBodyW, 0f);
            var inputsGrid = inputsGo.AddComponent<GridLayoutGroup>();
            inputsGrid.cellSize        = new Vector2(CRAFT_CELL, CRAFT_CELL);
            inputsGrid.spacing         = new Vector2(CRAFT_SPACING, CRAFT_SPACING);
            inputsGrid.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
            inputsGrid.constraintCount = CRAFT_COLS;

            var inputViews = new InventorySlotView[CRAFT_COLS * CRAFT_ROWS];
            for (int i = 0; i < inputViews.Length; i++)
            {
                var slotGo = (GameObject)PrefabUtility.InstantiatePrefab(slotPrefab, inputsGo.transform);
                slotGo.name = $"TableInput_{i}";
                inputViews[i] = slotGo.GetComponent<InventorySlotView>();
            }

            // Arrow + output + Craft button — right of the row, vertically centred.
            var rightRow = MakeUIChild(craftRowGo.transform, "RightRow");
            var rightRt = (RectTransform)rightRow.transform;
            rightRt.anchorMin = new Vector2(0f, 0.5f);
            rightRt.anchorMax = new Vector2(0f, 0.5f);
            rightRt.pivot     = new Vector2(0f, 0.5f);
            rightRt.anchoredPosition = new Vector2(craftBodyW + CRAFT_SPACING * 4, 0f);
            rightRt.sizeDelta = new Vector2(craftRightW, CRAFT_CELL);
            var rightLayout = rightRow.AddComponent<HorizontalLayoutGroup>();
            rightLayout.childAlignment       = TextAnchor.MiddleLeft;
            rightLayout.spacing              = CRAFT_SPACING;
            rightLayout.childControlWidth    = false;
            rightLayout.childControlHeight   = false;
            rightLayout.childForceExpandWidth  = false;
            rightLayout.childForceExpandHeight = false;

            var arrowGo = MakeUIChild(rightRow.transform, "Arrow");
            var arrowLayout = arrowGo.AddComponent<LayoutElement>();
            arrowLayout.preferredWidth  = ARROW_W;
            arrowLayout.preferredHeight = CRAFT_CELL;
            var arrowText = arrowGo.AddComponent<TextMeshProUGUI>();
            arrowText.text      = "→";
            arrowText.alignment = TextAlignmentOptions.Center;
            arrowText.fontSize  = 48f;
            arrowText.color     = Color.white;

            var outputGo = (GameObject)PrefabUtility.InstantiatePrefab(slotPrefab, rightRow.transform);
            outputGo.name = "TableOutput";
            var outputLE = outputGo.GetComponent<LayoutElement>() ?? outputGo.AddComponent<LayoutElement>();
            outputLE.preferredWidth  = CRAFT_CELL;
            outputLE.preferredHeight = CRAFT_CELL;
            var outputView = outputGo.GetComponent<InventorySlotView>();

            var craftBtn = BuildCraftButton(rightRow.transform, BUTTON_W, CRAFT_CELL);

            // ── Inventory grid (8×4 = 32 slots, slots 0-31) ─────────────────
            var invGridGo = MakeUIChild(panelGo.transform, "InventoryGrid");
            var invGridRt = (RectTransform)invGridGo.transform;
            invGridRt.anchorMin = new Vector2(0.5f, 1f);
            invGridRt.anchorMax = new Vector2(0.5f, 1f);
            invGridRt.pivot     = new Vector2(0.5f, 1f);
            invGridRt.anchoredPosition = new Vector2(0f, yInventory);
            invGridRt.sizeDelta = new Vector2(invGridW, invGridH);
            var invGrid = invGridGo.AddComponent<GridLayoutGroup>();
            invGrid.cellSize        = new Vector2(INV_CELL, INV_CELL);
            invGrid.spacing         = new Vector2(INV_SPACING, INV_SPACING);
            invGrid.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
            invGrid.constraintCount = INV_COLS;

            // ── Hotbar (10×1 = 10 slots, slots 40-49) ────────────────────────
            var hotbarGo = MakeUIChild(panelGo.transform, "Hotbar");
            var hotbarRt = (RectTransform)hotbarGo.transform;
            hotbarRt.anchorMin = new Vector2(0.5f, 1f);
            hotbarRt.anchorMax = new Vector2(0.5f, 1f);
            hotbarRt.pivot     = new Vector2(0.5f, 1f);
            hotbarRt.anchoredPosition = new Vector2(0f, yHotbar);
            hotbarRt.sizeDelta = new Vector2(invGridW, hotbarH);
            var hotbarGrid = hotbarGo.AddComponent<GridLayoutGroup>();
            hotbarGrid.cellSize        = new Vector2(INV_CELL, INV_CELL);
            hotbarGrid.spacing         = new Vector2(INV_SPACING, INV_SPACING);
            hotbarGrid.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
            hotbarGrid.constraintCount = INV_COLS;
            // Subtle band background to read the hotbar as its own section.
            var hotbarBg = hotbarGo.AddComponent<Image>();
            hotbarBg.color         = new Color(0.2f, 0.18f, 0.12f, 0.5f);
            hotbarBg.raycastTarget = false;

            // ── Secondary InventoryHUD: drives the in-table grid + hotbar ──
            // ownsGlobalState = false so it doesn't fight the regular InventoryHUD over the
            // I-key / IsAnyOpen flag. CraftingTableUI handles open/close of THIS panel.
            var tableHud = canvasGo.AddComponent<InventoryHUD>();
            SetSerialized(tableHud, "inventory",       inventory);
            SetSerialized(tableHud, "panel",           panelGo);
            SetSerialized(tableHud, "slotsContainer",  invGridRt);
            SetSerialized(tableHud, "hotbarContainer", hotbarRt);
            SetSerialized(tableHud, "slotViewPrefab",  slotPrefab);
            SetSerializedBool(tableHud, "ownsGlobalState", false);

            // ── CraftingPanel: drives the 3×3 inputs + craft button ─────────
            var craftingPanel = canvasGo.AddComponent<CraftingPanel>();
            SetSerialized(craftingPanel, "inventory",      inventory);
            SetSerialized(craftingPanel, "inventoryHud",   tableHud);
            SetSerialized(craftingPanel, "outputSlotView", outputView);
            SetSerialized(craftingPanel, "craftButton",    craftBtn);
            SetSerializedObjectList(craftingPanel, "inputSlotViews", inputViews);

            // Pre-wire the same Crafting-Table recipe the inventory's 4-slot bar uses,
            // so the placed table can craft tables too (designers add more recipes via
            // Inspector).
            var craftingTableRecipe = AssetDatabase.LoadAssetAtPath<RecipeSO>(W3_AssetBuilder.RECIPE_CRAFTING_TABLE);
            if (craftingTableRecipe != null)
                SetSerializedObjectList(craftingPanel, "recipes", new[] { craftingTableRecipe });

            // ── CraftingTableUI: owns the open/close + hides the regular HUD ─
            // The regular InventoryHUD lives on the other (already-built) canvas under the
            // same GameManager parent — find it for the inventoryHud ref.
            var regularHud = FindRegularInventoryHud(parent, canvasGo);
            var tableUI = canvasGo.AddComponent<CraftingTableUI>();
            SetSerialized(tableUI, "panel",         panelGo);
            SetSerialized(tableUI, "craftingPanel", craftingPanel);
            SetSerialized(tableUI, "closeButton",   closeBtn);
            SetSerialized(tableUI, "inventoryHud",  regularHud);

            panelGo.SetActive(false);
        }

        /// <summary>Locate the OTHER InventoryHUD (on InventoryHUDCanvas), skipping the one
        /// we just added to the table canvas.</summary>
        private static InventoryHUD FindRegularInventoryHud(Transform parent, GameObject excludeCanvas)
        {
            foreach (var hud in parent.GetComponentsInChildren<InventoryHUD>(true))
                if (hud != null && hud.gameObject != excludeCanvas) return hud;
            return null;
        }

        private static Button BuildCloseButton(Transform parent, float size)
        {
            var closeGo = MakeUIChild(parent, "CloseButton");
            var closeRt = (RectTransform)closeGo.transform;
            closeRt.anchorMin = new Vector2(1f, 1f);
            closeRt.anchorMax = new Vector2(1f, 1f);
            closeRt.pivot     = new Vector2(1f, 1f);
            closeRt.anchoredPosition = new Vector2(-8f, -8f);
            closeRt.sizeDelta = new Vector2(size, size);
            var img = closeGo.AddComponent<Image>();
            img.color = new Color(0.55f, 0.15f, 0.15f, 0.9f);
            var btn = closeGo.AddComponent<Button>();
            btn.targetGraphic = img;
            var labelGo = MakeUIChild(closeGo.transform, "X");
            var labelRt = (RectTransform)labelGo.transform;
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;
            var label = labelGo.AddComponent<TextMeshProUGUI>();
            label.text          = "X";
            label.alignment     = TextAlignmentOptions.Center;
            label.fontSize      = 22f;
            label.color         = Color.white;
            label.raycastTarget = false;
            return btn;
        }

        private static Button BuildCraftButton(Transform parent, float width, float height)
        {
            var btnGo = MakeUIChild(parent, "CraftButton");
            var le = btnGo.AddComponent<LayoutElement>();
            le.preferredWidth  = width;
            le.preferredHeight = height;
            var img = btnGo.AddComponent<Image>();
            img.color = new Color(0.15f, 0.45f, 0.2f, 0.9f);
            var btn = btnGo.AddComponent<Button>();
            btn.targetGraphic = img;
            var labelGo = MakeUIChild(btnGo.transform, "Label");
            var labelRt = (RectTransform)labelGo.transform;
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;
            var label = labelGo.AddComponent<TextMeshProUGUI>();
            label.text          = "Craft";
            label.alignment     = TextAlignmentOptions.Center;
            label.fontSize      = 28f;
            label.color         = Color.white;
            label.raycastTarget = false;
            return btn;
        }

        private static GameObject MakeUIChild(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);
            return go;
        }

        private static void EnsureEventSystem(Transform parent)
        {
            // Already in scene? Done. FindAnyObjectByType is the non-deprecated equivalent —
            // we don't care about deterministic ordering here; we just want to know whether
            // *any* EventSystem exists in any loaded scene.
            var existing = Object.FindAnyObjectByType<EventSystem>(FindObjectsInactive.Include);
            if (existing != null) return;

            var go = new GameObject(EVENT_SYSTEM_NAME);
            go.transform.SetParent(parent, false);
            go.AddComponent<EventSystem>();
            // Project uses the new Input System (CLAUDE.md §1) — pick the matching UI module.
            go.AddComponent<InputSystemUIInputModule>();
        }

        private static void SetSerializedObjectList<T>(Object target, string fieldName, System.Collections.Generic.IList<T> values) where T : Object
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(fieldName);
            if (prop == null || !prop.isArray)
            {
                Debug.LogWarning($"List field '{fieldName}' not found (or not an array) on {target}");
                return;
            }
            prop.arraySize = values.Count;
            for (int i = 0; i < values.Count; i++)
                prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetSerialized(Object target, string fieldName, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(fieldName);
            if (prop == null) { Debug.LogWarning($"Field '{fieldName}' not found on {target}"); return; }
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetSerializedBool(Object target, string fieldName, bool value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(fieldName);
            if (prop == null) { Debug.LogWarning($"Field '{fieldName}' not found on {target}"); return; }
            prop.boolValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
