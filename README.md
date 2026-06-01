# RPGStarter

A small Unity 6 ARPG starter kit built around a single editor menu that wires up the whole project in one click. Showcases:

- **Phantom Archer player** (Mixamo humanoid + custom controller) — movement, dodge, ranged combat with a pooled arrow system
- **Monster enemy** — chase / attack / death FSM, health bar, damage numbers
- **8-slot weapon / tool roster** — Bow, Sword + Shield, Axe, Mace, Spear (combat) · Hammer, Pickaxe, Axe-Tool (gathering)
- **Inventory + 10-slot hotbar** — 50-slot grid, slot-based moves, persistent selection
- **Tree chopping + rock mining** — generic `RockMineable` harvest node, tool-kind gating, pickup spawning
- **Crafting** — 3×3 grid placeable crafting table, recipe matching
- **Bootstrap → additive scene load** — `GameManager` + `SceneLoader` via Addressables

Everything builds from a single menu — **`Celestia → Build → Build Everything`** (yes, the menu still says Celestia internally; rename `Celestia.EditorTools` to your own namespace if you want).

## Stack

Unity 6.4 LTS · URP · new Input System · Cinemachine 3 · Addressables · TextMeshPro

## Setup

```bash
git clone <your-repo-url> RPGStarter
cd RPGStarter
git lfs install
```

Open in Unity 6000.4+ and run:

1. **`Celestia → Demo → 1. Build Primitive Source Assets`** — generates Unity-primitive stand-ins for rocks / trees / weapons / table
2. **`Celestia → W1 → Build Everything`** through `W3 → Build Everything` in order (each is one click)
3. Open `Bootstrap.unity` → Play

## What's third-party

- **Unity StarterAssets** — Unity's free, redistributable ThirdPersonController sample
- **Mixamo character + animations** — Adobe Mixamo content, free to redistribute per Mixamo's licence
- **Pixel-art RPG icon packs** under `Assets/_Project/Art/Icons/` — author-stated CC-friendly

No paid asset-store packs included.

## Layout

```
Assets/
├── _Project/          # all gameplay code + content
│   ├── Art/           # character mesh, weapons, icons, materials
│   ├── Prefabs/       # player, enemies, UI, VFX, weapons
│   ├── ScriptableObjects/  # weapons, abilities, items, recipes, enemies
│   ├── Scenes/        # Bootstrap.unity + dev test scene
│   └── Scripts/       # runtime + editor builders
├── ThirdParty/        # only StarterAssets
├── Settings/          # URP renderer, input asset
└── AddressableAssetsData/
```

## License

MIT (see `LICENSE`). Third-party content under their respective licences.
