# ValoCase Unity Backend Authority Audit

Date: 2026-06-21

Scope: Unity project only, under `Assets/_ValoCase`. I did not inspect the backend project, so every backend-related statement is based on Unity API calls, request/response models, and comments in the Unity client. Tests were not run. Code and assets were not modified.

Explicit exclusion: the Upgrade screen is intentionally excluded from this audit per request because it is not current. I did not make upgrade-screen findings, except where shared services expose general economy or inventory patterns that affect other screens.

Core standard used for this audit: Unity should send player intent and display backend state. Backend should calculate prices, odds, rewards, affordability, locks, eligibility, winner selection, wallet deltas, inventory deltas, claim availability, and final results.

## 1. Executive summary

### Overall Unity authority risk

The project is partially migrated to a backend-authoritative model, but it still contains a large local-authoritative economy and game-rule stack. Some newer flows correctly use backend results, but many screens still display local prices, odds, unlock state, sell values, reward estimates, wallet cache, and battle/case assumptions. This means the player can see UI that is not backend truth, and some paths can still run local behavior when backend is unavailable or when old local systems are reached.

The biggest architectural issue is not one bug. It is that Unity still has two sources of truth:

- Backend APIs for wallet, inventory, case open results, daily claims, mission claims, earn-VP claims, and online battles.
- Local ScriptableObjects, local JSON balance overrides, local services, local randomizers, local mission rules, local progression maps, and legacy battle engines.

That dual model is fragile. It makes every screen decide whether to trust backend state or local state. Some screens do this well. Others guess.

### Biggest duplicated-rule risks

- Case prices, case availability, skin values, drop weights, and drop odds are loaded and displayed from Unity catalog data in `ContentDatabaseSO`, `CaseDefinitionSO`, `CaseDropTableSO`, and `BalanceOverrideService`.
- Inventory sell value is calculated locally from `skin.VpValue * GameConfigSO.SellMultiplier`.
- Mission definitions, mission progress, mission rewards, and claim logic exist locally in `MissionSystem`.
- Progression and unlock rules are duplicated in `PlayerProgression`, including hardcoded category unlock levels and string-based case-category classification.
- Battle winner logic, battle roll logic, bot/player totals, and battle reward settlement still exist locally in `BattleOpeningEngine`, `LocalBattleService`, `CaseBattleSession`, and `CaseBattleSystem`.
- Earn VP has a full local reward formula in `EarnVpScreen`, even though backend mode later claims the session from the server.

### Biggest misleading-UI risks

- Case opening displays local prices and local drop chances that may not match backend price or backend roll odds.
- Case opening and shop can block or allow actions based on local unlock and affordability guesses before backend confirmation.
- Inventory displays collection value and per-skin sell value from Unity-local values, while backend may sell for different values.
- Sell-below-value asks the player for a local "valued at or below" threshold, but backend owns the real values.
- Tools screen hardcodes daily reward display as `+2,000 VP` while the backend daily status response has `nextRewardVp`.
- Earn VP shows local `+VP` float text and session estimates before backend accepts taps and calculates the actual reward.
- Wallet labels display the local cache. That is acceptable after a successful sync, but unsafe after backend boot or resync failure because the current behavior keeps cached wallet/inventory.

## 2. Confirmed good patterns

### Backend case opening result is used for actual rewards

Files:

- `Assets/_ValoCase/Scripts/CaseOpening/CaseOpeningFlowController.cs`
- `Assets/_ValoCase/Scripts/UI/Screens/CaseOpeningScreen.cs`

Good pattern:

- Backend path calls `Backend.OpenCase(caseId)`.
- The winning skin comes from `OpenCaseResultResponse.wonSkin`.
- Wallet is overwritten from `response.newVpBalance`.
- `CompleteOpenFromBackend` avoids local wallet spend and local duplicate bonus.
- If the backend returns a skin not present in the Unity catalog, the flow cancels the reveal, applies backend wallet, requests resync, and avoids pretending a local result exists.

This is the right direction. The remaining issue is that preview, displayed price, displayed odds, local gating, and local stat spend still come from Unity.

### Backend sell routines apply authoritative wallet and resync inventory

Files:

- `Assets/_ValoCase/Scripts/Core/GameContext.cs`
- `Assets/_ValoCase/Scripts/Services/Backend/BackendApiClient.cs`

Good pattern:

- `SellOneBackend`, `SellAllBackend`, and `SellBelowValueBackend` call backend endpoints.
- On success, Unity applies `newVpBalance`.
- Inventory is re-pulled from backend after sale.
- Ambiguous transport failures trigger backend resync.

This is a strong pattern. The remaining problems are value preview and item identity, especially selling by `skinId` instead of backend `itemId`.

### Backend inventory sync is replacement-based, not merge-based

File:

- `Assets/_ValoCase/Scripts/Core/GameContext.cs`

Good pattern:

- `ApplyInventoryFromBackend` rebuilds local inventory from the backend response instead of merging.
- `BackendInventoryCache` preserves runtime item identity.
- Unknown backend skin IDs are logged and kept rather than silently deleted.

This reduces double-grant and stale-inventory risk.

### Backend daily reward popup uses backend status

File:

- `Assets/_ValoCase/Scripts/UI/DailyRewardPopup.cs`

Good pattern:

- Backend mode fetches `DailyStatusResponse`.
- Reward label uses `nextRewardVp`.
- Claim button uses backend `claimable`.
- Claim response applies authoritative wallet through `ClaimDailyBackend`.
- Status is re-fetched after claim.

This popup follows the desired source-of-truth model better than the Tools screen card.

### Backend mission claim path applies authoritative wallet

Files:

- `Assets/_ValoCase/Scripts/Core/GameContext.cs`
- `Assets/_ValoCase/Scripts/UI/Screens/MissionsScreen.cs`

Good pattern:

- Mission claim calls backend.
- Wallet is overwritten from `newVpBalance`.
- UI refreshes backend mission state.
- Local mission fallback is not used by `MissionsScreen` when backend mode is active.

The remaining issue is that button claimability is partially inferred from progress instead of backend status.

### Online battle lobbies mostly use backend state

Files:

