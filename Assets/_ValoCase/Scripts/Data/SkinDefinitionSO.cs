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
        [TextArea(2, 4)] [SerializeField] string description;
        [SerializeField] int vpValue = 100;
        [SerializeField] string collectionName;
        [SerializeField] Color accentColor = Color.white;

        public string SkinId => string.IsNullOrEmpty(skinId) ? name : skinId;
        public string SkinName => skinName;
        public string WeaponName => weaponName;
        public SkinRarity Rarity => rarity;
        public Sprite Icon => icon;
        public string Description => description;
        public int VpValue => vpValue;
        public string CollectionName => collectionName;
        public Color AccentColor => accentColor;

        // Populates all fields from filesystem loader at runtime.
        // Not called for SO assets created in the Editor.
        public void InitializeRuntime(string id, string displayName, string weapon,
                                      SkinRarity rar, Sprite spr, int vp = 1775)
        {
            skinId        = id;
            skinName      = displayName;
            weaponName    = weapon;
            rarity        = rar;
            icon          = spr;
            vpValue       = vp;
            accentColor   = Color.white;
        }
    }
}
