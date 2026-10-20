using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CreativeRecorder
{
    /// <summary>
    /// Đọc / ghi Bộ chỉnh ở ProjectSettings/CreativeRecorderPreset.json (ngoài Assets nên không vào bản build)
    /// và tự tạo CreativeDirector mỗi khi bấm Play.
    /// </summary>
    [InitializeOnLoad]
    public static class CreativePresetStore
    {
        public const string FilePath = "ProjectSettings/CreativeRecorderPreset.json";
        static CreativePreset current;

        static CreativePresetStore()
        {
            EditorApplication.playModeStateChanged += s =>
            {
                if (s != PlayModeStateChange.EnteredPlayMode) return;
                Load();
                if (current.enabled && current.HasAny && CreativeDirector.Instance == null) CreativeDirector.Create(current);
            };
        }

        public static CreativePreset Current
        {
            get
            {
                if (current == null) Load();
                return current;
            }
        }

        public static void Load()
        {
            current = null;
            if (File.Exists(FilePath))
            {
                try { current = JsonUtility.FromJson<CreativePreset>(File.ReadAllText(FilePath)); }
                catch (System.Exception e) { Debug.LogWarning("Creative Recorder: không đọc được Bộ chỉnh — " + e.Message); }
            }
            if (current == null) current = new CreativePreset();
            Resolve(current);
        }

        /// <summary>Lưu file và đẩy ngay vào game nếu đang Play.</summary>
        public static void Save()
        {
            File.WriteAllText(FilePath, JsonUtility.ToJson(Current, true));
            if (!Application.isPlaying) return;
            var d = CreativeDirector.Instance;
            if (d == null) { if (current.enabled && current.HasAny) CreativeDirector.Create(current); }
            else if (d.preset != current) d.SetPreset(current);
        }

        public static void ExportTo(string file)
        {
            File.WriteAllText(file, JsonUtility.ToJson(Current, true));
        }

        public static void ImportFrom(string file)
        {
            File.Copy(file, FilePath, true);
            Load();
            Save();
        }

        public static void Clear()
        {
            current = new CreativePreset();
            Save();
            if (Application.isPlaying && CreativeDirector.Instance != null) CreativeDirector.Instance.SetPreset(current);
        }

        static void Resolve(CreativePreset p)
        {
            foreach (var s in p.skins) s.assetObj = Find(s.asset);
            foreach (var c in p.clipSwaps)
            {
                c.fromClip = Find(c.from) as AudioClip;
                c.toClip = Find(c.to) as AudioClip;
            }
            foreach (var r in p.sfx) r.clipObj = Find(r.clip) as AudioClip;
        }

        /// <summary>Mã asset ổn định: "guid:localId" (giữ được sprite con trong atlas, mesh trong FBX).</summary>
        public static string Ref(Object o)
        {
            string guid;
            long id;
            if (o != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o, out guid, out id)) return guid + ":" + id;
            return "";
        }

        public static Object Find(string r)
        {
            if (string.IsNullOrEmpty(r)) return null;
            int colon = r.IndexOf(':');
            string guid = colon > 0 ? r.Substring(0, colon) : r;
            long id = 0;
            if (colon > 0) long.TryParse(r.Substring(colon + 1), out id);
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) return null;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                string g;
                long l;
                if (o != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o, out g, out l) && l == id) return o;
            }
            return AssetDatabase.LoadMainAssetAtPath(path);
        }
    }
}