- `Assets/_ValoCase/Scripts/UI/Screens/LobbyListScreen.cs`
- `Assets/_ValoCase/Scripts/UI/Screens/WaitingRoomScreen.cs`
- `Assets/_ValoCase/Scripts/Battle/BattleLobbyMapper.cs`
- `Assets/_ValoCase/Scripts/Battle/BattleService.cs`

Good pattern:

- Public lobbies are fetched from backend.
- Create lobby sends case selection intent, not a trusted final price.
- Waiting room polls backend.
- Completed lobby data is mapped from backend response.
- Backend battle service applies `newVpBalance`.
- Backend battle settlement records local stats and resyncs inventory instead of locally granting skins.

The remaining issue is the confirmed local bot fallback when backend is not ready, plus legacy battle engines still present.

### Release builds are intended to fail closed for local economy

File:

- `Assets/_ValoCase/Scripts/Core/GameContext.cs`

Good pattern:

- `CanUseLocalEconomy` returns true only in `UNITY_EDITOR` or `OFFLINE_DEMO` and only when `UseBackend` is false.
- Release/player builds force `BackendEnabled`.

This is the correct direction. However, some UI still falls back to local/bot behavior when `BackendReady` is false, which weakens this standard.

## 3. Risk findings

### Finding 1: Local-authoritative economy stack still exists beside backend stack

Area/screen:

- Shared economy, inventory, case opening, daily rewards, shop, progression, and local/demo mode.

Files:

- `Assets/_ValoCase/Scripts/Services/Interfaces.cs`
- `Assets/_ValoCase/Scripts/Services/GameServices.cs`
- `Assets/_ValoCase/Scripts/Services/EconomyService.cs`
- `Assets/_ValoCase/Scripts/Services/LocalResultProvider.cs`
- `Assets/_ValoCase/Scripts/Core/GameContext.cs`

What is wrong or risky:

Unity still has services that spend VP, add VP, sell inventory, grant rewards, calculate inventory value, roll cases, calculate duplicate bonuses, claim daily rewards, rotate shop deals, and unlock cases locally.

Why this is amateur/temporary/duplicated:

This is not a thin UI cache. It is a complete second economy implementation. It duplicates the same class of rules the backend should own: wallet mutation, inventory mutation, RNG, sell values, daily rewards, case eligibility, and progression.

How it could cause exploit, desync, or bad UX:

- Any screen that accidentally routes to local services can mutate wallet or inventory without backend validation.
- Editor/demo behavior can diverge from production and hide backend contract problems.
- Local services make it easy for future features to accidentally bypass the backend because convenient APIs like `GrantReward`, `TrySpend`, `TrySell`, and `RollSkin` remain available.

Severity: High.

Recommended fix:

- Keep local economy only behind an explicit offline demo assembly define or separate demo scene.
- In backend-enabled builds, replace local mutation methods with fail-closed adapters that return errors if called.
- Rename local services with an obvious `OfflineDemo` prefix and keep them out of production composition.
- Add runtime assertions when backend mode calls local authoritative methods.

Backend changes required:

- Not strictly required, but backend must provide enough state and preview endpoints so screens do not need local economy services for UI.

### Finding 2: Battle lobby screen falls back to local bot lobbies when backend is not ready

Area/screen:

- Battle lobby list and waiting room.

Files:

- `Assets/_ValoCase/Scripts/UI/Screens/LobbyListScreen.cs`
- `Assets/_ValoCase/Scripts/UI/Screens/WaitingRoomScreen.cs`

What is wrong or risky:

`LobbyAutoRefresh` shows local bot lobbies when `GameContext.BackendReady` is false. The local fallback creates fake lobbies with local prices and opens the local waiting room path.

Why this is amateur/temporary/duplicated:

The code explicitly logs that backend is not ready and that it is showing a "LOCAL bot fallback". That is a temporary demo behavior leaking into the same screen as production online battles.

How it could cause exploit, desync, or bad UX:

- In a backend-required build where boot sync is delayed or failed, the player can see fake local battle opportunities.
- Local lobby costs are derived from Unity case prices, not backend entry cost.
- The player may join or create a non-backend battle that cannot be reconciled with server state.
- Even if later backend calls fail closed, the UI has already misled the player into thinking battles are available.

Severity: Critical if reachable in production builds. High if only reachable in editor/backend-offline testing.

Recommended fix:

- In backend-enabled mode, never show local bot lobbies because `BackendReady` is false.
- Show a loading, reconnecting, or unavailable state.
- Keep local bot lobbies only in explicit offline demo mode.
- Gate the local fallback on `ctx.CanUseLocalEconomy`, not on `!ctx.BackendReady`.

Backend changes required:

- No backend change required for the fail-closed UI.
- Optional: backend could expose a health/status endpoint so Unity can display a precise reconnecting state.

### Finding 3: Legacy local battle engines still decide winners and rewards

Area/screen:

- Battle system, bot battles, old battle simulation.

Files:

- `Assets/_ValoCase/Scripts/Battle/BattleOpeningEngine.cs`
- `Assets/_ValoCase/Scripts/Battle/BattleService.cs`
- `Assets/_ValoCase/Scripts/Data/CaseBattleModel.cs`
- `Assets/_ValoCase/Scripts/Systems/CaseBattleSystem.cs`
- `Assets/_ValoCase/Scripts/UI/Screens/LobbyListScreen.cs`

What is wrong or risky:

Unity contains multiple battle engines that roll skins locally, total local `VpValue`, decide winners locally, and in some paths grant rewards locally. `CaseBattleSystem.TryStartBattle` spends local VP and `Settle` grants local winnings. `LocalBattleService` does the same for local battle mode.

Why this is amateur/temporary/duplicated:

Battle winner selection and reward settlement are core authoritative rules. Duplicating them in Unity is exactly the type of backend-bypassing logic the project should remove or quarantine.

How it could cause exploit, desync, or bad UX:

- If any old path is invoked, Unity can produce a battle result the backend never created.
- Winner and reward results can differ from backend rules.
- Local stats can be polluted by fake battle outcomes.
- Future developers may reuse the local engine because it already "works".

Severity: High.

Recommended fix:

- Move all local battle engines into explicit offline demo code.
- In backend mode, battle screens should only accept backend lobby/result objects.
- Delete or compile out `CaseBattleSystem` and `LocalBattleService` from production composition once backend battle flow is complete.
- Local battle animation should consume backend result data only.

