# RPGStarter

A small Unity 6 ARPG starter kit built around a single editor menu that wires up the whole project in one click. Showcases:

- **Phantom Archer player** (Mixamo humanoid + custom controller) — movement, dodge, ranged combat with a pooled arrow system
- **Monster enemy** — chase / attack / death FSM, health bar, damage numbers
- **5-slot hotbar** — Bow (arrow shot), Sword + Shield (melee), Hammer / Pickaxe / Axe-Tool (gathering tools)
- **Inventory + 10-slot hotbar UI** — 50-slot grid, slot-based moves, persistent selection
- **Tree chopping + rock mining** — generic `RockMineable` harvest node, tool-kind gating, pickup spawning
- **Crafting** — 3×3 grid placeable crafting table, recipe matching
- **Bootstrap → additive scene load** — `GameManager` + `SceneLoader` via Addressables

Everything builds from a single menu — **`RPGStarter → Build Demo`**.

## Stack

Unity 6.4 LTS · URP · new Input System · Cinemachine 3 · Addressables · TextMeshPro

## Setup

```bash
git clone https://github.com/joseph280/rpgstarter.git
cd rpgstarter
git lfs install
git lfs pull
```

Open in Unity 6000.4+ and run **`RPGStarter → Build Demo`**. Then open `Bootstrap.unity` → Play.

## Visuals out-of-the-box vs upgraded

The kit ships with **Unity-primitive stand-ins** for rocks, trees, and the bow so the demo is 100% redistributable (zero paid asset-store content). Functionally everything works — you can chop, mine, shoot, equip, craft — but the bow looks like a thin brown cube, rocks are spheres, and trees are a cylinder + sphere.

If you want proper art, drop in any of these **free, redistributable** packs and the kit will use them automatically (or with a one-line constant change):

### Recommended free packs

| Need | Pack | License | Where |
|---|---|---|---|
| Bow + other weapons | **Quaternius — Ultimate Modular Weapons** | CC0 | [quaternius.com](https://quaternius.com) |
| Trees + foliage | **Quaternius — Ultimate Stylized Nature** | CC0 | [quaternius.com](https://quaternius.com) |
| Rocks + props | **Quaternius — Ultimate Modular Environment** | CC0 | [quaternius.com](https://quaternius.com) |
| Alternate weapons | **Kenney — Weapon Pack** | CC0 | [kenney.nl](https://kenney.nl) |

All four are CC0 — no attribution required, redistributable in any project. Drop the unitypackage into `Assets/ThirdParty/<PackName>/` and either:

- **Drag-replace** the visual mesh inside the generated prefabs (`Rock_Mineable.prefab`, `Tree_Choppable.prefab`, `Weapon_Bow.asset`'s `mainPrefab` field), **or**
- Change the source-prefab path constants in `Demo_PrimitiveAssets.cs` to point at the new mesh.

### What stays primitive regardless

- The **3 tools** (Hammer / Pickaxe / Axe-Tool) — composite handle + head primitives that read OK as tools
- The **table** (crafting table) — cube
- The **sword + shield** — actually real `.fbx` meshes already, no replacement needed

## What's third-party (in the repo)

- **Unity StarterAssets** — Unity's free, redistributable ThirdPersonController sample
- **Mixamo character + animations** — Adobe Mixamo content, free to redistribute per Mixamo's licence
- **Pixel-art RPG icon packs** under `Assets/_Project/Art/Icons/` — author-stated CC-friendly

No paid asset-store packs are bundled.

## Layout

```
Assets/
├── _Project/          # all gameplay code + content
│   ├── Art/           # character mesh, weapons, icons, materials
│   ├── Prefabs/       # player, enemies, UI, VFX, weapons, environment
│   ├── ScriptableObjects/  # weapons, abilities, items, recipes, enemies
│   ├── Scenes/        # Bootstrap.unity + Tests/PlayMode/Test_PlayerMovement.unity
│   └── Scripts/       # runtime gameplay + one-click Build.cs
├── ThirdParty/        # only StarterAssets
├── Settings/          # URP renderer, input asset
└── AddressableAssetsData/
```

## Single-menu build

The only thing in the editor menu is **`RPGStarter → Build Demo`**. Under the hood `Build.cs` runs 17 ordered steps:

1. Primitive source assets (rock / tree / weapons / table)
2. Physics layers
3-14. SO / animator / weapon / monster / player chain
15. Standard → URP material conversion
16. Addressables sync (with auto-Repair fallback if internals are corrupt)
17. Scene cleanup (strip stale RefPillars / DummyParent / TargetDummy) + harvest-node scatter

Plus one escape hatch: **`RPGStarter → Repair Addressables`** for when Addressables gets corrupt (rare but happens).

## License

MIT (see `LICENSE`). Third-party content under their respective licences.
