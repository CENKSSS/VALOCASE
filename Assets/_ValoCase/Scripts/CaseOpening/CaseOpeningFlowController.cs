using System;
using System.Collections;
using UnityEngine;
using ValoCase.Core;
using ValoCase.Data;
using ValoCase.Services;
using ValoCase.Services.Backend;

namespace ValoCase.CaseOpening
{
    public sealed class CaseOpeningFlowController : MonoBehaviour
    {
        [SerializeField] CaseSpinController spinController;

        CaseDefinitionSO _activeCase;
        SkinDefinitionSO _rolledSkin;
        int _vpSpent;
        bool _sessionActive;
        bool _backendMode;   // true while a backend-authoritative open is in progress

        public bool SessionActive => _sessionActive;

        // Skin rolled at spin start — available until CompleteOpen succeeds.
        // CaseOpeningScreen reads this to show the reward and as a fallback.
        public SkinDefinitionSO RolledSkin => _rolledSkin;

        public void StartOpening(CaseDefinitionSO caseDef)
        {
            if (_sessionActive || caseDef == null) return;
            var ctx = GameContext.Instance;
            if (ctx == null || ctx.CaseOpening == null) return;

            if (!ctx.CaseOpening.TryBeginOpen(caseDef, out _rolledSkin, out _vpSpent))
            {
                GameEvents.RaiseToast("Cannot open this case.");
                return;
            }

            _activeCase = caseDef;
            _sessionActive = true;
            _backendMode = false;
            Debug.Log($"[FLOW] StartOpening — case='{caseDef.CaseId}' rolledSkin='{_rolledSkin?.SkinName}'");
            spinController.BeginSpin(caseDef, _rolledSkin, OnSpinFinished);
        }

        // ── Backend-authoritative open (optimistic spin) ────────────────────────
        // Used only when GameContext.BackendEnabled is true. The reel starts the
        // instant the player taps (onSpinStarting + warmup spin), and the network
        // request runs in parallel. The reward stays 100% backend-authoritative: the
        // server still decides + commits (spend VP, grant skin), and the reel only
        // LANDS once that result arrives (ResolveWinner). Nothing is revealed or
        // granted on a guess. The session is marked active immediately so the back
        // button stays blocked and double taps are ignored. onFailed fires when the
        // server rejects the open — the warmup is stopped gracefully and no grant runs.
        public void StartOpeningBackend(CaseDefinitionSO caseDef, Action onSpinStarting, Action<string> onFailed)
        {
            if (_sessionActive || caseDef == null) return;
            var ctx = GameContext.Instance;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            bool meleeDbg = caseDef.CaseId == "melee_case";
            if (meleeDbg)
                Debug.Log($"[CASE_OPEN_DEBUG] StartOpeningBackend caseId='{caseDef.CaseId}' name='{caseDef.DisplayName}' price={caseDef.VpPrice} backendEnabled={(ctx != null && ctx.BackendEnabled)} backendReady={(ctx != null && ctx.BackendReady)} offline={BackendErrorMapper.IsOffline}");
#endif

            if (ctx == null || !ctx.BackendReady)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (meleeDbg) Debug.LogError("[CASE_OPEN_ERROR] melee_case aborted BEFORE request — backend not ready");
#endif
                onFailed?.Invoke("Sunucu kullanılamıyor.");
                return;
            }

            // Offline pre-check: do not start the warmup spin or any spend/grant logic
            // when there is no connectivity — surface the offline message and bail.
            if (BackendErrorMapper.IsOffline)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (meleeDbg) Debug.LogError("[CASE_OPEN_ERROR] melee_case aborted BEFORE request — offline");
#endif
                onFailed?.Invoke(BackendErrorMapper.Offline);
                return;
            }

            _activeCase    = caseDef;
            _sessionActive = true;
            _backendMode   = true;
            _rolledSkin    = null;

