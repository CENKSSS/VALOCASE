using System;
using System.Collections.Generic;
using ValoCase.Data;
using ValoCase.Save;
using ValoCase.Services;

namespace ValoCase.Systems
{
    /// <summary>
    /// Event-driven facade over <see cref="IInventoryService"/>.
    ///
    /// Owns the screen-level *view state*: which filter mode, which sort mode and
    /// which weapon filter are currently active. The screen becomes a thin
    /// renderer that asks GetViewModel() for a ready-to-bind list.
    ///
    /// CONTRACT
    ///   • Plain C# class. Construct with an IInventoryService.
    ///   • Screen subscribes to OnViewChanged and pulls a new list each time.
    ///   • Sell / Consume calls flow back through the service.
    /// </summary>
    public sealed class InventorySystem
    {
        readonly IInventoryService _service;

        public SkinFilterMode    Filter       { get; private set; } = SkinFilterMode.All;
        public InventorySortMode Sort         { get; private set; } = InventorySortMode.RarityDesc;
        public string            WeaponFilter { get; private set; }

        public int UniqueCount    => _service?.UniqueCount    ?? 0;
        public int TotalCount     => _service?.TotalCount     ?? 0;
        public int InventoryValue => _service?.InventoryValue ?? 0;

        // ── Events ────────────────────────────────────────────────────────────
        /// <summary>Filter/sort changed → screen should rebuild its grid.</summary>
        public event Action OnViewChanged;
        /// <summary>Underlying inventory mutated (added/sold/consumed) → re-bind.</summary>
        public event Action OnInventoryMutated;

        public InventorySystem(IInventoryService service)
        {
            _service = service;
        }

        // ── External notification (called by adapter that listens to GameEvents) ──
        public void NotifyInventoryMutated()  => OnInventoryMutated?.Invoke();

        // ── Filter / sort state ───────────────────────────────────────────────
        public void SetFilter(SkinFilterMode filter)
        {
            if (Filter == filter) return;
            Filter = filter;
            OnViewChanged?.Invoke();
        }

        public void SetSort(InventorySortMode sort)
        {
            if (Sort == sort) return;
            Sort = sort;
            OnViewChanged?.Invoke();
        }

        public void SetWeaponFilter(string weapon)
        {
            if (WeaponFilter == weapon) return;
            WeaponFilter = weapon;
            OnViewChanged?.Invoke();
        }

        // ── Queries ───────────────────────────────────────────────────────────
        /// <summary>
        /// Returns filtered+sorted entries respecting Filter, Sort and (optional)
        /// WeaponFilter. The screen just iterates the result; no logic on its side.
        /// </summary>
        public List<OwnedSkinSaveEntry> GetViewModel(ContentDatabaseSO content)
        {
            if (_service == null) return new List<OwnedSkinSaveEntry>();
            var list = _service.GetFilteredSorted(Filter, Sort);
            if (string.IsNullOrEmpty(WeaponFilter) || content == null) return list;

            var filtered = new List<OwnedSkinSaveEntry>(list.Count);
            foreach (var e in list)
            {
                var skin = content.GetSkin(e?.skinId);
                if (skin == null) continue;
                if (string.Equals(skin.WeaponName, WeaponFilter, StringComparison.OrdinalIgnoreCase))
                    filtered.Add(e);
            }
            return filtered;
        }

        public bool Owns(string skinId)        => _service != null && _service.Owns(skinId);
        public int  GetQuantity(string skinId) => _service?.GetQuantity(skinId) ?? 0;

        // ── Commands (passthrough, no extra logic) ────────────────────────────
        public bool TrySell(string skinId, out int vpGained)
        {
            vpGained = 0;
            return _service != null && _service.TrySell(skinId, out vpGained);
        }

        public bool ConsumeOne(string skinId)
        {
            return _service != null && _service.ConsumeOne(skinId);
        }
    }
}
