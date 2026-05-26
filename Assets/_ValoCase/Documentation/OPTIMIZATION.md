# Optimization Suggestions

## UI

- Use **one Canvas** for static HUD, separate canvas only if dynamic sorting needed
- Enable **CanvasGroup** culling on hidden screens (already faded off)
- Prefer **ObjectPool** for reel items and inventory cards (implemented)
- Disable raycasts on non-interactive Images

## Scripts

- No per-frame logic in services — event-driven only
- `SafeAreaFitter` only updates when `Screen.safeArea` changes
- `FakeOnlineCountService` refresh on menu show, not every frame
- Avoid `Update()` in screens except CaseOpening skip state (minimal)

## Mobile

- Texture atlases for UI sprites (2048 max atlas)
- Compress audio to Vorbis, short SFX &lt; 200kb
- Target **60 FPS**, disable vsync test on device
- Use **IL2CPP** + ARM64

## Memory

- Load icons via Addressables when skin count &gt; 100
- Release spin reel pool on screen hide (`CaseSpinController.OnDisable`)
- Limit reel strip length (`GameConstants.ReelPaddingItems`)

## Build size

- Strip unused Unity modules
- Omit Visual Scripting / Timeline if unused
- Don't commit `Library/`

## Profiling checklist

1. Unity Profiler → UI.Rendering
2. Frame Debugger → batch count on inventory scroll
3. Memory Profiler after 50 case opens (pool leaks)