            // Instant feedback — reveal overlay + start the free-scrolling reel NOW,
            // before the network call. The winner is filled in by the server result.
            onSpinStarting?.Invoke();
            spinController.BeginWarmupSpin(caseDef);

            StartCoroutine(BackendOpenRoutine(caseDef, onFailed));
        }

        IEnumerator BackendOpenRoutine(CaseDefinitionSO caseDef, Action<string> onFailed)
        {
            var ctx = GameContext.Instance;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            bool meleeDbg = caseDef.CaseId == "melee_case";
            if (meleeDbg) Debug.Log($"[CASE_OPEN_DEBUG] sending backend open request — caseId='{caseDef.CaseId}'");
#endif

            OpenCaseResultResponse response = null;
            BackendError error = null;

            yield return ctx.Backend.OpenCase(caseDef.CaseId,
                r => response = r,
                e => error = e);

            // ── Request failed BEFORE any local commit — stop the reel, no spend/grant.
            if (error != null || response == null)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (meleeDbg)
                    Debug.LogError($"[CASE_OPEN_ERROR] melee_case open FAILED (during/after request) — status={(error != null ? error.HttpStatus : -1)} detail={(error != null ? error.ToString() : "null response")} userMsg=\"{BackendErrorMapper.Map(error)}\"");
#endif
                Debug.LogWarning("[FLOW] Backend open failed — " + (error?.ToString() ?? "null response"));
                spinController.CancelSpin();
                // Ambiguous transport failure (status 0) may have committed server-side:
                // reconcile wallet + inventory before we surface anything.
                if (error == null || error.HttpStatus == 0)
                    ctx.RequestBackendResync();
                AbortBackendSession();
                onFailed?.Invoke(BackendErrorMapper.Map(error));
                yield break;
            }

            // ── Resolve the backend-selected skin via the stable-ID catalog. ──
            var mapped = BackendResultMapper.ToCaseOpeningResult(response);
            var skin = ctx.Content != null ? ctx.Content.GetSkin(mapped?.RolledSkinId) : null;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (meleeDbg)
                Debug.Log($"[CASE_OPEN_DEBUG] response parsed — respCaseId='{response.caseId}' wonSkinId='{(response.wonSkin != null ? response.wonSkin.skinId : "null")}' wonRarity='{(response.wonSkin != null ? response.wonSkin.rarity : "null")}' newVpBalance={response.newVpBalance} inventoryItemId='{response.inventoryItemId}' localResolved={(skin != null)}");
#endif

            if (skin == null)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (meleeDbg)
                    Debug.LogError($"[CASE_OPEN_ERROR] melee_case AFTER response — backend skinId '{mapped?.RolledSkinId}' not found in local catalog (GetSkin returned null)");
#endif
                // Server committed but we cannot resolve the skin locally. Do NOT
                // fake-grant or refund. Stop the reel, apply the authoritative wallet,
                // reconcile inventory from the server, and show a safe message.
                Debug.LogError("[FLOW] Backend won skinId not resolvable locally: " +
                               (response.wonSkin != null ? response.wonSkin.skinId : "null"));
                spinController.CancelSpin();
                ctx.ApplyBackendWallet(response.newVpBalance);
                ctx.RequestBackendResync();
                AbortBackendSession();
                onFailed?.Invoke("Ödül eşitleniyor...");
                yield break;
            }

            // ── Apply authoritative wallet immediately (no local spend). ──
            ctx.ApplyBackendWallet(response.newVpBalance);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (meleeDbg)
                Debug.Log($"[CASE_OPEN_DEBUG] melee_case resolved local skin='{skin.SkinName}' id='{skin.SkinId}' — open OK, landing reel");