Backend changes required:

- Backend must continue returning full battle result data needed by animation: participants, rounds, skins, values, winner slot/account, entry cost, rewards, inventory deltas, wallet balance, and progression.

### Finding 4: Case opening UI displays local odds, prices, and eligibility

Area/screen:

- Case opening screen.

Files:

- `Assets/_ValoCase/Scripts/UI/Screens/CaseOpeningScreen.cs`
- `Assets/_ValoCase/Scripts/CaseOpening/CaseOpeningFlowController.cs`
- `Assets/_ValoCase/Scripts/Data/CaseDefinitionSO.cs`
- `Assets/_ValoCase/Scripts/Data/CaseDropTableSO.cs`

What is wrong or risky:

The case opening screen displays local `caseDef.VpPrice`, local drop odds from `caseDef.DropTable.RarityWeights`, and per-item chances calculated from local skin weights. The open button is enabled using `ctx.CaseOpening.CanOpen`, local wallet balance, local case price, and local unlock checks before backend confirmation.

Why this is amateur/temporary/duplicated:

The UI is calculating and presenting authoritative economy and probability information from Unity assets. If backend price or odds change, Unity can show false information even though the backend correctly rolls the result.

How it could cause exploit, desync, or bad UX:

- Player sees a price that differs from what backend charges.
- Player sees odds that differ from backend roll odds.
- Player sees an enabled button when backend will reject.
- Player sees a disabled/locked button when backend would allow.
- Multi-open stats use local price as spent value because backend open response does not provide `vpSpent`.

Severity: High.

Recommended fix:

- Add a backend case preview/state endpoint and use it for displayed price, odds, unlock status, affordability, disabled reason, and quantity availability.
- In backend mode, `OpenSelected` should not fail based on local `CanOpen` or local `PlayerProgression.IsCaseUnlocked`; it should request backend preview or attempt backend open and display backend rejection.
- Keep local drop tables only for cosmetic reel fillers or offline demo mode.

Backend changes required:

- Yes. Backend should return authoritative case price, unlock state, affordability, odds display, and rejection reasons.
- `OpenCaseResultResponse` should include `vpSpent` or `priceChargedVp` so Unity does not derive spend from local catalog.

### Finding 5: Local catalog and balance overrides duplicate backend economy data

Area/screen:

- Content loading, case list, shop, case opening, inventory, battle display.

Files:

- `Assets/_ValoCase/Scripts/Data/ContentDatabaseSO.cs`
- `Assets/_ValoCase/Scripts/Data/BalanceOverrideService.cs`
- `Assets/_ValoCase/Scripts/Data/BalanceOverrideModels.cs`
- `Assets/_ValoCase/Scripts/Data/CaseDefinitionSO.cs`
- `Assets/_ValoCase/Scripts/Data/SkinDefinitionSO.cs`

What is wrong or risky:

Unity loads local `skins.json`, `cases.json`, ScriptableObjects, and `balance_overrides.json`. These can change skin VP values, case prices, case enabled flags, skin enabled flags, drop enabled flags, and drop weights at runtime.

Why this is amateur/temporary/duplicated:

This is a parallel balance system. It can override exactly the economy fields the backend should own.

How it could cause exploit, desync, or bad UX:

- Case prices displayed in Unity can differ from backend charges.
- Skin values displayed in inventory can differ from backend sell values.
- Drop odds displayed in Unity can differ from backend odds.
- Unity can show disabled/enabled content differently from backend.
- Backend can return a skin ID not in local catalog, making Unity unable to render the reward cleanly.

Severity: High.

Recommended fix:

- Treat local catalog as presentation metadata only: names, images, colors, layout grouping.
- Move price, sell value, enabled state, drop odds, unlock state, and availability into backend-provided catalog/state responses.
- Add catalog versioning. Unity should display the backend catalog version and know when local presentation data is stale.

Backend changes required:

- Yes. Backend needs a catalog/state endpoint or enriched responses that include authoritative economy values and version identifiers.

### Finding 6: Inventory sell previews and collection values are calculated locally

Area/screen:

- Inventory screen, skin cards, skin details, reward review sell.

Files:

- `Assets/_ValoCase/Scripts/UI/Screens/InventoryScreen.cs`
- `Assets/_ValoCase/Scripts/UI/SkinCardView.cs`
- `Assets/_ValoCase/Scripts/UI/SkinDetailPopup.cs`
- `Assets/_ValoCase/Scripts/UI/InventorySellFlow.cs`
- `Assets/_ValoCase/Scripts/UI/Screens/CaseOpeningScreen.cs`
- `Assets/_ValoCase/Scripts/Services/GameServices.cs`

What is wrong or risky:

Inventory displays collection value from local `InventoryValue`. Skin cards display sell value using local `skin.VpValue * ctx.Config.SellMultiplier`. Sell-below-value asks the user for a threshold against local displayed values. Reward review totals sum local `skin.VpValue`.

Why this is amateur/temporary/duplicated:

Sell value is a backend economy rule. Unity is showing and sorting by local values that backend may not use.

How it could cause exploit, desync, or bad UX:

- Player expects a sell payout based on Unity value, but backend returns a different gain.
- Sell-below-value may sell a different set of items than the player expected if backend values differ.
- Collection value can be wrong after backend balance changes.
- Reward review "total value" can be mistaken for backend sell value.

Severity: High.

Recommended fix:

- Backend inventory response should include authoritative display value and sell value per item.
- Backend should provide sell preview endpoints for sell one, sell all, and sell below value.
- UI should label unconfirmed estimates clearly until backend preview/result returns.

Backend changes required:

- Yes. Add item-level value/sell fields or an inventory valuation endpoint.
- Add sell preview endpoint(s), especially for bulk sell and sell-below-value.

### Finding 7: Sell-one uses `skinId` instead of backend `itemId`

Area/screen:

- Inventory sell, skin detail popup, reward review sell.

Files:

- `Assets/_ValoCase/Scripts/Core/GameContext.cs`
- `Assets/_ValoCase/Scripts/Services/Backend/BackendApiClient.cs`
- `Assets/_ValoCase/Scripts/UI/SkinDetailPopup.cs`
- `Assets/_ValoCase/Scripts/UI/Screens/CaseOpeningScreen.cs`

