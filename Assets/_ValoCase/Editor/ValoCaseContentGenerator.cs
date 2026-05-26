#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using ValoCase.Data;

namespace ValoCase.Editor
{
    public static class ValoCaseContentGenerator
    {
        const string Root = "Assets/_ValoCase/ScriptableObjects";
        const string ResourcesRoot = "Assets/_ValoCase/Resources";

        [MenuItem("ValoCase/Generate Sample Content")]
        public static void Generate()
        {
            EnsureFolder(Root);
            EnsureFolder(ResourcesRoot);
            EnsureFolder($"{Root}/Skins");
            EnsureFolder($"{Root}/Cases");
            EnsureFolder($"{Root}/DropTables");

            var skins = CreateSkins();
            var dropTables = CreateDropTables(skins);
            var cases = CreateCases(dropTables);
            var config = CreateOrLoad<GameConfigSO>($"{ResourcesRoot}/GameConfig.asset");
            var rarityVisuals = CreateRarityVisuals();
            var database = CreateOrLoad<ContentDatabaseSO>($"{ResourcesRoot}/ContentDatabase.asset");

            database.EditorSetContent(skins, cases);

            EditorUtility.SetDirty(database);
            EditorUtility.SetDirty(config);
            EditorUtility.SetDirty(rarityVisuals);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[ValoCase] Sample content generated.");
        }

        static List<SkinDefinitionSO> CreateSkins()
        {
            var definitions = new (string skin, string weapon, SkinRarity rarity, int vp, string collection)[]
            {
                ("Aero Frenzy", "Vandal", SkinRarity.Select, 875, "Ruchina"),
                ("Silvanus", "Phantom", SkinRarity.Select, 875, "Silvanus"),
                ("Topotek", "Operator", SkinRarity.Deluxe, 1275, "Topotek"),
                ("Nanobreak", "Sheriff", SkinRarity.Deluxe, 1275, "Nanobreak"),
                ("Magepunk", "Marshal", SkinRarity.Premium, 1775, "Magepunk"),
                ("Primordium", "Vandal", SkinRarity.Premium, 1775, "Primordium"),
                ("Champions 2024", "Knife", SkinRarity.Exclusive, 5350, "Champions"),
                ("Arcane", "Sheriff", SkinRarity.Exclusive, 2475, "Arcane"),
                ("Singularity", "Butterfly Knife", SkinRarity.Ultra, 5350, "Singularity"),
                ("RGX 11z Pro", "Phantom", SkinRarity.Ultra, 2975, "RGX"),
            };

            var list = new List<SkinDefinitionSO>();
            foreach (var d in definitions)
            {
                var path = $"{Root}/Skins/Skin_{d.skin.Replace(" ", "")}.asset";
                var skin = CreateOrLoad<SkinDefinitionSO>(path);
                SetSkin(skin, d.skin, d.weapon, d.rarity, d.vp, d.collection);
                list.Add(skin);
            }

            return list;
        }

        static void SetSkin(SkinDefinitionSO skin, string skinName, string weapon, SkinRarity rarity, int vp, string collection)
        {
            var so = new SerializedObject(skin);
            so.FindProperty("skinId").stringValue = $"{weapon}_{skinName}".Replace(" ", "");
            so.FindProperty("skinName").stringValue = skinName;
            so.FindProperty("weaponName").stringValue = weapon;
            so.FindProperty("rarity").enumValueIndex = (int)rarity;
            so.FindProperty("description").stringValue = $"{skinName} for the {weapon}. Portfolio fan recreation.";
            so.FindProperty("vpValue").intValue = vp;
            so.FindProperty("collectionName").stringValue = collection;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(skin);
        }

        static List<CaseDropTableSO> CreateDropTables(List<SkinDefinitionSO> skins)
        {
            var standard = CreateOrLoad<CaseDropTableSO>($"{Root}/DropTables/DropTable_Standard.asset");
            var premium = CreateOrLoad<CaseDropTableSO>($"{Root}/DropTables/DropTable_Premium.asset");

            ApplyDropTable(standard, skins, new[]
            {
                (SkinRarity.Select, 52f),
                (SkinRarity.Deluxe, 28f),
                (SkinRarity.Premium, 14f),
                (SkinRarity.Exclusive, 5f),
                (SkinRarity.Ultra, 1f),
            });

            ApplyDropTable(premium, skins, new[]
            {
                (SkinRarity.Select, 38f),
                (SkinRarity.Deluxe, 30f),
                (SkinRarity.Premium, 20f),
                (SkinRarity.Exclusive, 9f),
                (SkinRarity.Ultra, 3f),
            });

            return new List<CaseDropTableSO> { standard, premium };
        }

