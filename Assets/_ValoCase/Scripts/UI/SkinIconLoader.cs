using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ValoCase.Data;

namespace ValoCase.UI
{
    /// <summary>
    /// Shared, non-blocking loader for skin icon sprites.
    ///
    /// Replaces the synchronous Resources.Load on the UI hot path: card grids can show
    /// a placeholder immediately and have the real sprite streamed in via
    /// Resources.LoadAsync, so binding many cards in one frame no longer hitches.
    ///
    /// CACHING
    ///   Once a sprite is resolved it is cached back onto the SkinDefinitionSO
    ///   (SetResolvedIcon), so later accesses — sync (Icon) or async (this loader) —
    ///   are instant. Only icons that are actually displayed are ever loaded; the full
    ///   catalog is never eager-loaded, preserving the lazy-memory behaviour.
    ///
    ///   Concurrent requests for the same skin are de-duplicated to a single load.
    /// </summary>
    public sealed class SkinIconLoader : MonoBehaviour
    {
        static SkinIconLoader _instance;
        static bool _quitting;

        static SkinIconLoader Instance
        {
            get
            {
                if (_instance == null && !_quitting)
                {
                    var go = new GameObject("SkinIconLoader") { hideFlags = HideFlags.HideAndDontSave };
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<SkinIconLoader>();
                }
                return _instance;
            }
        }

        // Skins with an in-flight async load → callbacks waiting for that single load.
        readonly Dictionary<SkinDefinitionSO, List<Action<Sprite>>> _pending =
            new Dictionary<SkinDefinitionSO, List<Action<Sprite>>>();

        static Sprite _placeholder;

        /// <summary>A tiny neutral sprite to show while the real icon streams in.</summary>
        public static Sprite Placeholder => _placeholder != null
            ? _placeholder
            : (_placeholder = CreatePlaceholder());

        /// <summary>
        /// Resolves a skin's icon without ever blocking. If it is already cached, onReady
        /// is invoked immediately with the sprite; otherwise the sprite is loaded
        /// asynchronously and onReady fires once on the main thread when it is ready
        /// (with null if the icon could not be loaded).
        /// </summary>
        public static void Request(SkinDefinitionSO skin, Action<Sprite> onReady)
        {
            if (skin == null) { onReady?.Invoke(null); return; }

            // Already resolved (or nothing to load) → instant, no coroutine.
            if (skin.TryGetCachedIcon(out var cached)) { onReady?.Invoke(cached); return; }

            if (_quitting || Instance == null) { onReady?.Invoke(null); return; }
            Instance.Enqueue(skin, onReady);
        }

        void Enqueue(SkinDefinitionSO skin, Action<Sprite> onReady)
        {
            if (_pending.TryGetValue(skin, out var list))
            {
                if (onReady != null) list.Add(onReady);   // join the in-flight load
                return;
            }

            list = new List<Action<Sprite>>();
            if (onReady != null) list.Add(onReady);
            _pending[skin] = list;
            StartCoroutine(LoadRoutine(skin));
        }

        IEnumerator LoadRoutine(SkinDefinitionSO skin)
        {
            Sprite sprite = null;
            var key = skin.IconResourceKey;
            if (!string.IsNullOrEmpty(key))
            {
                var req = Resources.LoadAsync<Sprite>(key);
                yield return req;
                sprite = req != null ? req.asset as Sprite : null;
            }

            // Cache on the skin so all later accesses are instant.
            skin.SetResolvedIcon(sprite);

            if (_pending.TryGetValue(skin, out var list))
            {
                _pending.Remove(skin);
                for (int i = 0; i < list.Count; i++)
                {
                    try { list[i]?.Invoke(sprite); }
                    catch (Exception e) { Debug.LogError("[SkinIconLoader] icon-ready callback failed: " + e); }
                }
            }
        }

        void OnApplicationQuit() => _quitting = true;

        // 4×4 faint translucent square — fills the icon rect softly while loading so a
        // card never flashes fully empty. One shared texture; negligible memory.
        static Sprite CreatePlaceholder()
        {
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            var fill = new Color32(255, 255, 255, 28);
            var px = new Color32[16];
            for (int i = 0; i < px.Length; i++) px[i] = fill;
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
        }
    }
}