What is wrong or risky:

`SellOneRequest` sends `skinId`. Unity already preserves backend item identity in `BackendInventoryCache`, but the sell-one endpoint call does not use it.

Why this is amateur/temporary/duplicated:

Selling by skin type is weaker than selling by owned item instance. It is a shortcut that hides item identity and makes future item history, upgrades, trades, and duplicate-specific UI harder.

How it could cause exploit, desync, or bad UX:

- If the player owns multiple copies of the same skin, backend may sell a different instance than the one the UI marked sold.
- Reward review can mark the newly won card sold while backend sells an older matching item.
- Item history, acquired time, lock flags, or future item modifiers cannot be respected.

Severity: High.

Recommended fix:

- Change sell-one intent to use `inventoryItemId`.
- Reward review should bind each won card to `inventoryItemId` from the open result.
- Inventory UI should select exact backend item IDs from `BackendInventoryCache`.

Backend changes required:

- Yes, unless backend already supports itemId sell and Unity is not using it.

### Finding 8: Mission rules still exist locally and can progress in the background

Area/screen:

- Missions, daily/earn progression hooks, event system.

Files:

- `Assets/_ValoCase/Scripts/Systems/MissionSystem.cs`
- `Assets/_ValoCase/Scripts/UI/Screens/MissionsScreen.cs`
- `Assets/_ValoCase/Scripts/UI/Screens/ToolsScreen.cs`
- `Assets/_ValoCase/Scripts/CompositionRoot.cs`

What is wrong or risky:

`MissionSystem` defines local missions, local progress rules, local rewards, local claimability, and event subscriptions. It listens to Unity events such as case opened, VP changed, skin obtained, and battle completed.

Why this is amateur/temporary/duplicated:

Mission progress and claim availability are authoritative backend rules. Keeping a local mission engine alive beside backend missions creates another source of truth.

How it could cause exploit, desync, or bad UX:

- Local mission progress can change from cached/local events even though backend mission state is different.
- Any UI fallback or future notification using local mission data can show false claimability.
- Local mission definitions can drift from backend definitions.

Severity: Medium now because `MissionsScreen` uses backend state in backend mode. High if any production UI, notification, or future feature reads local `MissionSystem`.

Recommended fix:

- In backend mode, do not instantiate or subscribe `MissionSystem`.
- Make backend mission response the only source for progress, target, reward, status, and reset time.
- Keep local mission engine only in explicit offline demo mode.

Backend changes required:

- Backend should return complete mission card data so Unity does not need local mission definitions for display.

### Finding 9: Mission claim UI infers claimability from progress instead of backend status

Area/screen:

- Missions screen.

File:

- `Assets/_ValoCase/Scripts/UI/Screens/MissionsScreen.cs`

What is wrong or risky:

Backend mode checks `progress >= target` and `status != "CLAIMED"` in places such as notification state and card button state. It does not strictly require backend `status == "CLAIMABLE"`.

Why this is amateur/temporary/duplicated:

The UI is recreating a backend eligibility rule from raw progress fields. That bypasses any backend-only status such as locked, cooldown, expired, anti-abuse hold, season mismatch, or prerequisite not met.

How it could cause exploit, desync, or bad UX:

- Button can look claimable when backend rejects.
- Notification badge can show a reward that is not actually claimable.
- Player loses trust in mission UI after backend rejection.

Severity: Medium.

Recommended fix:

- In backend mode, claim button and notification should use backend `status == "CLAIMABLE"` or explicit `claimable == true`.
- Treat progress fields as display only.

Backend changes required:

- If not already present, add a boolean `claimable` and a machine-readable `statusReason`.

### Finding 10: Progression and case unlocks are duplicated locally

Area/screen:

- Shop, case opening, battle create, case list.

Files:

- `Assets/_ValoCase/Scripts/Progression/PlayerProgression.cs`
- `Assets/_ValoCase/Scripts/UI/Screens/ShopScreen.cs`
- `Assets/_ValoCase/Scripts/UI/Screens/CaseOpeningScreen.cs`
- `Assets/_ValoCase/Scripts/UI/Screens/CreateBattleScreen.cs`
- `Assets/_ValoCase/Scripts/UI/CaseListItemView.cs`

What is wrong or risky:

Unity has hardcoded unlock levels by category and a string classifier that maps case IDs to categories. Unknown cases default to Classic in one path. Some screens use `IsCaseUnlocked`; battle create uses the stricter `IsCaseUnlockedAuthoritative`.

Why this is amateur/temporary/duplicated:

Unlock state is a game rule. It should not be inferred by string matching case IDs or hardcoded category levels in the client.

How it could cause exploit, desync, or bad UX:

- Unity can show a case as unlocked while backend rejects it.
- Unity can hide or block a case backend would allow.
- New backend categories or renamed case IDs can be misclassified.
- Unknown case IDs can become unlocked by defaulting to Classic.

Severity: High.

Recommended fix:

- Backend should return per-case access state: unlocked, required level, locked reason, and category.
- Unity should use local category only for visual grouping, not eligibility.
- In backend mode, unknown case category should fail closed for action buttons.

Backend changes required:

- Yes. Add per-case unlock/access state to catalog or preview responses.

### Finding 11: Wallet and inventory cache can remain stale after backend failures

Area/screen:

- Global wallet displays, inventory, case opening, lobby balance, top bar.

Files:

- `Assets/_ValoCase/Scripts/Core/GameContext.cs`
- `Assets/_ValoCase/Scripts/UI/TopProfileBar.cs`
- `Assets/_ValoCase/Scripts/UI/VpCounterView.cs`
- `Assets/_ValoCase/Scripts/UI/Screens/CaseOpeningScreen.cs`
- `Assets/_ValoCase/Scripts/UI/Screens/InventoryScreen.cs`
- `Assets/_ValoCase/Scripts/UI/Screens/LobbyListScreen.cs`

What is wrong or risky:

Backend boot sync and resync failure logs say Unity is "keeping cached balance" or "keeping cached inventory". Wallet labels and screens then continue showing the local cache.

Why this is amateur/temporary/duplicated:

In backend mode, stale cache should not look like confirmed truth. The UI needs an explicit stale/unverified state.

How it could cause exploit, desync, or bad UX:

