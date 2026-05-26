# ValoCase

VALORANT-inspired mobile case opening simulator for **portfolio / learning only**.

## Quick start

1. Open in Unity 2022.3+ (created on Unity 6).
2. **ValoCase → Generate Sample Content**
3. Follow `Documentation/SETUP.md` and `Documentation/HIERARCHY.md` to wire scenes.

## Architecture

```
Core (GameContext, events, bootstrap)
  → Services (VP, inventory, cases, shop, daily, stats, save)
  → Data (ScriptableObjects)
  → UI (screens, pooling, animations)
  → CaseOpening (reel spin flow)
```

## Key docs

| File | Purpose |
|------|---------|
| `Documentation/SETUP.md` | Install DOTween, Android, first run |
| `Documentation/HIERARCHY.md` | Scene object tree |
| `Documentation/PREFABS.md` | Prefab list |
| `Documentation/UI_LAYOUT.md` | Mobile layout wireframes |
| `Documentation/SCRIPTABLE_OBJECTS.md` | Data examples |
| `Documentation/DEVELOPMENT_ROADMAP.md` | Phased plan |
| `Documentation/OPTIMIZATION.md` | Performance notes |

Not affiliated with Riot Games.
