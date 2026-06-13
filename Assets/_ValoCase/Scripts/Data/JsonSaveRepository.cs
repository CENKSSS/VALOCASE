using System;
using System.IO;
using UnityEngine;
using ValoCase.Core;

namespace ValoCase.Save
{
    public sealed class JsonSaveRepository : ISaveRepository
    {
        readonly string _path;
        readonly string _tmpPath;
        readonly string _bakPath;

        public JsonSaveRepository()
        {
            _path = Path.Combine(Application.persistentDataPath, GameConstants.SaveFileName);
            _tmpPath = _path + ".tmp";
            _bakPath = _path + ".bak";
            Debug.Log($"[SAVE PATH] {_path}");
        }

        // A save is considered present if either the main file or the backup exists.
        public bool Exists() => File.Exists(_path) || File.Exists(_bakPath);

        public bool TryLoad(out SaveDataRoot data)
        {
            // Primary read; on failure/corruption fall back to the last good backup.
            if (TryReadFrom(_path, out data)) return true;

            if (File.Exists(_bakPath) && TryReadFrom(_bakPath, out data))
            {
                Debug.LogWarning("[Save] Main save unreadable — recovered from backup (.bak).");
                return true;
            }

            data = null;
            return false;
        }

        static bool TryReadFrom(string path, out SaveDataRoot data)
        {
            data = null;
            if (!File.Exists(path)) return false;

            try
            {
                var json = File.ReadAllText(path);
                data = JsonUtility.FromJson<SaveDataRoot>(json);
                return data != null;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Save] Load failed ({path}): {ex.Message}");
                data = null;
                return false;
            }
        }

        // Atomic write: serialize to a temp file, then atomically replace the main file
        // (keeping the previous good copy as .bak). Save FORMAT is unchanged — this only
        // hardens the write procedure against process-kill-mid-write corruption (Android).
        public void Save(SaveDataRoot data)
        {
            data.lastSaveUnix = TimeUtil.NowUnix();
            var json = JsonUtility.ToJson(data, true);

            try
            {
                File.WriteAllText(_tmpPath, json);

                if (File.Exists(_path))
                {
                    // Atomically swap temp → main, moving the old main to .bak.
                    if (File.Exists(_bakPath)) File.Delete(_bakPath);
                    File.Replace(_tmpPath, _path, _bakPath);
                }
                else
                {
                    File.Move(_tmpPath, _path);
                }
            }
            catch (Exception ex)
            {
                // Best-effort fallback: never lose the write because the atomic path
                // is unavailable on some filesystem. Format is identical either way.
                Debug.LogWarning($"[Save] Atomic write failed ({ex.Message}); writing directly.");
                try
                {
                    File.WriteAllText(_path, json);
                }
                catch (Exception ex2)
                {
                    Debug.LogError($"[Save] Direct write also failed: {ex2.Message}");
                }
                finally
                {
                    try { if (File.Exists(_tmpPath)) File.Delete(_tmpPath); } catch { /* ignore */ }
                }
            }
        }

        public void Delete()
        {
            try { if (File.Exists(_path)) File.Delete(_path); } catch { /* ignore */ }
            try { if (File.Exists(_bakPath)) File.Delete(_bakPath); } catch { /* ignore */ }
            try { if (File.Exists(_tmpPath)) File.Delete(_tmpPath); } catch { /* ignore */ }
        }
    }
}
