using UnityEngine;

namespace ValoCase.Data
{
    public enum CaseUnlockType
    {
        Available,
        Level,
        Achievement
    }

    [CreateAssetMenu(fileName = "Case_", menuName = "ValoCase/Case Definition", order = 2)]
    public class CaseDefinitionSO : ScriptableObject
    {
        [SerializeField] string caseId;
        [SerializeField] string displayName;
        [TextArea(2, 3)] [SerializeField] string description;
        [SerializeField] int vpPrice = 475;
        [SerializeField] Sprite caseIcon;
        [SerializeField] Sprite bannerArt;
        [SerializeField] CaseDropTableSO dropTable;
        [SerializeField] bool isFeatured;
        [SerializeField] bool isLimited;
        [SerializeField] CaseUnlockType unlockType = CaseUnlockType.Available;
        [SerializeField] int unlockRequirement;
        [SerializeField] Color themeColor = new(0.92f, 0.23f, 0.29f, 1f);

        public string CaseId => string.IsNullOrEmpty(caseId) ? name : caseId;
        public string DisplayName => displayName;
        public string Description => description;
        public int VpPrice => vpPrice;
        public Sprite CaseIcon => caseIcon;
        public Sprite BannerArt => bannerArt;
        public CaseDropTableSO DropTable => dropTable;
        public bool IsFeatured => isFeatured;
        public bool IsLimited => isLimited;
        public CaseUnlockType UnlockType => unlockType;
        public int UnlockRequirement => unlockRequirement;
        public Color ThemeColor => themeColor;

        // Populate at runtime (skips Inspector).
        public void InitializeRuntime(string id, string displayName, CaseDropTableSO table,
                                      int price, Color? color = null)
        {
            caseId           = id;
            this.displayName = displayName;
            dropTable        = table;
            vpPrice          = price;
            unlockType       = CaseUnlockType.Available;
            isFeatured       = true;
            themeColor       = color ?? new Color(0.92f, 0.23f, 0.29f, 1f);
        }

        public void SetVpPriceRuntime(int price) => vpPrice = price;

        // Optional runtime icon assignment (used by VandalCaseBuilder + Resources loader).
        public void SetIconRuntime(Sprite icon)
        {
            if (icon != null) caseIcon = icon;
        }
    }
}
