# Eski Mimari Blueprint

**Oluşturulma tarihi:** 2026-05-26
**Amaç:** Refactor öncesi mevcut mimariyi dondurmak. İhtiyaç olursa bu belge baz alınarak eski davranışa dönülebilir.

---

## 1. Yüksek Seviye Mimari

```
┌─────────────────────────────────────────────────────────────────────┐
│                          BOOTSTRAP SAHNESİ                          │
│  GameBootstrap → LoadingScreen → GameContext.Awake → MainScene yük  │
└─────────────────────────────────────────────────────────────────────┘
                                  ↓
┌─────────────────────────────────────────────────────────────────────┐
│                            MAIN SAHNESİ                              │
│                                                                      │
│  ┌─────────────────┐  ┌──────────────────┐  ┌──────────────────┐  │
│  │  GameContext    │  │ CompositionRoot  │  │   PF_UICanvas    │  │
│  │  (Singleton)    │  │  (Wire systems)  │  │ (UINavigator vb) │  │
│  └────────┬────────┘  └─────────┬────────┘  └──────────────────┘  │
│           │                      │                                   │
│           ▼                      ▼                                   │
│      Services                CaseBattle                              │
│      (Interface-based DI)    (Inject)                                │
└─────────────────────────────────────────────────────────────────────┘
```

**Tekil sorumluluk dağılımı:**

| Katman | Sorumluluk | Dosyalar |
|--------|-----------|----------|
| **Bootstrap** | İlk sahne, GameContext yüklemesi | `GameBootstrap.cs` |
| **Core** | Global servisler, sabitler, eventler | `GameContext.cs`, `GameEvents.cs`, `GameConstants.cs`, `CompositionRoot.cs` |
| **Services** | İş mantığı (Interface + Impl) | `Interfaces.cs`, `GameServices.cs`, `UpgradeService`, `CaseOpeningService` |
| **Data** | SO'lar, save modelleri, rarity | `*SO.cs`, `SaveModels.cs`, `RaritySystem.cs`, `ContentDatabaseSO.cs` |
| **CaseOpening** | Kasa akış kontrolörleri | `CaseOpeningFlowController.cs`, `CaseSpinController.cs` |
| **Systems** | Yeni nesil mini-sistemler | `UpgradeSystem.cs`, `InventorySystem.cs`, `CaseBattleSystem.cs` |
| **UI** | Ekranlar, navigator, popup | `UI/Screens/*.cs`, `UINavigator.cs`, `SkinWinPopup.cs` |
| **Animation** | TweenFacade, RouletteAnimationController, UpgradeSpinAnimator | `Animation/*.cs`, `UI/Animation/*.cs` |
| **Editor** | Prefab builder, content generator | `ValoCaseUIBuilder.cs`, `ValoCaseContentGenerator.cs` |

---

## 2. Bootstrap → GameContext → Services Akışı

```
GameBootstrap.Start()
  │ 1. LoadingScreen göster
  │ 2. minimumLoadSeconds bekle
  │ 3. GameContext.Instance null mı kontrol et
  │ 4. SceneManager.LoadSceneAsync("Main")
  │ 5. LoadingScreen.Hide()
  ▼
GameContext.Awake()  [DontDestroyOnLoad]
  │ • Instance set
  │ • ContentDatabaseSO, GameConfigSO, RarityVisualSO Resources'tan yükle
  │ • contentDatabase.BuildLookups()  → FileSystemSkinLoader.LoadAll()
  │ • InitializeServices() → GameServicesFactory.Create(...)
  ▼
GameServicesFactory.Create(contentDb, gameConfig)
  │ Constructor chain (bağımlılık sırası):
  │   1. JsonSaveRepository → SaveService.LoadOrCreate
  │   2. VpCurrencyService(save)
  │   3. InventoryService(save, db, config, vp)
  │   4. StatisticsService(save, db)
  │   5. CaseProgressionService(save, config)
  │   6. ShopService(save, db, config)  → EnsureRotation
  │   7. DailyRewardService(save, config, vp)
  │   8. FakeOnlineCountService(config) → Refresh
  │   9. CaseOpeningService(vp, inv, stats, prog, shop)
  │  10. UpgradeService(inv, db, save)
  ▼
GameEvents.RaiseVpChanged + RaiseStatisticsChanged + RaiseShopRotated
ProfileManager.EnsureInitialized()
```

**Servis bağımlılık grafiği:**

```
SaveService ←──── VpCurrencyService
     ↑                    ↑
     │                    │
InventoryService ─────────┤
     ↑                    │
     │                    │
StatisticsService         │
     ↑                    │
     │                    │
CaseOpeningService ───────┤
     ↑                    │
     │                    │
DailyRewardService ───────┘
```

---

## 3. CompositionRoot Sistemi

