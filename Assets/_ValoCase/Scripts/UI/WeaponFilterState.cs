using ValoCase.Data;

namespace ValoCase.UI
{
    // Persists weapon/rarity filter selection across screen transitions.
    // Static so it survives without a MonoBehaviour or extra ScriptableObject.
    public static class WeaponFilterState
    {
        public static string SelectedWeapon = null;   // null = All
        public static SkinRarity? SelectedRarity = null; // null = All

        public static void Reset()
        {
            SelectedWeapon = null;
            SelectedRarity = null;
        }
    }
}
