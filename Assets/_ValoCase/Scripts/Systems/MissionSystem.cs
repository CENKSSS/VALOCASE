using System;
using System.Collections.Generic;
using ValoCase.Battle;
using ValoCase.Core;
using ValoCase.Data;
using ValoCase.Save;
using ValoCase.Services;

namespace ValoCase.Systems
{
    public enum MissionType
    {
        OpenCase,
        SpendVp,
        WinVandal,
        PlayBattle,
        WinBattle,
        WinPremiumOrHigher,
        AddSkinToInventory,
        OpenArcaneCase
    }

    public sealed class MissionDefinition
    {
        public int         Index;
        public string      Title;
        public MissionType Type;
        public int         TargetAmount;
        public int         RewardVp;
    }

    public sealed class MissionSystem
    {
        static readonly MissionDefinition[] s_defs =
        {
            new MissionDefinition { Index=0, Title="Open 5 Cases",               Type=MissionType.OpenCase,           TargetAmount=5,    RewardVp=500  },
            new MissionDefinition { Index=1, Title="Spend 5,000 VP",             Type=MissionType.SpendVp,            TargetAmount=5000, RewardVp=750  },
            new MissionDefinition { Index=2, Title="Win a Vandal Skin",          Type=MissionType.WinVandal,          TargetAmount=1,    RewardVp=600  },
            new MissionDefinition { Index=3, Title="Play 3 Case Battles",        Type=MissionType.PlayBattle,         TargetAmount=3,    RewardVp=400  },
            new MissionDefinition { Index=4, Title="Win 2 Case Battles",         Type=MissionType.WinBattle,          TargetAmount=2,    RewardVp=700  },
            new MissionDefinition { Index=5, Title="Win 3 Premium+ Skins",       Type=MissionType.WinPremiumOrHigher, TargetAmount=3,    RewardVp=800  },
            new MissionDefinition { Index=6, Title="Add 10 Skins to Inventory",  Type=MissionType.AddSkinToInventory, TargetAmount=10,   RewardVp=1000 },
            new MissionDefinition { Index=7, Title="Open 3 Arcane Vandal Cases", Type=MissionType.OpenArcaneCase,     TargetAmount=3,    RewardVp=900  },
        };

        public static int MissionCount => s_defs.Length;

        readonly ISaveService    _save;
        WeeklyMissionsSave       _data;

        public event Action OnChanged;

        public MissionSystem(ISaveService save) => _save = save;

        public void Initialize(CaseBattleSystem battle = null)
        {
            if (_save.Data.weeklyMissions == null)
                _save.Data.weeklyMissions = new WeeklyMissionsSave();
            _data = _save.Data.weeklyMissions;

            while (_data.missions.Count < s_defs.Length)
                _data.missions.Add(new MissionProgressEntry { missionIndex = _data.missions.Count });

            GameEvents.OnCaseOpened   += HandleCaseOpened;
            GameEvents.OnVpChanged    += HandleVpChanged;
            GameEvents.OnSkinObtained += HandleSkinObtained;

            if (battle != null)
            {
                battle.OnBattleStarted += _ => Increment(MissionType.PlayBattle, 1);
                battle.OnBattleSettled += s => { if (s.Outcome == BattleOutcome.PlayerWins) Increment(MissionType.WinBattle, 1); };
            }
        }

        public void Dispose()
        {
            GameEvents.OnCaseOpened   -= HandleCaseOpened;
            GameEvents.OnVpChanged    -= HandleVpChanged;
            GameEvents.OnSkinObtained -= HandleSkinObtained;
        }

        public MissionDefinition    GetDef(int i)   => s_defs[i];
        public MissionProgressEntry GetEntry(int i) => _data.missions[i];

        public bool TryClaim(int i, out int rewardVp)
        {
            rewardVp = 0;
            var e = _data.missions[i];
            var d = s_defs[i];
            if (e.claimed || e.currentAmount < d.TargetAmount) return false;
            int claimIdx = 0;
            foreach (var m in _data.missions) if (m.claimed) claimIdx++;
            e.claimOrder = claimIdx;
            e.claimed    = true;
            rewardVp  = d.RewardVp;
            _save.Save();
            OnChanged?.Invoke();
            return true;
        }

        void HandleCaseOpened(CaseDefinitionSO caseDef, SkinDefinitionSO skin)
        {
            Increment(MissionType.OpenCase, 1);
            if (caseDef != null && caseDef.name.IndexOf("arcane", StringComparison.OrdinalIgnoreCase) >= 0)
                Increment(MissionType.OpenArcaneCase, 1);
        }

        void HandleVpChanged(int prev, int curr)
        {
            if (curr < prev) Increment(MissionType.SpendVp, prev - curr);
        }

        void HandleSkinObtained(SkinDefinitionSO skin)
        {
            if (skin == null) return;
            Increment(MissionType.AddSkinToInventory, 1);
            if (string.Equals(skin.WeaponName, "Vandal", StringComparison.OrdinalIgnoreCase))
                Increment(MissionType.WinVandal, 1);
            if (skin.Rarity >= SkinRarity.Premium)
                Increment(MissionType.WinPremiumOrHigher, 1);
        }

        void Increment(MissionType type, int amount)
        {
            bool dirty = false;
            foreach (var d in s_defs)
            {
                if (d.Type != type) continue;
                var e = _data.missions[d.Index];
                if (e.claimed || e.currentAmount >= d.TargetAmount) continue;
                e.currentAmount = Math.Min(e.currentAmount + amount, d.TargetAmount);
                dirty = true;
            }
            if (!dirty) return;
            _save.Save();
            OnChanged?.Invoke();
        }
    }
}
