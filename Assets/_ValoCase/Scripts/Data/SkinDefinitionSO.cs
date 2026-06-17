using UnityEngine;

namespace ValoCase.Data
{
    [CreateAssetMenu(fileName = "Skin_", menuName = "ValoCase/Skin Definition", order = 1)]
    public class SkinDefinitionSO : ScriptableObject
    {
        [SerializeField] string skinId;
        [SerializeField] string skinName;
        [SerializeField] string weaponName;
        [SerializeField] SkinRarity rarity;
        [SerializeField] Sprite icon;
        // Phase-6 mobile hardening: runtime catalog skins store only the Resources
        // path here and resolve the Sprite LAZILY on first Icon access (then cache it).
        // This avoids loading ~1000 skin textures into memory at startup. Editor-authored
        // SOs keep their serialized `icon` and never touch this path.
        [SerializeField] string iconResourceKey;
        [TextArea(2, 4)] [SerializeField] string description;
        [SerializeField] int vpValue = 100;
        [SerializeField] string collectionName;
        [SerializeField] Color accentColor = Color.white;

        // Set once we've attempted a lazy load (success or fail) so a missing sprite
        // isn't re-loaded on every access. Not serialized — runtime-only.
        [System.NonSerialized] bool _iconResolveAttempted;

        public string SkinId => string.IsNullOrEmpty(skinId) ? name : skinId;
        public string SkinName => skinName;
        public string WeaponName => weaponName;
        public SkinRarity Rarity => rarity;

        // Lazy: returns the serialized/cached sprite, or resolves it once from
        // iconResourceKey on first access. Identical API for all existing callers.
        public Sprite Icon
        {
            get
            {
                if (icon == null && !_iconResolveAttempted && !string.IsNullOrEmpty(iconResourceKey))
                {
                    _iconResolveAttempted = true;
                    icon = Resources.Load<Sprite>(iconResourceKey);
                }
                return icon;
            }
        }

        public string Description => description;
        public int VpValue => vpValue;
        public string CollectionName => collectionName;
        public Color AccentColor => accentColor;

        // ── Non-blocking icon access (for high-volume card grids) ──────────────
        // Resources key used for the lazy load (null/empty when there is nothing to
        // load, e.g. an editor-authored SO with a serialized sprite).
        public string IconResourceKey => iconResourceKey;

        // Returns the already-resolved sprite WITHOUT triggering a synchronous load.
        // Returns true when no async load is needed: the sprite is already cached, a
        // previous attempt finished (even if it failed), or there is no resource key.
        // Returns false (sprite = null) when an async load should be kicked off.
        public bool TryGetCachedIcon(out Sprite sprite)
        {
            sprite = icon;
            return icon != null || _iconResolveAttempted || string.IsNullOrEmpty(iconResourceKey);
        }

        // Caches a sprite resolved asynchronously (by SkinIconLoader), mirroring the
        // sync getter's caching so every later access — sync or async — is instant.
        // Marks the attempt complete even on failure so a missing sprite is never reloaded.
        public void SetResolvedIcon(Sprite sprite)
        {
            icon = sprite;
            _iconResolveAttempted = true;
        }

        // Populates all fields from filesystem loader at runtime (eager sprite).
        // Not called for SO assets created in the Editor.
        public void InitializeRuntime(string id, string displayName, string weapon,
                                      SkinRarity rar, Sprite spr, int vp = 1775)
        {
            skinId        = id;
            skinName      = displayName;
            weaponName    = weapon;
            rarity        = rar;
            icon          = spr;
            iconResourceKey = null;
            _iconResolveAttempted = spr != null;
            vpValue       = vp;
            accentColor   = Color.white;
        }

        public void SetVpValueRuntime(int vp) => vpValue = vp;

        // Phase-6: lazy variant — stores the Resources path instead of the Sprite.
        // The Sprite is loaded + cached on first Icon access. Used by the catalog loader
        // so startup no longer loads every skin texture into memory.
        public void InitializeRuntimeLazy(string id, string displayName, string weapon,
                                          SkinRarity rar, string resourceKey, int vp = 1775)
        {
            skinId          = id;
            skinName        = displayName;
            weaponName      = weapon;
            rarity          = rar;
            icon            = null;
            iconResourceKey = resourceKey;
            _iconResolveAttempted = false;
            vpValue         = vp;
            accentColor     = Color.white;
        }
    }
}
