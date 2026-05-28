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

        void Awake()
        {
            foreach (var screen in screens)
            {
                if (screen == null) continue;
                _map[screen.ScreenType] = screen;
                screen.HideImmediate();
            }

            Debug.Log("[STARTUP] Initial screen set to CASES (defaultScreen=" + defaultScreen + ")");
            Debug.Log("[DAILY_REWARD] Auto open disabled — daily reward only shown on explicit button press");
            Navigate(defaultScreen, instant: true);
        }

        public void Navigate(ScreenType type, bool instant = false)
        {
            if (!_map.TryGetValue(type, out var next)) return;
            if (_current == next) return;

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