**Dosya:** `Scripts/Core/CompositionRoot.cs`
**Sahnede:** PF_UICanvas (veya ayrı obje) üzerinde MonoBehaviour

**Görev:** Cross-layer wiring — UI ekranlarına system inject etmek, system event'lerini animation katmanına bağlamak.

**Kural:** `GameContext.Instance` yalnızca burada okunur. Yeni sistemler buraya `WireXxx(ctx)` metoduyla eklenir.

**Aktif Wirings:**
- `WireCaseBattle(ctx)` — `CaseBattleSystem` oluşturulur ve `CaseBattleScreen.Inject()` ile veriliyor, animasyon eventleri bağlanıyor

**Şablon (henüz aktif değil):**
- `WireUpgrade(ctx)` (yorum satırı)
- `WireInventory(ctx)` (yorum satırı)

---

## 4. Save & Persistence Katmanı

```
ISaveRepository (interface)
  └─ JsonSaveRepository  (Application.persistentDataPath/valocase_save.json)

ISaveService
  └─ SaveService
       └─ Data: SaveDataRoot
            ├─ vpBalance, playerName, version, timestamps
            ├─ inventory: List<OwnedSkinSaveEntry>
            ├─ statistics: PlayerStatisticsSave
            ├─ caseProgress: CaseProgressSave
            ├─ shop: ShopSave (rotation seed + ids)
            └─ dailyReward: DailyRewardSave (streak + last claim)
```

**Persistence tetikleyicileri:**
- `UpgradeService.TryUpgrade()` sonunda
- `CaseOpeningFlowController.OnSpinFinished()` sonunda
- `GameContext.OnApplicationPause(true)` ve `OnApplicationQuit()` sonunda
- Manuel: `GameContext.Persist()`

---

## 5. Servis Interface Sözleşmeleri

| Interface | Önemli Üyeler |
|-----------|---------------|
| `IVpCurrencyService` | `Balance`, `CanAfford`, `TrySpend`, `Add`, `SetBalance` |
| `IInventoryService` | `Items`, `UniqueCount`, `TotalCount`, `Owns`, `AddSkin`, `TrySell`, `ConsumeOne` |
| `IUpgradeService` | `ComputeChance`, `GetEligibleTargets`, `TryUpgrade`, `OnUpgradeResolved` |
| `ICaseOpeningService` | `CanOpen`, `TryBeginOpen`, `CompleteOpen`, `TryOpenCaseInstant`, `RollSkin` |
| `IShopService` | `FeaturedCases`, `DailyDeals`, `EnsureRotation`, `GetDiscountedPrice` |
| `IDailyRewardService` | `CurrentStreak`, `CanClaimToday`, `PeekTodayReward`, `TryClaim` |
| `IStatisticsService` | `RecordCaseOpened`, `RecordVpEarned`, `RecalculateInventoryStats` |
| `ICaseProgressionService` | `IsCaseUnlocked`, `ProgressionTier`, `TierProgress01`, `OnCaseOpened` |
| `IFakeOnlineService` | `CurrentOnlineCount`, `Refresh` |
| `ISaveService` | `Data`, `LoadOrCreate`, `Save`, `ResetSave` |

---

## 6. RaritySystem (Tek Doğruluk Kaynağı)

**Dosya:** `Scripts/Data/RaritySystem.cs` — `static class`

| Rarity | Rank | VP | Tam İsim | Kısa İsim |
|--------|------|-----|----------|-----------|
| Select | 0 | 1000 | Özel Seri | Özel |
| Deluxe | 1 | 2000 | Üstün Seri | Üstün |
| Premium | 2 | 3000 | İhtişamlı Seri | İhtişamlı |
| Ultra | 3 | 4000 | Ultra Seri | Ultra |
| Exclusive | 4 | 5000 | Seçkin Seri | Seçkin |

**Upgrade chance formula:**
```
chance = (100 - (targetRank - inputRank) * 20) / 100
clamp [0, 1]
```
| Rank Δ | Chance |
|--------|--------|
| 0 | 1.00 (100%) |
| +1 | 0.80 |
| +2 | 0.60 |
| +3 | 0.40 |
| +4 | 0.20 |
| <0 | 0.00 (geçersiz hedef) |

---

## 7. UI Katmanı — UINavigator + Screens

### UINavigator
**Dosya:** `Scripts/UI/UINavigator.cs`
**Pattern:** Tek aktif ekran (CanvasGroup ile fade)

```csharp
Navigate(ScreenType type, bool instant = false)
```
- Mevcut ekrana `HideAnimated()` / `HideImmediate()`
- Yeni ekrana `ShowAnimated()` / `ShowImmediate()`