- Player sees a wallet amount that backend no longer agrees with.
- Buttons can enable based on stale local balance.
- Inventory can show items already sold or omit items already granted.
- Case opening and battle actions can appear available but backend rejects.

Severity: High.

Recommended fix:

- Track backend sync state: fresh, syncing, stale, failed.
- In backend mode, disable economy actions when wallet/inventory state is stale unless a backend preview/action call can authoritatively validate.
- Show "syncing" or "connection required" rather than cached values as truth.

Backend changes required:

- No mandatory backend change, but lightweight wallet/inventory version fields would help detect staleness.

### Finding 12: Earn VP displays local reward estimates before backend validation

Area/screen:

- Earn VP screen.

File:

- `Assets/_ValoCase/Scripts/UI/Screens/EarnVpScreen.cs`

What is wrong or risky:

Earn VP has local constants for base reward, multiplier, crit chance, decay, and milestones. Backend mode does not immediately mutate wallet, which is good, but the UI still shows `+VP` float text and a "SESSION VP" estimate based on local calculations before backend accepts taps and grants actual VP.

Why this is amateur/temporary/duplicated:

The screen contains a local reward formula for a backend economy action. Even when used as an estimate, it looks like earned VP.

How it could cause exploit, desync, or bad UX:

- Player sees VP amounts that backend later reduces or rejects.
- Crit/multiplier feedback may imply backend uses the same randomness or formula.
- If backend treats tap count/duration/offsets too trustingly, the request itself is an exploit surface.

Severity: Medium for UI. High if backend does not fully validate sessions.

Recommended fix:

- In backend mode, label pending VP as unconfirmed or avoid numeric reward floats until backend response.
- Backend should return accepted tap count, rejected tap count, granted VP, and reason fields.
- Unity should render backend-confirmed grants separately from local tap feedback.

Backend changes required:

- Backend must validate tap cadence and cap rewards. Unity request values must be treated as untrusted intent/telemetry.

### Finding 13: Tools screen hardcodes daily reward display

Area/screen:

- Tools screen daily reward card.

File:

- `Assets/_ValoCase/Scripts/UI/Screens/ToolsScreen.cs`

What is wrong or risky:

`ToolsScreen` has `DailyRewardVp = 2000` and displays `+2,000 VP` while backend status has `nextRewardVp`.

Why this is amateur/temporary/duplicated:

This is a hardcoded reward value in UI for a backend-owned economy reward.

How it could cause exploit, desync, or bad UX:

- Player sees a reward amount that does not match backend claim result.
- Backend balance changes require Unity code changes to avoid false UI.

Severity: Medium.

Recommended fix:

- Use `DailyStatusResponse.nextRewardVp` for display.
- While loading or failed, show unknown/unavailable instead of a hardcoded amount.

Backend changes required:

- No. Unity already receives `nextRewardVp` in the popup path.

### Finding 14: Shop displays local prices and local locks

Area/screen:

- Shop screen.

File:

- `Assets/_ValoCase/Scripts/UI/Screens/ShopScreen.cs`

What is wrong or risky:

Shop cards are sorted by local `VpPrice`, display local `VpPrice`, and click-gate using local `PlayerProgression.IsCaseUnlocked`.

Why this is amateur/temporary/duplicated:

Shop is presenting backend-owned product availability and price from Unity assets.

How it could cause exploit, desync, or bad UX:

- Price shown in shop can differ from backend open/battle price.
- Lock overlay can be wrong.
- Player can click through based on local state and later be rejected.

Severity: Medium.

Recommended fix:

- Shop should render backend case catalog/state: price, enabled, locked, required level, and display order.
- Local assets should only provide image fallback and layout grouping.

Backend changes required:

- Yes, if no backend catalog/case-state endpoint currently exists.

### Finding 15: Battle result mapping can choose winner by display name before stable slot identity

Area/screen:

- Completed battle display.

File:

- `Assets/_ValoCase/Scripts/Battle/BattleLobbyMapper.cs`

What is wrong or risky:

`ResolveWinnerSlotIndex` uses `winnerDisplayName` first, then `winnerSlotIndex` as fallback. Display names are not stable identifiers.

Why this is amateur/temporary/duplicated:

Winner identity should use backend-provided stable slot/account identity. Display text should never decide result mapping.

How it could cause exploit, desync, or bad UX:

- Duplicate or changed display names can highlight the wrong winner.
- Player may see a different winner than backend intended.

Severity: Medium.

Recommended fix:

- Prefer `winnerSlotIndex` or winner account ID.
- Use display name only for text.

Backend changes required:

- Backend should always return stable `winnerSlotIndex` and ideally `winnerAccountId`.

### Finding 16: Case and battle fallback-to-first-case behavior can hide backend/catalog mismatches

Area/screen:

- Backend battle service and battle lobby mapper.

Files:

- `Assets/_ValoCase/Scripts/Battle/BattleOpeningEngine.cs`
- `Assets/_ValoCase/Scripts/Battle/BattleService.cs`
- `Assets/_ValoCase/Scripts/Battle/BattleLobbyMapper.cs`

What is wrong or risky:

When a case cannot be resolved, local code can fall back to `vandal_basic` or the first case. Backend battle service uses `BattleOpeningEngine.ResolveCase` before sending bot battle intent.

Why this is amateur/temporary/duplicated:

Fallback-to-first-case is a demo convenience. In backend mode it can silently convert an invalid or stale case into a different case.

How it could cause exploit, desync, or bad UX:

- Backend receives a different case ID than the UI intended.
- Completed lobby display can show the wrong case art/name.
- Catalog mismatch is hidden instead of surfaced.

Severity: Medium.

Recommended fix:

- In backend mode, fail closed when a case ID cannot be resolved.
- Show "case unavailable, refresh required" and resync catalog/state.
- Keep fallback only for offline demo filler visuals.

Backend changes required:

- Backend should include enough case display metadata or catalog version so Unity can identify mismatch.

### Finding 17: Reward review sells optimistically and by skin type

Area/screen:

- Case opening reward review.

File:

- `Assets/_ValoCase/Scripts/UI/Screens/CaseOpeningScreen.cs`

What is wrong or risky:

Sell-review and sell-all-review mark cards sold/in-flight before backend response and call `SellOneBackend(card.skin.SkinId)` for each card.

