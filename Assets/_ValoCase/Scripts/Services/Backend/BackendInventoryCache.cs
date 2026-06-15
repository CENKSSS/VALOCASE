using System;
using System.Collections.Generic;

namespace ValoCase.Services.Backend
{
    /// <summary>
    /// One owned backend inventory instance. The backend is authoritative and models
    /// inventory per-instance (each owned unit has its own itemId), so this preserves
    /// that identity on the Unity side. RUNTIME ONLY — never persisted into the save
    /// file (backend itemIds are server truth and go stale offline; they are rebuilt
    /// from each inventory sync instead).
    /// </summary>
    public sealed class BackendInventoryItem
    {
        public string ItemId;
        public string SkinId;
        public string AcquiredAt;
        public string Source;
    }

    /// <summary>
    /// Runtime, in-memory store of backend inventory instances, kept alongside the
    /// existing quantity/skinId save cache. The quantity-based UI keeps working exactly
    /// as before; this cache adds back the per-instance identity (itemId) that the
    /// aggregation throws away, so future systems (Upgrade, Trade, Market, gifting,
    /// item history) can request real itemIds.
    ///
    /// Contract:
    ///   • Rebuilt wholesale from every inventory sync (idempotent; no double-add).
    ///   • Empty in local mode and whenever no backend sync has run.
    ///   • Never mutated to reflect a pending operation — the backend remains the source
    ///     of truth and a resync reconciles the cache after any server-side change.
    /// </summary>
    public sealed class BackendInventoryCache
    {
        readonly List<BackendInventoryItem> _items = new();
        readonly Dictionary<string, List<BackendInventoryItem>> _bySkin = new();

        /// <summary>Total backend instances currently cached.</summary>
        public int Count => _items.Count;

        /// <summary>All cached instances (read-only view).</summary>
        public IReadOnlyList<BackendInventoryItem> Items => _items;

        /// <summary>
        /// Replace the entire cache from a backend inventory response. Items without an
        /// itemId (e.g. a legacy aggregate shape) are skipped — they have no instance
        /// identity and are represented only by the quantity cache. Idempotent.
        /// </summary>
        public void Rebuild(IEnumerable<InventoryItemResponse> responseItems)
        {
            _items.Clear();
            _bySkin.Clear();
            if (responseItems == null) return;

            foreach (var r in responseItems)
            {
                if (r == null || string.IsNullOrEmpty(r.itemId) || string.IsNullOrEmpty(r.skinId)) continue;

                var item = new BackendInventoryItem
                {
                    ItemId     = r.itemId,
                    SkinId     = r.skinId,
                    AcquiredAt = r.acquiredAt,
                    Source     = r.source
                };

                _items.Add(item);
                if (!_bySkin.TryGetValue(item.SkinId, out var list))
                {
                    list = new List<BackendInventoryItem>();
                    _bySkin[item.SkinId] = list;
                }
                list.Add(item);
            }
        }

        /// <summary>Drop all cached instances (used when leaving backend state).</summary>
        public void Clear()
        {
            _items.Clear();
            _bySkin.Clear();
        }

        /// <summary>How many backend instances are owned for a given skinId.</summary>
        public int CountForSkin(string skinId)
            => !string.IsNullOrEmpty(skinId) && _bySkin.TryGetValue(skinId, out var list) ? list.Count : 0;

        /// <summary>All backend instances for a skinId (read-only; empty if none).</summary>
        public IReadOnlyList<BackendInventoryItem> GetBackendItemsForSkin(string skinId)
            => !string.IsNullOrEmpty(skinId) && _bySkin.TryGetValue(skinId, out var list)
                ? list
                : Array.Empty<BackendInventoryItem>();

        /// <summary>The itemIds of every owned instance of a skinId.</summary>
        public List<string> GetBackendItemIds(string skinId)
        {
            var result = new List<string>();
            if (!string.IsNullOrEmpty(skinId) && _bySkin.TryGetValue(skinId, out var list))
                foreach (var item in list) result.Add(item.ItemId);
            return result;
        }

        /// <summary>
        /// Select up to <paramref name="count"/> candidate itemIds of a skinId to send to
        /// a consuming operation (e.g. Upgrade). NON-MUTATING by design: the backend
        /// performs the actual consumption and a subsequent inventory resync reconciles
        /// this cache. Returns false (and an empty list) if fewer than <paramref name="count"/>
        /// instances are owned, so callers never send itemIds they don't actually have.
        /// </summary>
        public bool TryConsumeCandidatesForSkin(string skinId, int count, out List<string> itemIds)
        {
            itemIds = new List<string>();
            if (count <= 0 || string.IsNullOrEmpty(skinId)) return false;
            if (!_bySkin.TryGetValue(skinId, out var list) || list.Count < count) return false;

            for (var i = 0; i < count; i++) itemIds.Add(list[i].ItemId);
            return true;
        }
    }
}