### UIScreenBase
**Dosya:** `Scripts/UI/UIScreenBase.cs`
**Lifecycle:**
- `ShowImmediate()` → `OnShown()` virtual
- `HideImmediate()` → `OnHidden()` virtual
- `ShowAnimated()` / `HideAnimated()` — `CanvasGroup` fade (varsayılan 0.25s)
- `EnsureInteractive()` — async iş bitince input açmak için

### ScreenType enum
| Değer | Sınıf | Notlar |
|-------|-------|--------|
| MainMenu | `MainMenuScreen` | Profil, online sayısı, daily, buton barı |
| CaseOpening | `CaseOpeningScreen` | Kasa seçici, spin overlay, drop list |
| Inventory | `InventoryScreen` | Filtre + sıralama + grid |
| Shop | `ShopScreen` | Featured + Daily Deals |
| Settings | `SettingsScreen` | Toggle'lar, profil ismi |
| Weapons | `WeaponsScreen` | Silah grubuna göre skin grid |
| Upgrade | `UpgradeScreen` | Spin wheel + 2 panel + filterbar |
| CaseBattle | `CaseBattleScreen` | PvP roulette |
| EarnVp | `EarnVpScreen` | Reklam izle / günlük görev |

### UI Çıktıları (Singleton + Builder)
| Singleton | Dosya | Görev |
|-----------|-------|-------|
| `GameContext.Instance` | `GameContext.cs` | Tüm servisler |
| `PoolManager.Instance` | `PoolManager.cs` | ReelItem + SkinCard havuzu |
| `SkinWinPopup.Instance` | `SkinWinPopup.cs` | Loot reveal popup (lazy via `EnsureExists()`) |
| `SoundManager.Instance` | `SoundManager.cs` | SFX |
| `HapticManager.Instance` | `HapticManager.cs` | Mobil titreşim |
| `ProfileManager` | `ProfileManager.cs` | Avatar + isim |

---

## 8. ValoCaseUIBuilder — Editor Builder Sistemi

**Dosya:** `Editor/ValoCaseUIBuilder.cs` (~2350 satır, tek `static class`)
**MenuItem'lar:**
- `ValoCase → Build UI Prefabs` — sadece prefablar
- `ValoCase → Setup Current Scene (UI + Systems)` — prefablar + sahne kurulumu

### Builder'ın oluşturduğu prefab'lar
| Prefab | Path | Oluşturucu |
|--------|------|------------|
| `PF_UICanvas` | `Assets/_ValoCase/Prefabs/` | `BuildUiCanvasPrefab` |
| `PF_GameContext` | aynı | `BuildGameContextPrefab` |
| `PF_SkinCard` | aynı | `BuildSkinCardPrefab` |
| `PF_ReelItem` | aynı | `BuildReelItemPrefab` |
| `PF_CaseListItem` | aynı | `BuildCaseListItemPrefab` |
| `PF_DropItem` | aynı | `BuildDropItemPrefab` |
| `PF_WeaponSkinCard` | aynı | `BuildWeaponSkinCardPrefab` |

### PF_UICanvas hiyerarşisi
```
PF_UICanvas (Canvas, Scaler 1080×1920, Raycaster, UINavigator, PoolManager)
├─ EventSystem
├─ PoolRoots
│   ├─ ReelPoolRoot
│   └─ CardPoolRoot
└─ SafeArea (SafeAreaFitter)
    ├─ Screens (host)
    │   ├─ MainMenuScreen
    │   ├─ InventoryScreen
    │   ├─ ShopScreen
    │   ├─ SettingsScreen
    │   ├─ CaseOpeningScreen
    │   ├─ WeaponsScreen
    │   ├─ UpgradeScreen
    │   ├─ CaseBattleScreen
    │   └─ EarnVpScreen
    ├─ DailyRewardPopup
    ├─ Toast
    └─ SkinWinPopup     (en son sibling → üstte render)
```

