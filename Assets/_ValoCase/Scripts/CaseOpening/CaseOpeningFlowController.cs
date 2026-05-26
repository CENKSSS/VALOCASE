using UnityEngine;
using ValoCase.Core;
using ValoCase.Data;
using ValoCase.Services;

namespace ValoCase.CaseOpening
{
    public sealed class CaseOpeningFlowController : MonoBehaviour
    {
        [SerializeField] CaseSpinController spinController;

        CaseDefinitionSO _activeCase;
        SkinDefinitionSO _rolledSkin;
        int _vpSpent;
        bool _sessionActive;

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
            Debug.Log($"[FLOW] StartOpening — case='{caseDef.CaseId}' rolledSkin='{_rolledSkin?.SkinName}'");
            spinController.BeginSpin(caseDef, _rolledSkin, OnSpinFinished);
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

            _activeCase = null;
            _rolledSkin = null;
            _vpSpent    = 0;

            var ctx = GameContext.Instance;
            if (ctx != null && caseDef != null)
            {
                ctx.CaseOpening.CompleteOpen(caseDef, skin, vpSpent);
                ctx.Statistics?.RecalculateInventoryStats(ctx.Inventory, ctx.Content);
                ctx.Save?.Save();
            }

            return skin;
        }

        public void Skip() => spinController?.Skip();
    }
}
