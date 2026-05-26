using System.IO;
using UnityEngine;
using ValoCase.Core;

namespace ValoCase.Save
{
    public sealed class JsonSaveRepository : ISaveRepository
    {
        readonly string _path;

        public JsonSaveRepository()
        {
            _path = Path.Combine(Application.persistentDataPath, GameConstants.SaveFileName);
        }

        public bool Exists() => File.Exists(_path);

        public bool TryLoad(out SaveDataRoot data)
        {
            if (!Exists())
            {
                data = null;
                return false;
            }

            try
            {
                var json = File.ReadAllText(_path);
                data = JsonUtility.FromJson<SaveDataRoot>(json);
                return data != null;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[Save] Load failed: {ex.Message}");
                data = null;
                return false;
            }
        }

        public void Save(SaveDataRoot data)
        {
            data.lastSaveUnix = TimeUtil.NowUnix();
            var json = JsonUtility.ToJson(data, true);
            File.WriteAllText(_path, json);
        }

        public void Delete()
        {
            if (Exists()) File.Delete(_path);
        }
    }
}