Why this is amateur/temporary/duplicated:

The UI treats a local review card as if it were a backend inventory item, but it only knows skin type, not item identity.

How it could cause exploit, desync, or bad UX:

- Wrong duplicate instance can be sold.
- A failed sale causes card state to revert after already showing sold feedback.
- Bulk sell review can partially succeed and leave player confused unless every server result is clearly reconciled.

Severity: Medium.

Recommended fix:

- Bind review cards to backend `inventoryItemId`.
- Display backend sell preview/result per item.
- Keep optimistic UI minimal, or show "selling" until backend confirms.

Backend changes required:

- Yes. Open result should include item ID and sell preview/value for that item, and sell endpoint should accept item ID.

### Finding 18: Cosmetic case reels and battle fillers use local random pools that may imply odds

Area/screen:

- Case spin reel, battle roulette.

Files:

- `Assets/_ValoCase/Scripts/CaseOpening/CaseSpinController.cs`
- `Assets/_ValoCase/Scripts/CaseOpening/CaseReelBuilder.cs`
- `Assets/_ValoCase/Scripts/Battle/BattleRouletteView.cs`

What is wrong or risky:

Filler items are local and cosmetic, which is acceptable if they are clearly not the result. However, local filler weighting can visually imply odds that differ from backend odds.

Why this is amateur/temporary/duplicated:

Players infer probability from reels. If filler distribution is not backend-driven or deliberately neutral, it becomes a misleading odds display.

How it could cause exploit, desync, or bad UX:

- Player thinks near-misses or visible reel frequency indicate actual backend probabilities.
- Backend odds changes do not affect visual filler distribution.

Severity: Low to Medium depending on presentation.

Recommended fix:

- Keep reel filler explicitly cosmetic and neutral.
- If the reel is meant to communicate odds, build filler from backend-provided odds/display data.

Backend changes required:

- Optional. Backend could return odds display groups or a signed reel-preview payload, but final result must remain server-owned.

### Finding 19: Local shop rotation, daily rewards, fake online count, and duplicate bonus remain in config

Area/screen:

- Shared config, shop, daily, inventory.

Files:

- `Assets/_ValoCase/Scripts/Data/GameConfigSO.cs`
- `Assets/_ValoCase/Scripts/Core/GameConstants.cs`
- `Assets/_ValoCase/Scripts/Services/GameServices.cs`

What is wrong or risky:

Unity config still defines starting VP, sell multiplier, duplicate bonus percent, daily reward streaks, shop rotation hours, featured slots, deal slots, fake online counts, and progression unlock tier cadence.

Why this is amateur/temporary/duplicated:

These are not just visual preferences. Several are economy and game-state rules that backend should own.

How it could cause exploit, desync, or bad UX:

- Local/offline behavior diverges from backend.
- Developers may tune Unity config and expect production economy to change.
- UI can reuse these values for backend screens by mistake.

Severity: Medium.

Recommended fix:

- Split `GameConfigSO` into presentation-only config and offline-demo config.
- Remove economy values from production UI paths.
- Add comments/assertions that these fields are ignored in backend mode, or move them to a demo-only asset.

Backend changes required:

- Backend should expose corresponding production economy values through state/preview responses.

### Finding 20: Case opening local stats use local price after backend open

Area/screen:

- Case opening statistics/progression display.

Files:

- `Assets/_ValoCase/Scripts/CaseOpening/CaseOpeningFlowController.cs`
- `Assets/_ValoCase/Scripts/UI/Screens/CaseOpeningScreen.cs`
- `Assets/_ValoCase/Scripts/Services/Backend/BackendApiClient.cs`

What is wrong or risky:

Backend open response does not include `vpSpent`. Unity derives spent VP from selected local `caseDef.VpPrice`.

Why this is amateur/temporary/duplicated:

Even if wallet is authoritative, stats and spend displays are still based on local catalog values.

How it could cause exploit, desync, or bad UX:

- Local stats can record wrong spend.
- Player history/analytics can drift from backend.
- Multi-open spend totals can be wrong after backend price changes.

Severity: Medium.

Recommended fix:

- Backend open response should include `priceChargedVp` or `vpSpent`.
- Unity stats should use backend returned spend.

Backend changes required:

- Yes.

## 4. Backend endpoint gaps Unity needs

### Case preview/state endpoint

Current Unity guess:

- Price from local `caseDef.VpPrice`.
- Odds from local drop table.
- Affordability from cached wallet.
- Unlock state from local progression.
- Button state from local `CanOpen`.

Recommended endpoint:

- `GET /cases/{caseId}/preview?quantity=1`
- Or batch: `POST /cases/preview` with case IDs and quantities.

Suggested response shape:

```json
{
  "caseId": "vandal_basic",
  "quantity": 1,
  "enabled": true,
  "canOpen": true,
  "blockedReason": null,
  "priceVp": 500,
  "totalPriceVp": 500,
  "walletVp": 1200,
  "walletAfterVp": 700,
  "unlocked": true,
  "requiredLevel": 1,
  "category": "Vandal",
  "oddsVersion": "2026-06-21T00:00:00Z",
  "rarityOdds": [
    { "rarity": "Select", "chancePercent": 65.0 }
  ],
  "itemOdds": [
    { "skinId": "skin_001", "chancePercent": 4.25 }
  ]
}
```

### Case open response should include charged price and item identity

Current Unity guess:

- `vpSpent` is derived from local price.
- Review sell binds to `skinId`, not the exact awarded item.

Recommended response fields:

```json
{
  "caseId": "vandal_basic",
  "priceChargedVp": 500,
  "newVpBalance": 700,
  "wonSkin": {
    "skinId": "skin_001",
    "displayName": "Example",
    "rarity": "Select",
    "valueVp": 1000,
    "sellValueVp": 350
  },
  "inventoryItemId": "inv_abc123",
  "progression": {}
}
```

### Inventory valuation and sell preview

Current Unity guess:

- Collection value from local skin values.
- Sell value from local multiplier.
- Sell-below-value threshold against local values.

Recommended endpoints:

- `GET /inventory` should include item value and sell value per item.
- `POST /inventory/sell-preview` for sell one, sell all, and sell below value.
- `POST /inventory/sell` should accept item IDs for one-item sale.

Suggested sell preview response:

