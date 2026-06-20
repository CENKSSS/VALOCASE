using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValoCase.UI
{
    public sealed class UINavigator : MonoBehaviour
    {
        [SerializeField] List<UIScreenBase> screens = new();
        [SerializeField] ScreenType defaultScreen = ScreenType.Shop;

        readonly Dictionary<ScreenType, UIScreenBase> _map = new();
        UIScreenBase _current;

        /// <summary>Fired whenever navigation succeeds (screen actually changes).</summary>
        public event Action<ScreenType> OnNavigated;

        /// <summary>The ScreenType currently displayed.</summary>
        public ScreenType CurrentScreen { get; private set; }

        /// <summary>The ScreenType that was displayed before the current one.</summary>
        public ScreenType PreviousScreen { get; private set; }

        /// <summary>While true, all navigation requests are ignored (e.g. during a case spin).</summary>
        public bool NavigationLocked { get; set; }

        void Awake()
        {
            foreach (var screen in screens)
            {
                if (screen == null) continue;
                _map[screen.ScreenType] = screen;
                screen.HideImmediate();
            }

            foreach (var kvp in _map)
                Debug.Log($"[UINavigator] Registered: {kvp.Key} → {kvp.Value?.GetType().Name ?? "null"}");

            Debug.Log("[STARTUP] Initial screen set to CASES (defaultScreen=" + defaultScreen + ")");
            Debug.Log("[DAILY_REWARD] Auto open disabled — daily reward only shown on explicit button press");
            Navigate(defaultScreen, instant: true);
        }

        public void Navigate(ScreenType type, bool instant = false)
        {
            if (NavigationLocked) return;
            Debug.Log($"[UINavigator] Navigate → {type} | found: {_map.ContainsKey(type)}");
            if (!_map.TryGetValue(type, out var next))
            {
                Debug.LogWarning($"[UINavigator] Navigate failed: '{type}' not in map. Registered: {string.Join(", ", _map.Keys)}");
                return;
            }
            // Re-tapping the current screen (e.g. the active bottom-nav tab) lets the
            // screen reset its own sub-state instead of being a dead no-op.
            if (_current == next) { _current.OnReselected(); return; }

            // Screens like Settings request a zero-delay open; promote the whole
            // transition (outgoing hide + incoming show) to instant.
            if (next.OpensInstantly) instant = true;

            if (_current != null)
            {
                if (instant) _current.HideImmediate();
                else _current.HideAnimated();
            }

            _current = next;
            PreviousScreen = CurrentScreen;
            CurrentScreen = type;

            if (instant) _current.ShowImmediate();
            else _current.ShowAnimated();

            OnNavigated?.Invoke(type);
        }
    }
}