#endif

            _rolledSkin = skin;
            // Cosmetic stat only — authoritative wallet is response.newVpBalance.
            _vpSpent = caseDef.VpPrice;

            Debug.Log($"[FLOW] Backend open OK — case='{caseDef.CaseId}' skin='{skin.SkinName}' " +
                      $"vpSpent={_vpSpent} newBalance={response.newVpBalance}");

            // ── Lock the reel to the server skin and let it decelerate onto it. ──
            spinController.ResolveWinner(caseDef, skin, OnSpinFinishedBackend);
        }

        // Clears in-flight backend state when no spin will run. Unlocks the session
        // (re-enables back/open) without touching VP or inventory.
        void AbortBackendSession()
        {
            _sessionActive = false;
            _backendMode = false;
            _activeCase = null;
            _rolledSkin = null;
            _vpSpent = 0;
        }

        void OnSpinFinishedBackend(SkinDefinitionSO skin)
        {
            var caseDef = _activeCase;
            var vpSpent = _vpSpent;

            _sessionActive = false;

            if (skin == null) skin = _rolledSkin;
            Debug.Log($"[FLOW] OnSpinFinishedBackend — finalSkin='{skin?.SkinName ?? "NULL"}'");

            var ctx = GameContext.Instance;
            if (ctx != null && caseDef != null && skin != null)
            {
                // Backend already spent VP + granted the skin. Cache-only completion:
                // no VP mutation, no re-spend, no double-grant.
                ctx.CaseOpening.CompleteOpenFromBackend(caseDef, skin, vpSpent);
                ctx.Statistics?.RecalculateInventoryStats(ctx.Inventory, ctx.Content);
                ctx.Save?.Save();
            }

            // Clear AFTER persistence — TryForceComplete detects non-null == incomplete.
            _activeCase = null;
            _rolledSkin = null;
            _vpSpent = 0;
            _backendMode = false;
        }

        void OnSpinFinished(SkinDefinitionSO skin)
        {
            var caseDef = _activeCase;
            var vpSpent = _vpSpent;

            // Unlock UI immediately so button/back become responsive.
            _sessionActive = false;

            // If spin returned null winner (abnormal), fall back to our stored roll.
            if (skin == null) skin = _rolledSkin;
            Debug.Log($"[FLOW] OnSpinFinished — finalSkin='{skin?.SkinName ?? "NULL"}' storedRoll='{_rolledSkin?.SkinName ?? "NULL"}'");

            var ctx = GameContext.Instance;
            if (ctx != null && caseDef != null && skin != null)
            {
                ctx.CaseOpening.CompleteOpen(caseDef, skin, vpSpent);
                ctx.Statistics?.RecalculateInventoryStats(ctx.Inventory, ctx.Content);
                ctx.Save?.Save();
            }

            // Clear AFTER persistence — TryForceComplete detects non-null == incomplete.
            _activeCase = null;
            _rolledSkin = null;
            _vpSpent    = 0;
        }

        // Called by CaseOpeningScreen watchdog when the normal completion path
        // did not fire (e.g. coroutine killed, CompleteOpen threw, etc.).
        // Returns the skin that was saved, or null if nothing was needed.
        public SkinDefinitionSO TryForceComplete()
        {
            if (_sessionActive || _rolledSkin == null) return null;

            var skin    = _rolledSkin;
            var caseDef = _activeCase;
            var vpSpent = _vpSpent;
            var backend = _backendMode;

            _activeCase = null;
            _rolledSkin = null;
            _vpSpent    = 0;
            _backendMode = false;

            var ctx = GameContext.Instance;
            if (ctx != null && caseDef != null)
            {
                // Mirror the normal completion path for the active mode so the
                // watchdog fallback never double-spends or double-grants.
                if (backend)
                    ctx.CaseOpening.CompleteOpenFromBackend(caseDef, skin, vpSpent);
                else
                    ctx.CaseOpening.CompleteOpen(caseDef, skin, vpSpent);
                ctx.Statistics?.RecalculateInventoryStats(ctx.Inventory, ctx.Content);
                ctx.Save?.Save();
            }

            return skin;
        }

        public void Skip() => spinController?.Skip();
    }
}
