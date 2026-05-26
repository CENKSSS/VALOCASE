# Prefab Recommendations

| Prefab | Components | Notes |
|--------|------------|-------|
| `PF_GameContext` | GameContext | Drag ContentDatabase + GameConfig |
| `PF_ReelItem` | ReelItemView, LayoutElement (220×280) | Pool prefab; rarity frame + glow |
| `PF_SkinCard` | SkinCardView, Button | Grid cell 180×240 |
| `PF_CaseListItem` | CaseListItemView | Horizontal shop/case picker |
| `PF_UICanvas` | Canvas, CanvasScaler, UINavigator | Reference resolution 1080×1920 portrait |
| `PF_DailyRewardPopup` | DailyRewardPopup | Modal overlay |
| `PF_SkinDetailPopup` | SkinDetailPopup | Full-screen dim + panel |
| `PF_LoadingScreen` | LoadingScreenController | Bootstrap only |

## Visual polish

- Use **9-slice** panels with dark `#0f1923` base and red `#ff4655` accents
- Add **UIOutline** / secondary glow Image on Ultra reel items
- Assign placeholder sprites for skin icons until art pass
- Hook `UIButtonFeedback` on all Buttons

## Audio prefab slots

Assign clips on `SoundManager`: click, spin loop, reveal, ultra, VP gain, sell, daily claim.
