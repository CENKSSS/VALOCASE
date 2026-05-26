using System;
using System.Collections.Generic;
using ValoCase.Data;
using ValoCase.Services;

namespace ValoCase.Systems
{
    /// <summary>
    /// Event-driven facade over <see cref="IUpgradeService"/>.
    ///
    /// Owns the *selection state* (selectedInput / selectedTarget) so the UI screen
    /// becomes purely a renderer: it no longer needs to track "which skin did the
    /// user click last". Filter state (weapon, rarity) is also owned here.
    ///
    /// CONTRACT
    ///   • Plain C# class. Construct with an IUpgradeService.
    ///   • Screen calls Select* / SetFilter* and listens for OnSelectionChanged.
    ///   • Screen calls RequestUpgrade(); system invokes service, raises OnResolved.
    /// </summary>
    public sealed class UpgradeSystem
    {
        readonly IUpgradeService _service;

        public SkinDefinitionSO SelectedInput  { get; private set; }
        public SkinDefinitionSO SelectedTarget { get; private set; }
        public string           WeaponFilter   { get; private set; }
        public SkinRarity?      RarityFilter   { get; private set; }
        public bool             IsBusy         { get; private set; }

        public bool CanUpgrade =>
            !IsBusy && SelectedInput != null && SelectedTarget != null;

        // ── Events ────────────────────────────────────────────────────────────
        public event Action OnSelectionChanged;
        public event Action OnFilterChanged;
        public event Action OnBusyChanged;
        public event Action<SkinDefinitionSO, SkinDefinitionSO, bool> OnResolved; // input, target, success

        public UpgradeSystem(IUpgradeService service)
        {
            _service = service;
        }

        // ── Selection ─────────────────────────────────────────────────────────
        public void SelectInput(SkinDefinitionSO skin)
        {
            if (IsBusy) return;
            if (SelectedInput == skin) return;
            SelectedInput  = skin;
            SelectedTarget = null;     // reset target on input change
            OnSelectionChanged?.Invoke();
        }

        public void SelectTarget(SkinDefinitionSO skin)
        {
            if (IsBusy) return;
            if (SelectedTarget == skin) return;
            SelectedTarget = skin;
            OnSelectionChanged?.Invoke();
        }

        public void ClearSelection()
        {
            if (SelectedInput == null && SelectedTarget == null) return;
            SelectedInput  = null;
            SelectedTarget = null;
            OnSelectionChanged?.Invoke();
        }

        // ── Filters ───────────────────────────────────────────────────────────
        public void SetWeaponFilter(string weapon)
        {
            if (WeaponFilter == weapon) return;
            WeaponFilter = weapon;
            OnFilterChanged?.Invoke();
        }

        public void SetRarityFilter(SkinRarity? rarity)
        {
            if (RarityFilter == rarity) return;
            RarityFilter = rarity;
            OnFilterChanged?.Invoke();
        }

        public void ResetFilters()
        {
            if (WeaponFilter == null && RarityFilter == null) return;
            WeaponFilter = null;
            RarityFilter = null;
            OnFilterChanged?.Invoke();
        }

        // ── Upgrade resolution ────────────────────────────────────────────────
        public float ComputeChance()
        {
            if (_service == null || SelectedInput == null || SelectedTarget == null) return 0f;
            return _service.ComputeChance(SelectedInput, SelectedTarget);
        }

        public List<SkinDefinitionSO> GetEligibleTargets()
        {
            if (_service == null || SelectedInput == null) return new List<SkinDefinitionSO>();
            return _service.GetEligibleTargets(SelectedInput);
        }

        public bool RequestUpgrade()
        {
            if (!CanUpgrade || _service == null) return false;

            IsBusy = true;
            OnBusyChanged?.Invoke();

            var input  = SelectedInput;
            var target = SelectedTarget;
            var resolved = _service.TryUpgrade(input, target, out var success);

            IsBusy = false;
            OnBusyChanged?.Invoke();

            if (resolved)
            {
                OnResolved?.Invoke(input, target, success);
                // On success, the won skin becomes the new input
                SelectedInput  = success ? target : null;
                SelectedTarget = null;
                OnSelectionChanged?.Invoke();
            }

            return resolved;
        }
    }
}