### Builder'daki ana ekran üreticileri
| Metod | Üretir |
|-------|--------|
| `BuildMainMenuScreen` | MainMenuScreen + butonlar + profil widget'ı |
| `BuildSimpleScreen<T>` | Generic ekran (back butonlu boş kabuk) |
| `BuildCaseOpeningScreen` | Kasa listesi, display panel, spin overlay, skip btn |
| `BuildWeaponsScreen` | Tab strip + grid |
| `BuildUpgradeScreen` | İki yan panel + center spin + filterbar + tab + scroll |
| `BuildCaseBattleScreen` | (CaseBattleUiBuilder'a delege) |
| `BuildEarnVpScreen` | Boş kabuk |
| `BuildDailyPopup` | DailyRewardPopup |
| `BuildSkinDetailPopup` | SkinDetailPopup (envanterden) |
| `BuildSkinWinPopup` | SkinWinPopup (loot reveal — runtime fallback'i de var) |
| `BuildToast` | ToastView |

### Builder helper metodları
| Helper | İş |
|--------|----|
| `CreateRect(name, parent, size)` | RectTransform + Image |
| `CreateTmp(name, parent, text, size, align)` | TextMeshProUGUI |
| `CreateMenuButton(parent, name, label, color, pos, size)` | Standart buton |
| `StretchFull(rt)` / `StretchTop/Bottom/Center` | Anchor preset'leri |
| `ApplyNeonInteraction(btn, color)` | Buton hover/press tonları |
| `AddNeonGlow(go, color, dist)` | (Valorant düzleştirmesinde no-op) |
| `SavePrefab(go, path)` | Prefab'ı diske yaz |

---

## 9. Animation Katmanı

### TweenFacade
**Dosya:** `Animation/TweenFacade.cs`
**Pattern:** `#if VALOCASE_DOTWEEN` → DOTween; aksi takdirde coroutine fallback

```csharp
TweenFacade.ToFloat(host, from, to, dur, onUpdate, onComplete, ease)
TweenFacade.MoveAnchorX(rt, host, toX, dur, onComplete, ease)
TweenFacade.Kill(handle)
```

| Tween Sınıfı | Backend |
|--------------|---------|
| `CoroutineFloatTween`, `CoroutineAnchorXTween` | StartCoroutine |
| `DotweenFloatTween`, `DotweenAnchorXTween` | DOTween (define ile aktif) |

### Animation servisleri ve controller'lar
| Sınıf | Görev |
|-------|-------|
| `UIAnimationService` | Generic UI tween yardımcıları |
| `RouletteAnimationController` | Case Battle roulette (player + opponent) |
| `CaseBattleAnimation` | Battle pulse/round event'leri |
| `CaseBattleRouletteAnimator` | Battle özel reel animasyonu |
| `UpgradeSpinAnimator` | Upgrade ekranı dairesel ibreli spin |
| `UltraRevealEffect` | Ultra rarity flash efekti |

---

## 10. UpgradeScreen Detayı

**Dosya:** `Scripts/UI/Screens/UpgradeScreen.cs` (~977 satır)
**Sorumluluklar (TEK DOSYADA TOPLANMIŞ):**

1. **State management:** seçili input/target, filtre durumları, tab durumu
2. **Grid yönetimi:** card pool + render + click handling
3. **Filter UI:** silah dropdown + nadirlik dropdown + VP delta filtre butonları
4. **Tab sistemi:** ENVANTER ↔ TÜM SKİNLER
5. **Spin animator wiring:** `UpgradeSpinAnimator` lifecycle
6. **Upgrade flow coroutine:** spin → flash → state update → popup
7. **Wallet/event subscriptions:** GameEvents
8. **Drag-scroll passthrough:** child Button → parent ScrollRect

### Serialized Fields (40+ adet)
- Navigation: `navigator, backButton, walletLabel, cardPrefab`
- Sol panel: `inputIcon, inputName, inputRarityLabel, inputVpLabel, inputChanceLabel, inputRarityStrip, inputPlaceholder`
- Sağ panel: `targetIcon, targetName, targetRarityLabel, targetVpLabel, targetChanceLabel, targetRarityStrip, targetPlaceholder`
- Center: `spinCenter, chanceLabel, chanceHint, upgradeButton, upgradeButtonLabel`
- VP filtre butonları: `vpBtn1000, vpBtn2000, vpBtn3000`
- Bottom: `inventoryTabBtn, allSkinsTabBtn, inventoryTabLine, allSkinsTabLine, skinGridRoot, skinScrollRt, filterBar`
- Result overlay: `resultFlash, resultLabel`

### Public/Public-ish metod yüzeyi
Tamamı `private`. Dış API yok — `OnUpgradeClicked()` button event'i ile çağrılıyor.

### Coroutine'ler
| Coroutine | Görev |
|-----------|-------|
| `UpgradeSequence(ctx, input, target)` | Ana akış (4.5s spin + flash + popup) |
| `PlayResultFlash(success)` | Yeşil/kırmızı fullscreen tint |
| `PulseButton()` | Upgrade butonu nefes alma scale |
| `InitSpinAnimator()` | Bir frame bekleyip animator init |
| `ScaleRoutine(t, to, dur)` | Card hover scale tween |
| `AnimateDd(popup, from, to, hide)` | Dropdown açma/kapama scale Y |

### Inner classes
- `UpgradeCard` — pool item (Root, View, Button, Skin, BaseScale)
- `ScrollRectPassthrough : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler` — kart üstü drag-to-scroll

### Upgrade Success Flow (mevcut akış)
```
1. UpgradeButton.onClick → OnUpgradeClicked()
2. _isUpgrading = true; RefreshChance()
3. UpgradeSequence başlat
   a. _spinAnimator.SetChance(chance)
   b. ctx.Upgrade.TryUpgrade(input, target, out success)
      └─ FAIL: _isUpgrading=false, toast, yield break (paneller AYNI kalır)
   c. yield _spinAnimator.AnimateSpin(chance, success, null)  // 4.5s
   d. SoundManager.Play(success ? CaseReveal : UiBack)
   e. yield PlayResultFlash(success)                           // ~1.4s
   f. if success: _selectedInput=target, _selectedTarget=null, paneller güncellenir
      else:       _selectedInput=_selectedTarget=null, paneller temizlenir
   g. _isUpgrading=false; _spinAnimator.ResetNeedle(); RebuildGrid; RefreshChance
   h. if success: SkinWinPopup.EnsureExists().Show(target, null)
      else:       Toast "BASARISIZ — {input.SkinName} kaybedildi"
```

---

## 11. CaseOpeningScreen Detayı

**Dosya:** `Scripts/UI/Screens/CaseOpeningScreen.cs` (~577 satır)

### Serialized Fields
- Navigation: `navigator, backButton, walletLabel`
- Case Selector: `caseListRoot, caseItemPrefab`
- Display Panel: `caseDisplayPanel, caseIconDisplay, caseThemeBg, selectedCaseLabel, priceLabel, openButton`
- Drop List: `dropListRoot, dropItemPrefab`
- Spin: `spinOverlay, flow (CaseOpeningFlowController), skipButton`

### Static
- `PendingCaseId` — ShopScreen'den önce-seçim için

### Coroutine'ler
| Coroutine | Görev |
|-----------|-------|
| `RevealCoroutine(skin)` | 5-phase reveal (delay → hide spin → populate → glow burst → pop anim → popup) |
| `PanelPopAnimation()` | 0.55 → 1.10 → 1.0 scale pop |
| `GlowBurst(skin)` | Icon rarity → white tint fade |
| `SpinEndWatchdog()` | Force-complete fallback (event kaçırılırsa) |
| `SpinPulse()` | Spin overlay nefes alma rengi |

### Case Reward Flow (mevcut akış)
```
1. SelectCase → BuildDropList + RefreshOpenButton
2. OpenSelected → flow.StartOpening(caseDef)
3. CaseOpeningFlowController.StartOpening
   ├─ ctx.CaseOpening.TryBeginOpen → rolled + vpSpent
   └─ spinController.BeginSpin(case, rolled, OnSpinFinished)
4. CaseSpinController.BeginSpin
   ├─ CaseReelBuilder.BuildReelStrip(predetermined winner)
   ├─ Reel item'ları yerleştir + winnerIndex hesapla
   ├─ TweenFacade.MoveAnchorX (5.5s OutQuint)
   └─ BounceToTarget coroutine → OnSpinComplete
5. OnSpinComplete → _onComplete(winner) callback
6. flow.OnSpinFinished
   ├─ ctx.CaseOpening.CompleteOpen → AddSkin + Statistics
   └─ Save
7. GameEvents.OnCaseOpened → CaseOpeningScreen.OnCaseOpened
8. StartRevealSequence(skin) — re-entry guard via _showingResult
9. RevealCoroutine
   ├─ 0.45s delay
   ├─ ShowSpinOverlay(false) + EnsureInteractive
   ├─ PopulateResultPanel(skin) + GlowBurst (paralel)
   ├─ yield PanelPopAnimation (0.36s)
   ├─ 0.25s beat
   └─ SkinWinPopup.EnsureExists().Show(skin, () => navigator.Navigate(MainMenu))
       └─ Fallback: ConvertToTamam() (popup null ise)
```

### Watchdog mekanizması
Eğer `OnCaseOpened` event ateşlenmezse `SpinEndWatchdog` (10s timeout) `flow.TryForceComplete()` çağırarak save'i zorla yapar ve `StartRevealSequence` tetikler.
Ek olarak `Update()` içinde `flow.SessionActive=false && spinOverlay.activeSelf` koşulu fast-fail olarak çalışır.

---

## 12. SkinWinPopup Sistemi

**Dosya:** `Scripts/UI/SkinWinPopup.cs`
**Pattern:** Singleton + Lazy Runtime Build

### İki kaynak yolu
```
1. Builder → PF_UICanvas → SafeArea/SkinWinPopup
   └─ Awake → Instance set
2. SkinWinPopup.EnsureExists() (runtime fallback)
   └─ Instance null ise RuntimeBuildPopup()
      ├─ SafeArea bul (yoksa highest sortingOrder Canvas)
      ├─ Tüm hiyerarşiyi koddan oluştur
      ├─ Tüm [SerializeField] field'ları manuel wire
      └─ confirmButton listener'ı manuel attach (Awake'te null'du)
```

### Serialized fields (15 adet)
- `canvasGroup, overlay, card, rarityBg, diagonalGlow1, diagonalGlow2, topAccentLine`
- `skinNameLabel, vpLabel, skinIconImage, rarityBadgeBg, rarityLabel, categoryLabel`
- `confirmButton, confirmButtonBg`

### Public API
```csharp
static SkinWinPopup EnsureExists()           // her zaman bunu kullan
void Show(SkinDefinitionSO, Action onConfirm)
void Hide()
```

### Rarity Theme Uygulaması
Tek `ResolveRarityColor(skin)` çağrısı ile şunların hepsi aynı renge çekilir:
- `rarityBg` (alpha 0.40)
- `diagonalGlow1/2` (alpha 0.14 / 0.07)
- `topAccentLine` (alpha 1)
- `rarityBadgeBg` (alpha 0.75)
- `confirmButtonBg` (0.55× shade)
- `rarityLabel.color` (alpha 1)

### Coroutine
- `PopInAnimation()` — scale 0.60 → 1.08 (ease-out cubic, 0.26s) → 1.0 (settle, 0.12s), paralelde overlay alpha fade

### Rarity color fallback palette (RarityVisualSO yoksa)
| Rarity | Renk (RGB) |
|--------|------------|
| Select | (0.50, 0.62, 0.76) |
| Deluxe | (0.00, 0.58, 1.00) |
| Premium | (0.65, 0.13, 0.98) |
| Exclusive | (0.86, 0.16, 0.26) |
| Ultra | (1.00, 0.63, 0.00) |

---

## 13. Pooling Sistemi

**Dosya:** `Scripts/Managers/PoolManager.cs`
**Singleton.** Iki pool:
- `ReelItemView` pool — case spin reel item'ları (prewarm 16)
- `SkinCardView` pool — inventory grid kartları (prewarm 24)

API: `GetReelItem` / `ReleaseReelItem`, `GetSkinCard` / `ReleaseSkinCard`, `ReleaseAll(IEnumerable<T>)`

---

## 14. GameEvents (Static Pub/Sub Hub)

**Dosya:** `Scripts/Core/GameEvents.cs`

| Event | Tetikleyici | Dinleyiciler |
|-------|-------------|--------------|
| `OnVpChanged(int prev, int cur)` | VpCurrencyService.Set/Add/Spend | UpgradeScreen, CaseOpeningScreen, ShopScreen, EarnVpScreen, vd. |
| `OnSkinObtained(skin)` | InventoryService.AddSkin | Statistics rebuild |
| `OnSkinSold(skin, vp)` | InventoryService.TrySell | Toast, statistics |
| `OnCaseOpened(case, skin)` | CaseOpeningService.CompleteOpen | CaseOpeningScreen.RevealCoroutine |
| `OnInventoryChanged` | Inventory.Add/Sell/ConsumeOne | UpgradeScreen, InventoryScreen, WeaponsScreen |
| `OnStatisticsChanged` | Stats record/recalc | MainMenuScreen.Refresh |
| `OnDailyRewardClaimed` | DailyRewardService.TryClaim | UI refresh |
| `OnShopRotated` | ShopService.RotateShop | ShopScreen |
| `OnToastRequested(msg)` | `GameEvents.RaiseToast` | ToastView |

`ClearAll()` testler için.

---

## 15. Singleton Envanteri (Tam Liste)

| Singleton | Lifetime | Görev |
|-----------|----------|-------|
| `GameContext.Instance` | DontDestroyOnLoad | Tüm servisler |
| `SkinWinPopup.Instance` | Scene-scoped (lazy) | Loot reveal popup |
| `PoolManager.Instance` | Scene-scoped | Reel + Card pool |
| `SoundManager.Instance` | Scene-scoped | Audio |
| `HapticManager.Instance` | Scene-scoped | Mobile haptics |
| `ProfileManager` (static) | Process | Avatar/isim |

---

## 16. Dependency Haritası (En Önemli Bağlantılar)

```
GameContext  ──→ ContentDatabaseSO, GameConfigSO, RarityVisualSO
             ──→ GameServicesFactory ──→ tüm 10 servis

UpgradeScreen ──→ GameContext.Instance.Upgrade
              ──→ GameContext.Instance.Inventory
              ──→ GameContext.Instance.Vp
              ──→ UpgradeSpinAnimator (AddComponent)
              ──→ SkinWinPopup.EnsureExists()
              ──→ WeaponSkinCardView (prefab)
              ──→ SoundManager, GameEvents

CaseOpeningScreen ──→ GameContext.Instance.CaseOpening
                  ──→ CaseOpeningFlowController (serialized)
                  ──→ SkinWinPopup.EnsureExists()
                  ──→ CaseListItemView, DropItemView

CaseOpeningFlowController ──→ CaseSpinController (serialized)
                          ──→ GameContext.Instance.CaseOpening

CaseSpinController ──→ PoolManager.Instance
                   ──→ TweenFacade
                   ──→ CaseReelBuilder (static)
                   ──→ SoundManager, HapticManager, UltraRevealEffect

CompositionRoot ──→ GameContext.Instance
                ──→ CaseBattleScreen, CaseBattleAnimation, RouletteAnimationController

ValoCaseUIBuilder (Editor) ──→ Tüm prefab tipleri
                            ──→ Tüm Screen component'leri
                            ──→ SkinWinPopup (sahnedeki obje)
```

---

## 17. Coroutine Envanteri (Önemli Olanlar)

| Sınıf | Coroutine | Süre |
|-------|-----------|------|
| GameBootstrap | `Start()` (IEnumerator) | minimumLoadSeconds |
| UpgradeScreen | UpgradeSequence | ~6s |
| UpgradeScreen | PlayResultFlash | ~1.4s |
| UpgradeScreen | PulseButton | sonsuz |
| UpgradeScreen | InitSpinAnimator | 1 frame |
| UpgradeScreen | AnimateDd | 0.14s |
| UpgradeSpinAnimator | AnimateSpin | 4.5s |
| UpgradeSpinAnimator | FlashResultColor | ~1.1s |
| CaseOpeningScreen | RevealCoroutine | ~1.1s + popup |
| CaseOpeningScreen | PanelPopAnimation | 0.36s |
| CaseOpeningScreen | GlowBurst | 0.4s |
| CaseOpeningScreen | SpinEndWatchdog | ≤10s+spin |
| CaseOpeningScreen | SpinPulse | sonsuz (spin sırasında) |
| CaseSpinController | HighlightCenterItem | spin boyunca |
| CaseSpinController | BounceToTarget | postBounceDuration (0.35s) |
| SkinWinPopup | PopInAnimation | 0.38s |
| UINavigator (UIScreenBase) | Fade | fadeDuration (0.25s) |

---

## 18. Mevcut Davranışları Listesi (Refactor öncesi snapshot)

### A. Bootstrap
- Bootstrap sahnesi `GameBootstrap` + `LoadingScreen` + `GameContext` içerir
- Minimum 1.2s loading bar → Main sahnesine geç

### B. Main Menu
- Profil, online sayısı, stats summary, progression bar, daily button
- 8 ekran butonu (CaseOpening/Inventory/Shop/Settings/Weapons/Upgrade/CaseBattle/EarnVp)

### C. Case Opening
- ShopScreen'den `PendingCaseId` ile pre-select
- OPEN butonu wallet/unlock kontrolünden geçer
- Spin overlay başlar → reel CS:GO style overshoot+bounce
- Bitince result panel pop (case icon → skin icon swap + rarity wash)
- `SkinWinPopup` overlay → CONFIRM → MainMenu

### D. Upgrade
- ENVANTER tab: sahip olunan skinler (rarity 5→1 sort)
- TÜM SKİNLER tab: input seçildiğinde eligible+rest birleşik liste
- Filter bar her iki tab'de görünür (silah + nadirlik dropdown'ları)
- VP delta butonları (+1000/+2000/+3000) — ±500 bracket
- UpgradeButton: pulse animasyon + rarity Pink renkli
- Spin → flash → popup akışı
- Drag-to-scroll: kart üstünden ScrollRect'e geçiyor

### E. Popup
- `SkinWinPopup.EnsureExists()` her çağrıda Instance varsa onu, yoksa runtime'da inşa eder
- Full-screen dark overlay (0.88 alpha)
- Card 600×540, rarity-tinted background + diagonal glow
- Confirm → Hide + onConfirm callback

### F. Navigation
- `UINavigator` tek aktif ekran (CanvasGroup fade)
- Back butonları → `navigator.Navigate(ScreenType.MainMenu)`

### G. Animator Bağlantıları
- `UpgradeSpinAnimator` UpgradeScreen.Awake'te `gameObject.AddComponent` ile eklenir
- `UpgradeSpinAnimator.Initialize(spinCenter, chanceLabel)` 1 frame sonra çağrılır
- Procedural ring sprite + procedural circle dot (artık `UI/Skin/Knob.psd` bağımlılığı yok)

### H. Persistence
- `JsonSaveRepository` → `Application.persistentDataPath/valocase_save.json`
- Tetikleyiciler: pause, quit, upgrade success, case complete

---

## 19. Prefab Bağlantı Tablosu

| Prefab | Sahnede instantiate eden | Wired field |
|--------|--------------------------|-------------|
| PF_UICanvas | SetupCurrentScene (editor) | Sahneye prefab instance |
| PF_GameContext | SetupCurrentScene | Sahneye prefab instance |
| PF_SkinCard | InventoryScreen, PoolManager | `cardPrefab` (Inventory) |
| PF_WeaponSkinCard | WeaponsScreen, UpgradeScreen | `cardPrefab` |
| PF_ReelItem | PoolManager → CaseSpinController | Pool |
| PF_CaseListItem | CaseOpeningScreen, ShopScreen | `caseItemPrefab` |
| PF_DropItem | CaseOpeningScreen | `dropItemPrefab` |

---

## 20. Bilinen Eski Davranış Notları (Refactor için uyarılar)

1. **UpgradeScreen tek dosyada 977 satır** — UI, state, animator, dropdown popup'ları, drag passthrough hepsi bir arada
2. **ValoCaseUIBuilder.cs ~2350 satır** — tek static class, tüm builder mantığı içeride
3. **UpgradeSpinAnimator UpgradeScreen.Awake'te AddComponent ile eklenir** — prefab'da serialized değil
4. **SkinWinPopup hem builder hem runtime fallback üretir** — iki kaynak yolu, tek tip Instance
5. **CompositionRoot şu anda yalnızca CaseBattle wire eder** — diğer sistemler GameContext.Instance ile doğrudan çekiyor
6. **GameEvents static event'ler** — `ClearAll()` çağrılmazsa scene reload'da ghost subscription riski
7. **VandalCaseBuilder runtime'da çalışır** — tüm SO-tabanlı case'ler bypass edilir
8. **FileSystemSkinLoader** — `Desktop/ValorantProject/ValoSkinss` varsayılan yolu kullanır
9. **`OnCaseOpened` event'i hem watchdog hem `Update` hem normal callback'ten ateşlenebilir** — `_showingResult` re-entry guard ile korunuyor
10. **`gameObject.SetActive(false)` yerine `CanvasGroup.alpha=0` tercih ediliyor** — `UIScreenBase.Fade` bunu yapıyor

---

## 21. Refactor Yaparken Korunması Gereken Davranışlar (Test Listesi)

- [ ] Bootstrap → Main geçiş, GameContext init'in tamamlanması
- [ ] MainMenu → her ekran navigation çalışması
- [ ] Case Opening: open → spin (5.5s) → reveal → popup → MainMenu
- [ ] Case Opening: skip butonu çalışması, watchdog fallback (event drop edildiğinde)
- [ ] Upgrade: ENVANTER seçim → TÜM SKİNLER tab'a otomatik geçiş
- [ ] Upgrade: success → input target'a dönüşür, panel kalır
- [ ] Upgrade: fail → input/target sıfırlanır, toast
- [ ] Upgrade: popup confirm → upgrade ekranında kal (navigation yok)
- [ ] VP delta filtreler: aynı butona basınca toggle off, otomatik TÜM SKİNLER
- [ ] Drag-scroll: kart üstünden parmak sürüklemesi grid'i hareket ettirir
- [ ] DailyRewardPopup açılması ve claim
- [ ] Shop rotation 24 saat sonra yenilenmesi (timestamp tabanlı)
- [ ] Save/Load: oyun kapatılıp açıldığında inventory + VP korunur
- [ ] SkinWinPopup: builder çalıştırılmadan (Instance=null) bile çalışır (`EnsureExists` fallback)
- [ ] Audio: case spin loop, reveal sound, upgrade success/fail sound
- [ ] Statistics: case opened count, total VP spent, rarest skin

---

## 22. Dosya Boyut Özeti (Refactor önceliği için)

| Dosya | Satır | Refactor önceliği |
|-------|-------|-------------------|
| `ValoCaseUIBuilder.cs` | ~2350 | YÜKSEK — partial class'lara böl |
| `UpgradeScreen.cs` | ~977 | YÜKSEK — Controller/View/Filter ayır |
| `GameServices.cs` | ~770 | ORTA — her servis ayrı dosya |
| `CaseOpeningScreen.cs` | ~580 | ORTA — Reveal sequence ayrılabilir |
| `SkinWinPopup.cs` | ~420 | DÜŞÜK — Runtime build helper'a çıkarılabilir |
| `CaseSpinController.cs` | ~225 | DÜŞÜK — temiz |
| `CompositionRoot.cs` | ~92 | DÜŞÜK — şablon hazır |
| `GameContext.cs` | ~88 | DÜŞÜK — temiz |
| `GameEvents.cs` | ~42 | DÜŞÜK — temiz |

---

**SON.** Bu belge yalnızca mevcut durumu kayıt altına alır; refactor sırasında karar verilen değişiklikler yeni bir "YeniMimariBlueprint.md" belgesine yazılmalıdır.
