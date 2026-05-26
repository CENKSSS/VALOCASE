# ScriptableObject Examples

## SkinDefinitionSO

| Field | Example |
|-------|---------|
| skinId | `Vandal_Primordium` |
| skinName | Primordium |
| weaponName | Vandal |
| rarity | Premium |
| vpValue | 1775 |
| collectionName | Primordium |

## CaseDefinitionSO

| Field | Example |
|-------|---------|
| caseId | `case_standard` |
| displayName | Protocol Crate |
| vpPrice | 475 |
| dropTable | DropTable_Standard |
| isFeatured | false |

## CaseDropTableSO

**Rarity weights (Standard):**

```text
Select: 52%
Deluxe: 28%
Premium: 14%
Exclusive: 5%
Ultra: 1%    ← very low, as required
```

**Possible drops:** assign all skins that can drop from this case. Within a rarity bucket, skins split weight equally unless `skinWeightOverride` is set.

## GameConfigSO

- `startingVp`: 2500
- `dailyVpStreakRewards`: [150, 200, 275, 350, 450, 600, 900]
- `shopRotationHours`: 24

## ContentDatabaseSO

Master list of all `SkinDefinitionSO` and `CaseDefinitionSO` references. Lives in `Resources/ContentDatabase.asset`.

Generate via **ValoCase → Generate Sample Content**.
