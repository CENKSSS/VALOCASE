using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ValoCase.EditorTools
{
    /// <summary>
    /// The whole Android release flow in one window: version, signing, build.
    ///
    /// Exists because the pieces used to live in three places — version in Player
    /// Settings, passwords typed per-session into Publishing Settings, output path in
    /// the Build dialog — and two release mistakes on 2026-08-17 came from settings
    /// silently differing between builds. The window therefore FORCES the two that
    /// went wrong: texture subtarget Generic (a stray PVRTC value put
    /// supports-gl-texture into the manifest and cost 9,703 Play devices) and LZ4
    /// compression (a raw build shipped 43 MB heavier).
    ///
    /// Passwords are session-only on purpose: SessionState survives a domain reload
    /// but dies with the editor and never reaches disk or git — same lifetime Unity
    /// itself gives keystore passwords, minus the retyping after every recompile.
    /// </summary>
    public sealed class ValoCaseReleaseWindow : EditorWindow
    {
        const string KeystorePassKey = "ValoCase.Release.KeystorePass";
        const string KeyaliasPassKey = "ValoCase.Release.KeyaliasPass";

        string _version;
        int    _versionCode;
        string _keystorePass;
        string _keyaliasPass;
        bool   _cleanBuild;

        [MenuItem("ValoCase/Release Build...", false, 0)]
        public static void Open()
        {
            var window = GetWindow<ValoCaseReleaseWindow>("Release Build");
            window.minSize = new Vector2(460f, 400f);
        }

        void OnEnable()
        {
            _version      = PlayerSettings.bundleVersion;
            _versionCode  = PlayerSettings.Android.bundleVersionCode;
            _keystorePass = SessionState.GetString(KeystorePassKey, string.Empty);
            _keyaliasPass = SessionState.GetString(KeyaliasPassKey, string.Empty);
        }

        void OnGUI()
        {
            EditorGUILayout.Space(8f);

            EditorGUILayout.LabelField("Sürüm", EditorStyles.boldLabel);
            _version     = EditorGUILayout.TextField(new GUIContent("Version",
                               "PlayerSettings.bundleVersion — store'da görünen ad (örn. 1.0.29)"), _version);
            _versionCode = EditorGUILayout.IntField(new GUIContent("Bundle Version Code",
                               "Play'de her yükleme için bir öncekinden BÜYÜK olmalı; kullanılan kod geri gelmez"), _versionCode);

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("İmza", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField("Keystore", PlayerSettings.Android.keystoreName);
                EditorGUILayout.TextField("Key Alias", PlayerSettings.Android.keyaliasName);
            }
            _keystorePass = EditorGUILayout.PasswordField("Keystore Password", _keystorePass);
            _keyaliasPass = EditorGUILayout.PasswordField("Key Password", _keyaliasPass);
            SessionState.SetString(KeystorePassKey, _keystorePass);
            SessionState.SetString(KeyaliasPassKey, _keyaliasPass);
            EditorGUILayout.HelpBox("Şifreler diske yazılmaz; editör kapanana kadar hatırlanır.", MessageType.None);

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("Build", EditorStyles.boldLabel);
            _cleanBuild = EditorGUILayout.ToggleLeft(new GUIContent("Temiz build (yavaş — mağaza yüklemesi öncesi önerilir)",
                              "CleanBuildCache: her şeyi sıfırdan derler"), _cleanBuild);
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.TextField("Çıktı", DefaultOutputPath());
            EditorGUILayout.HelpBox("Her build'de otomatik: AAB + LZ4 sıkıştırma + doku ayarı Generic " +
                                    "(PVRTC/9.703 cihaz kazasının tekrar koruması).", MessageType.Info);

            EditorGUILayout.Space(12f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Sadece Ayarları Kaydet", GUILayout.Height(30f)))
                    ApplySettings(logIt: true);

                GUI.backgroundColor = new Color(1f, 0.35f, 0.4f);
                if (GUILayout.Button("BUILD (.aab)", GUILayout.Height(30f)))
                {
                    GUI.backgroundColor = Color.white;
                    if (Validate()) Build();
                    GUIUtility.ExitGUI();
                }
                GUI.backgroundColor = Color.white;
            }
        }

        string DefaultOutputPath()
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "ValoCaseBuild");
            return Path.Combine(dir, $"ValoCase_{(_version ?? string.Empty).Trim()}.aab").Replace('\\', '/');
        }

        bool Validate()
        {
            if (string.IsNullOrWhiteSpace(_version) || _versionCode <= 0)
            {
                EditorUtility.DisplayDialog("Release Build", "Version boş olamaz, Bundle Version Code 0'dan büyük olmalı.", "Tamam");
                return false;
            }
            if (!File.Exists(PlayerSettings.Android.keystoreName))
            {
                EditorUtility.DisplayDialog("Release Build",
                    "Keystore dosyası bulunamadı:\n" + PlayerSettings.Android.keystoreName, "Tamam");
                return false;
            }
            if (string.IsNullOrEmpty(_keystorePass) || string.IsNullOrEmpty(_keyaliasPass))
                return EditorUtility.DisplayDialog("Release Build",
                    "Şifre alanı boş — paket imzasız çıkabilir ve Play kabul etmez. Yine de devam?",
                    "Devam", "Vazgeç");
            return true;
        }

        void ApplySettings(bool logIt)
        {
            PlayerSettings.bundleVersion             = _version.Trim();
            PlayerSettings.Android.bundleVersionCode = _versionCode;
            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystorePass      = _keystorePass;
            PlayerSettings.Android.keyaliasPass      = _keyaliasPass;

            EditorUserBuildSettings.buildAppBundle        = true;
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.Generic;

            AssetDatabase.SaveAssets();
            if (logIt)
                Debug.Log($"[Release] version={PlayerSettings.bundleVersion} " +
                          $"code={PlayerSettings.Android.bundleVersionCode} " +
                          $"subtarget={EditorUserBuildSettings.androidBuildSubtarget} aab=true");
        }

        void Build()
        {
            ApplySettings(logIt: false);

            var output = DefaultOutputPath();
            Directory.CreateDirectory(Path.GetDirectoryName(output));

            var options = BuildOptions.CompressWithLz4;
            if (_cleanBuild) options |= BuildOptions.CleanBuildCache;

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes           = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = output,
                target           = BuildTarget.Android,
                options          = options
            });

            var summary = report.summary;
            if (summary.result == BuildResult.Succeeded)
            {
                var sizeMb = new FileInfo(output).Length / 1048576f;
                Debug.Log($"[Release] Build OK — {output} ({sizeMb:0.0} MB, {summary.totalTime.TotalMinutes:0.0} dk)");
                EditorUtility.RevealInFinder(output);
            }
            else
            {
                Debug.LogError($"[Release] Build {summary.result} — errors={summary.totalErrors}. Console'a bak.");
                EditorUtility.DisplayDialog("Release Build",
                    $"Build sonucu: {summary.result}. Ayrıntılar Console'da.", "Tamam");
            }
        }
    }
}