```json
{
  "mode": "BELOW_VALUE",
  "thresholdVp": 2000,
  "matchedItemCount": 12,
  "totalSellValueVp": 5400,
  "walletVp": 10000,
  "walletAfterVp": 15400,
  "items": [
    { "inventoryItemId": "inv_1", "skinId": "skin_001", "sellValueVp": 350 }
  ]
}
```

### Battle preview and stable completed result

Current Unity guess:

- Create battle screen calculates cost locally.
- Local fallback lobbies calculate wager locally.
- Completed mapper prefers display name in winner resolution.

Recommended endpoint/fields:

- Create/join preview should return `canCreate`, `canJoin`, `entryCostVp`, `walletAfterVp`, `blockedReason`, and required unlock/level details.
- Completed battle response should always include stable `winnerSlotIndex` and `winnerAccountId`.
- Lobby response should include case display metadata or a catalog version.

### Mission state should expose explicit claimability

Current Unity guess:

- Mission notification/button sometimes uses `progress >= target`.

Recommended response fields:

```json
{
  "missionId": "open_cases_10",
  "progress": 10,
  "target": 10,
  "rewardVp": 500,
  "status": "CLAIMABLE",
  "claimable": true,
  "statusReason": null,
  "secondsUntilReset": 3600
}
```

### Daily status should be used everywhere

Current Unity issue:

- Tools screen hardcodes `+2,000 VP`.

Recommended field:

- Already appears available as `nextRewardVp`; use it in every daily UI.

### Backend catalog/state endpoint

Current Unity guess:

- Prices, values, enabled flags, odds, unlocks, and grouping come from local catalog.

Recommended endpoint:

- `GET /game-state/catalog` or `GET /player/game-state`

Suggested response contents:

- Case IDs, display names, backend prices, enabled flags, categories, unlock state, required levels, odds display version.
- Skin IDs, backend value, backend sell value, rarity, enabled state.
- Player wallet, progression, unlocked categories, and server time.

Unity can still use local assets for sprites, colors, and fallback text, but not for economy truth.

### Earn VP claim response should distinguish estimate from accepted grant

Current Unity guess:

- Local pending estimate is displayed before backend grants.

Recommended response fields:

```json
{
  "clientSessionId": "uuid",
  "submittedTapCount": 120,
  "acceptedTapCount": 96,
  "rejectedTapCount": 24,
  "vpGranted": 180,
  "newBalance": 10180,
  "message": "24 taps ignored due to cadence limit"
}
```

## 5. Duplicated rule inventory

### Case prices

Duplicated in Unity:

- `CaseDefinitionSO.VpPrice`
- `BalanceOverrideService`
- `ShopScreen`
- `CaseOpeningScreen`
- `CreateBattleScreen`
- `LobbyListScreen`

Owner should be:

- Backend.

Unity should display:

- Backend case price from case state/preview/lobby response.

### Drop odds and item chances

Duplicated in Unity:

- `CaseDropTableSO.RarityWeights`
- `LocalResultProvider`
- `CaseOpeningScreen.BuildRateTable`
- `CaseOpeningScreen.CalculateDropChance`
- `BalanceOverrideService.ApplyDropOverrides`

Owner should be:

- Backend.

Unity should display:

- Backend odds display response. Local drop table can be offline-demo only or cosmetic filler only.

### Skin values and sell values

Duplicated in Unity:

- `SkinDefinitionSO.VpValue`
- `GameConfigSO.SellMultiplier`
- `GameConstants.SellValueMultiplier`
- `InventoryService.InventoryValue`
- `SkinCardView`
- `SkinDetailPopup`
- `InventoryScreen`

Owner should be:

- Backend.

Unity should display:

- Backend item value and backend sell value per inventory item.

### Wallet mutation

Duplicated in Unity:

- `VpCurrencyService.TrySpend`
- `VpCurrencyService.Add`
- `EconomyService.GrantReward`
- `DailyRewardService.TryClaim`
- `InventoryService.TrySell`
- `CaseBattleSystem.TryStartBattle`

Owner should be:

- Backend for production.

Unity should display:

- Backend wallet snapshot and action result balances.

### Inventory mutation

Duplicated in Unity:

- `InventoryService.AddSkin`
- `InventoryService.TrySell`
- `InventoryService.ConsumeOne`
- `EconomyService.SellOne`
- `EconomyService.SellMatching`
- local battle settlement
- local case open completion

Owner should be:

- Backend for production.

Unity should display:

- Backend inventory snapshot. Local aggregation may be used for rendering, but not as ownership truth.

### Daily rewards

Duplicated in Unity:

- `GameConfigSO.dailyVpStreakRewards`
- `DailyRewardService`
- `ToolsScreen.DailyRewardVp`

Owner should be:

- Backend.

Unity should display:

- `DailyStatusResponse.nextRewardVp`, `claimable`, current streak, and backend countdown.

### Missions

Duplicated in Unity:

- `MissionSystem` definitions, progress, claim rules, rewards.

Owner should be:

- Backend.

Unity should display:

- Backend mission cards and backend claim status.

### Progression and unlocks

Duplicated in Unity:

- `PlayerProgression.UnlockLevels`
- `PlayerProgression.CategoryForCaseId`
- `CaseProgressionService`
- authored `CaseDefinitionSO.UnlockType` and `UnlockRequirement`

Owner should be:

- Backend for eligibility. Unity can use local data only as presentation fallback.

Unity should display:

- Backend per-case access state and backend progression snapshot.

### Battle rolls, totals, and winner selection

Duplicated in Unity:

- `BattleOpeningEngine`
- `LocalBattleService`
- `CaseBattleSession`
- `CaseBattleSystem`

Owner should be:

- Backend.

Unity should display:

- Backend battle result.

### Earn VP reward formula

Duplicated in Unity:

- `EarnVpScreen` constants and local reward/multiplier/crit calculation.

Owner should be:

- Backend for production grant.

Unity should display:

- Backend-confirmed grants. Local animation can show tap feedback without claiming actual VP.

### Shop rotation and deals

Duplicated in Unity:

- `ShopService.RotateShop`
- `ShopService.GetDiscountedPrice`
- `GameConfigSO.shopRotationHours`
- `featuredCaseSlots`
- `dailyDealSlots`

Owner should be:

