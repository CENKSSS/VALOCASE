# ValoCase — Setup Guide

## Requirements

- Unity **2022.3 LTS** or newer (project opened on Unity 6)
- Android Build Support module
- TextMeshPro (import TMP Essentials on first open)
- **DOTween** (recommended): Asset Store / Demigiant — then add scripting define `VALOCASE_DOTWEEN`

## Quick Start

1. Open project in Unity.
2. Menu: **ValoCase → Generate Sample Content**
3. Menu: **ValoCase → Setup Sample Scene (UI + Systems)** — creates `PF_UICanvas` and wires `SampleScene`
4. Press **Play** — you should see the main menu (dark UI + red buttons).

Or manually drag from Project:
- `Assets/_ValoCase/Prefabs/PF_GameContext.prefab`
- `Assets/_ValoCase/Prefabs/PF_UICanvas.prefab`

3. Create scenes (see `HIERARCHY.md`) if building from scratch:
   - `Bootstrap` — `GameContext`, `SoundManager`, `HapticManager`, `GameBootstrap`, `LoadingScreen`
   - `Main` — full UI canvas + `UINavigator` + screens
4. Add both scenes to **Build Settings** (Bootstrap index 0).
5. Assign `ContentDatabase` and `GameConfig` from `Assets/_ValoCase/Resources/` to `GameContext` if not loaded via Resources.
6. Switch platform to **Android**, set portrait or landscape per `UI_LAYOUT.md`.

## DOTween

1. Import DOTween.
2. **Edit → Project Settings → Player → Scripting Define Symbols** → add `VALOCASE_DOTWEEN`
3. Recompile — reel/UI tweens use DOTween; without it, coroutine fallbacks run.

## Android Notes

- Minimum API 24+ recommended
- Enable **IL2CPP** + ARM64 for release builds
- Assign vibration permission is handled via legacy vibrator API in `HapticManager`

## Portfolio Disclaimer

Fan recreation for learning/portfolio. Not affiliated with Riot Games. Do not ship commercially with Riot IP.
