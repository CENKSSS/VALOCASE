# UI Layout Plan (Mobile)

## Orientation

- **Portrait** 1080×1920 reference (CanvasScaler: Match Width or Height 0.5)
- Safe area on all full-screen roots

## Main Menu

```
┌─────────────────────────────┐
│  [Agent Name]     12,450 VP │  ← VpCounterView animated
│  ● 8,421 agents online      │
├─────────────────────────────┤
│      VALOCASE (logo)        │
│   ┌─────────────────────┐   │
│   │  OPEN CASE  (hero)  │   │
│   └─────────────────────┘   │
│ [Inventory]  [Shop]         │
│ [Daily Reward]  [Settings]  │
│ ▓▓▓▓░░ Progression 45%      │
│ Stats strip                 │
└─────────────────────────────┘
```

## Case Opening

```
┌─────────────────────────────┐
│ ← Back        Case Name     │
│ ┌── scroll case chips ──┐   │
│ └─────────────────────────┘   │
│ ║ reel viewport (center) ║  │
│      ▼ winner marker        │
│ [OPEN 475 VP]  [SKIP]       │
└─────────────────────────────┘
```

## Inventory

- Grid **2 columns** (portrait) / 3–4 (landscape)
- Filter + Sort dropdowns top
- Tap card → detail popup with Sell

## Shop

- Featured carousel (horizontal)
- Daily deals row with 15% VP discount badge
- Rotation timer label (UTC)

## Rarity colors

| Rarity | Hex (approx) |
|--------|----------------|
| Select | `#8C9199` |
| Deluxe | `#3399F2` |
| Premium | `#5933D9` |
| Exclusive | `#E63946` |
| Ultra | `#FFD133` |

## Animation targets

- Screen fade 0.25s
- VP counter 0.45s smooth step
- Reel spin 5.5s OutQuint
- Button scale 0.95→1 on press (optional DOTween)
