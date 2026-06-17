using System;
using System.Collections.Generic;

namespace ValoCase.Data
{
    [Serializable]
    public class BalanceOverrideRoot
    {
        public int version = 1;
        public List<SkinBalanceOverride> skinOverrides = new();
        public List<CaseBalanceOverride> caseOverrides = new();
        public List<CaseDropBalanceOverride> dropOverrides = new();
    }

    [Serializable]
    public class SkinBalanceOverride
    {
        public string skinId;
        public bool hasVpOverride;
        public int vpValueOverride;
        public bool enabled = true;
    }

    [Serializable]
    public class CaseBalanceOverride
    {
        public string caseId;
        public bool hasPriceOverride;
        public int priceOverride;
        public bool enabled = true;
    }

    [Serializable]
    public class CaseDropBalanceOverride
    {
        public string caseId;
        public string skinId;
        public bool hasWeightOverride;
        public float weightOverride;
        public bool enabled = true;
    }
}
