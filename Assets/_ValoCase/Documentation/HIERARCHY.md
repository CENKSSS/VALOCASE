# Unity Hierarchy Structure

## Scene: Bootstrap (index 0)

```
Bootstrap
├── [Systems]
│   ├── GameContext          (GameContext.cs — DontDestroyOnLoad)
│   ├── SoundManager         (SoundManager.cs)
│   ├── HapticManager        (HapticManager.cs)
│   └── PoolManager          (optional here or in Main)
├── [UI]
│   └── LoadingScreen        (LoadingScreenController + Canvas)
└── GameBootstrap            (GameBootstrap.cs → loads Main)
```

## Scene: Main

```
Main
├── EventSystem
├── UICanvas                 (Screen Space - Camera, CanvasScaler 1080 ref)
│   ├── SafeAreaRoot         (SafeAreaFitter)
│   │   ├── TopBar
│   │   │   ├── VpCounterView
│   │   │   └── ProfileChip
│   │   ├── ScreenContainer
│   │   │   ├── MainMenuScreen
│   │   │   ├── CaseOpeningScreen
│   │   │   │   ├── CaseList (ScrollRect)
│   │   │   │   ├── ReelViewport
│   │   │   │   │   ├── CenterMarker
│   │   │   │   │   └── ReelContent (CaseSpinController.reelContent)
│   │   │   │   ├── OpenButton / SkipButton
│   │   │   │   └── CaseOpeningFlowController
│   │   │   ├── InventoryScreen
│   │   │   │   └── Grid (ScrollRect content)
│   │   │   ├── ShopScreen
│   │   │   │   ├── FeaturedRow
│   │   │   │   └── DailyDealsRow
│   │   │   └── SettingsScreen
│   │   ├── Overlays
│   │   │   ├── DailyRewardPopup
│   │   │   ├── SkinDetailPopup
│   │   │   ├── ToastView
│   │   │   └── UltraRevealEffect
│   │   └── BottomNav (optional shortcuts)
│   └── UINavigator
└── MainCamera
```

## DontDestroyOnLoad

- `GameContext`
- `SoundManager`
- `HapticManager`

## Pool roots (under PoolManager)

```
PoolManager
├── ReelPoolRoot
└── CardPoolRoot
```