- Backend if shop affects availability or price.

Unity should display:

- Backend shop state. If shop is purely visual navigation, local rotation must not affect price or availability.

## 6. UI misleading-state risks

### Wrong chance

- `CaseOpeningScreen` displays local rarity and item chances from drop tables.
- Cosmetic reels and filler pools can visually imply odds that backend does not use.

### Wrong reward

- Earn VP shows local `+VP` and session estimates before backend claim.
- Tools screen daily card hardcodes `+2,000 VP`.
- Reward review totals local `skin.VpValue`.
- Mission claim badge can use progress-derived claimability instead of backend status.

### Wrong wallet

- Wallet labels use local cached `ctx.Vp.Balance`.
- Cache is overwritten after successful backend responses, which is good.
- But boot/resync failures keep cached balance, which can be stale and still displayed as truth.

### Wrong unlock

- Shop and case opening use local progression/unlock functions.
- Unknown case category can default to Classic in local classification.
- Battle create is stricter than shop/case opening, so screens can disagree.

### Wrong sell value

- Skin cards and detail popup display sell value from local multiplier.
- Inventory collection value uses local skin values.
- Sell-below confirmation text implies local threshold matches backend valuation.

### Wrong battle status

- Battle lobby can show local bot fallback when backend is not ready.
- Completed battle winner mapping can choose by display name.
- Local fallback battle stats can be recorded if old paths are reached.

### Wrong action availability

- Open button uses local affordability/unlock/drop table availability.
- Lobby join affordability uses cached local wallet and returns true if VP service is null.
- Mission button can become interactable from progress/target instead of backend claimable status.

## 7. Recommended Unity architecture standard

### What Unity may calculate locally

- Pure presentation layout: sorting sections, grouping for display, animations, colors, sprite selection, temporary loading skeletons.
- Cosmetic-only animation timing and interpolation.
- Client-side input validation for obvious invalid forms, as long as backend still validates.
- Formatting already-confirmed backend values.
- Offline demo behavior only when explicitly compiled or configured as offline demo.

### What Unity should never calculate locally in backend mode

- Case price, battle entry cost, sell value, reward value, duplicate bonus, daily reward amount, mission reward amount.
- Drop odds, item chances, rarity weights, case roll result, battle roll result, winner selection.
- Wallet balance as final truth.
- Inventory ownership as final truth.
- Unlock/eligibility/claimability as final truth.
- Bulk sell contents or payout as final truth.
- Earn VP grant amount as final truth.
- Final progression level/XP/unlocked category changes.

### How future screens should be structured

- Each economy screen should start from a backend state or preview response.
- UI buttons should be enabled from backend `can...` or `claimable` fields, not local guesses.
- Every action should send intent only:
  - open this case
  - create this lobby with these case selections
  - sell this inventory item ID
  - claim this mission ID
  - claim this earn-VP session telemetry
- Every action response should include the authoritative post-action wallet and any changed inventory/progression state or version.
- Unity should reconcile after every economy action.
- If state is stale, display stale/syncing status and disable economy actions or force a preview/action call.
- Local catalog should be presentation metadata. Backend catalog should be economy metadata.
- Local/offline providers should be separated by build define and naming, not silently available through the same service interfaces.

### Suggested production guardrails

- Add a central `BackendAuthorityMode` flag and assert if local mutation services are called while it is active.
- Make `GrantReward`, `TrySpend`, `TrySell`, `RollSkin`, local battle settlement, and local mission claim throw/log critical errors in backend builds.
- Add telemetry for any backend rejection caused by stale local UI state.
- Add "backend state age" to wallet and inventory caches.
- Prefer item IDs over skin IDs for any operation on owned inventory.

## 8. Prioritized action plan

### First fixes before adding more features

1. Remove backend-enabled fallback to local bot lobbies in `LobbyListScreen`.
2. Add backend case preview/state and make case opening display backend price, odds, unlock, and affordability.
3. Add `priceChargedVp` or `vpSpent` and authoritative item sell value to `OpenCaseResultResponse`.
4. Change sell-one flow from `skinId` to backend `inventoryItemId`.
5. Add stale-state handling for wallet and inventory after backend sync failures.
6. Make mission buttons and notification badges use backend `status == "CLAIMABLE"` or `claimable == true`.

### Quick wins

1. Replace `ToolsScreen.DailyRewardVp` display with backend `nextRewardVp`.
2. In backend mode, disable lobby join/create when wallet is unknown instead of treating null VP as affordable.
3. In backend mode, fail closed on unresolved case IDs instead of falling back to the first/basic case.
4. Change battle winner mapping to prefer stable `winnerSlotIndex` over display name.
5. Label Earn VP pending amounts as unconfirmed, or show only backend-confirmed grants.
6. Hide or mark local odds as unavailable until backend odds preview exists.

### Larger refactors

1. Split production backend services from offline demo services.
2. Convert local catalog economy fields into presentation-only metadata.
3. Replace local mission system in backend mode with backend mission state only.
4. Replace local progression/unlock logic in backend mode with backend case access state.
5. Remove or compile out local battle engines from production builds.
6. Add a backend-driven game state store in Unity:
   - wallet snapshot
   - inventory snapshot with item IDs and values
   - progression snapshot
   - case catalog/access snapshot
   - mission/daily snapshot
   - sync freshness/version fields

## Explicit uncertainties and next checks

- I did not inspect backend code. The backend may already enforce many rules correctly; this audit only confirms where Unity guesses, duplicates, or can mislead.
- `LobbyListScreen.OnStartBattle` is wired to `WaitingRoomScreen.OnStartBattle`, but I found no invocation of `OnStartBattle` in the searched screen files. This looks like stale/dead wiring, not a confirmed active path. Next check: inspect Unity prefabs/scenes and any generated UI bindings for `WaitingRoomScreen.OnStartBattle` invocation if needed.
- I did not inspect every prefab reference, so reachability of `DailyRewardPopup`, old `CaseBattleSystem`, and every local provider path should be confirmed in scenes/prefabs. Next check: search scene/prefab serialized references for these MonoBehaviours and service construction paths.
- I did not audit the current Upgrade screen by request. Shared inventory item-ID concerns may affect upgrade systems, but upgrade-specific chance/preview logic is intentionally out of scope here.

