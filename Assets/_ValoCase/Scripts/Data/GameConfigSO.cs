using System.Collections.Generic;
using UnityEngine;

namespace ValoCase.Data
{
    [CreateAssetMenu(fileName = "GameConfig", menuName = "ValoCase/Game Config", order = 0)]
    public class GameConfigSO : ScriptableObject
    {
        [Header("Economy")]
        [SerializeField] int startingVp = 2500;
        [SerializeField] float sellMultiplier = 0.35f;
        [SerializeField] int duplicateBonusPercent = 15;

        [Header("Daily Rewards")]
        [SerializeField] List<int> dailyVpStreakRewards = new() { 150, 200, 275, 350, 450, 600, 900 };

        [Header("Shop")]
        [SerializeField] int shopRotationHours = 24;
        [SerializeField] int featuredCaseSlots = 2;
        [SerializeField] int dailyDealSlots = 3;

        [Header("Fake Online")]
        [SerializeField] int onlineCountMin = 4200;
        [SerializeField] int onlineCountMax = 12800;

        [Header("Progression")]
        [SerializeField] int casesOpenedPerUnlockTier = 15;

        public int StartingVp => startingVp;
        public float SellMultiplier => sellMultiplier;
        public int DuplicateBonusPercent => duplicateBonusPercent;
        public IReadOnlyList<int> DailyVpStreakRewards => dailyVpStreakRewards;
        public int ShopRotationHours => shopRotationHours;
        public int FeaturedCaseSlots => featuredCaseSlots;
        public int DailyDealSlots => dailyDealSlots;
        public int OnlineCountMin => onlineCountMin;
        public int OnlineCountMax => onlineCountMax;
        public int CasesOpenedPerUnlockTier => casesOpenedPerUnlockTier;
    }
}