        static void ApplyDropTable(CaseDropTableSO table, List<SkinDefinitionSO> skins, (SkinRarity, float)[] weights)
        {
            var so = new SerializedObject(table);
            so.FindProperty("rarityWeights").arraySize = weights.Length;
            for (var i = 0; i < weights.Length; i++)
            {
                var elem = so.FindProperty("rarityWeights").GetArrayElementAtIndex(i);
                elem.FindPropertyRelative("rarity").enumValueIndex = (int)weights[i].Item1;
                elem.FindPropertyRelative("weightPercent").floatValue = weights[i].Item2;
            }

            so.FindProperty("possibleDrops").arraySize = skins.Count;
            for (var i = 0; i < skins.Count; i++)
            {
                var elem = so.FindProperty("possibleDrops").GetArrayElementAtIndex(i);
                elem.FindPropertyRelative("skin").objectReferenceValue = skins[i];
                elem.FindPropertyRelative("skinWeightOverride").floatValue = 0f;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(table);
        }

        static List<CaseDefinitionSO> CreateCases(List<CaseDropTableSO> tables)
        {
            var standard = tables[0];
            var premium = tables[1];

            var list = new List<CaseDefinitionSO>();
            list.Add(CreateCase("case_standard", "Protocol Crate", 475, standard, false, false, 0));
            list.Add(CreateCase("case_premium", "Radiant Relic", 950, premium, true, false, 0));
            list.Add(CreateCase("case_limited", "Night Market Cache", 1275, premium, true, true, 0));
            list.Add(CreateCase("case_locked", "Ranked Pro Vault", 1275, standard, false, false, 2, CaseUnlockType.Level));
            return list;
        }

        static CaseDefinitionSO CreateCase(string id, string name, int price, CaseDropTableSO table, bool featured, bool limited, int unlockReq, CaseUnlockType unlock = CaseUnlockType.Available)
        {
            var path = $"{Root}/Cases/Case_{id}.asset";
            var c = CreateOrLoad<CaseDefinitionSO>(path);
            var so = new SerializedObject(c);
            so.FindProperty("caseId").stringValue = id;
            so.FindProperty("displayName").stringValue = name;
            so.FindProperty("description").stringValue = $"{name} — fan portfolio case.";
            so.FindProperty("vpPrice").intValue = price;
            so.FindProperty("dropTable").objectReferenceValue = table;
            so.FindProperty("isFeatured").boolValue = featured;
            so.FindProperty("isLimited").boolValue = limited;
            so.FindProperty("unlockType").enumValueIndex = (int)unlock;
            so.FindProperty("unlockRequirement").intValue = unlockReq;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(c);
            return c;
        }

        static RarityVisualSO CreateRarityVisuals()
        {
            var path = $"{ResourcesRoot}/RarityVisuals.asset";
            var db = CreateOrLoad<RarityVisualSO>(path);
            var so = new SerializedObject(db);
            // Neon Cyberpunk rarity palette — each tier maps to a brand color
            // (Select=dim cool gray, Deluxe=cyan, Premium=purple, Exclusive=pink, Ultra=electric green)
            // (rarity, neonAccent, glowIntensity, deepCardBackground)
            // Card background is a rich dark tone that matches the neon accent hue.
            // Reference: Valorant-style full-card rarity coloring.
            var entries = new (SkinRarity rar, Color primary, float intensity, Color cardBg)[]
            {
                (SkinRarity.Select,    new Color(0.55f,  0.65f,  0.78f,  1f), 0.6f, new Color(0.22f, 0.30f, 0.45f, 1f)), // solid steel blue
                (SkinRarity.Deluxe,    new Color(0f,     0.961f, 1f,     1f), 0.9f, new Color(0.06f, 0.30f, 0.68f, 1f)), // vivid ocean blue
                (SkinRarity.Premium,   new Color(0.690f, 0.149f, 1f,     1f), 1.1f, new Color(0.35f, 0.08f, 0.65f, 1f)), // vivid violet purple
                (SkinRarity.Exclusive, new Color(1f,     0.176f, 0.667f, 1f), 1.3f, new Color(0.62f, 0.07f, 0.30f, 1f)), // vivid crimson rose
                (SkinRarity.Ultra,     new Color(0.224f, 1f,     0.078f, 1f), 1.6f, new Color(0.06f, 0.48f, 0.10f, 1f)), // vivid neon green
            };

            so.FindProperty("entries").arraySize = entries.Length;
            for (var i = 0; i < entries.Length; i++)
            {
                var e = so.FindProperty("entries").GetArrayElementAtIndex(i);
                e.FindPropertyRelative("rarity").enumValueIndex  = (int)entries[i].rar;
                e.FindPropertyRelative("primaryColor").colorValue = entries[i].primary;
                e.FindPropertyRelative("glowColor").colorValue    = entries[i].primary * 1.2f;
                e.FindPropertyRelative("textColor").colorValue    = Color.white;
                e.FindPropertyRelative("glowIntensity").floatValue = entries[i].intensity;
                e.FindPropertyRelative("cardBgColor").colorValue  = entries[i].cardBg;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            return db;
        }

        static T CreateOrLoad<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parts = path.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
#endif
