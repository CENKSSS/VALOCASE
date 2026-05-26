using System.Collections.Generic;
using UnityEngine;

namespace ValoCase.UI
{
    public sealed class UINavigator : MonoBehaviour
    {
        [SerializeField] List<UIScreenBase> screens = new();
        [SerializeField] ScreenType defaultScreen = ScreenType.MainMenu;

        readonly Dictionary<ScreenType, UIScreenBase> _map = new();
        UIScreenBase _current;

        void Awake()
        {
            foreach (var screen in screens)
            {
                if (screen == null) continue;
                _map[screen.ScreenType] = screen;
                screen.HideImmediate();
            }

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
            if (instant) _current.ShowImmediate();
            else _current.ShowAnimated();
        }
    }
}
