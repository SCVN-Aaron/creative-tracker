using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CreativeRecorder
{
    /// <summary>
    /// Tab "Tuỳ chỉnh": gom MỌI tuỳ chọn trong scene (và tuỳ chọn: prefab, ScriptableObject) vào một chỗ,
    /// xếp theo loại — âm thanh, skin & hình ảnh, thông số, bật/tắt, màu, phím, prefab, chữ — để sửa trực tiếp,
    /// kèm "Thay tất cả" một âm thanh / material / sprite / prefab ở mọi nơi dùng nó.
    /// </summary>
    public partial class CreativeRecorderWindow
    {
        enum OptKind { Sound, Visual, Number, Toggle, Color, Key, Choice, Prefab, Text, Reference, Other }

        static readonly string[] OptKindNames =
        {
            "Âm thanh", "Skin & hình ảnh (material, sprite, texture, mesh)", "Thông số (số: tốc độ, máu, thời gian…)", "Bật / tắt",
            "Màu", "Phím", "Lựa chọn (enum)", "Prefab (object được tạo ra / dùng làm mẫu)", "Chữ", "Tham chiếu object trong scene", "Khác (nhóm, danh sách…)"
        };

        /// <summary>Thuộc tính đáng chỉnh của component có sẵn của Unity (không phải script game).</summary>
        static readonly Dictionary<string, string[]> UnityProps = new Dictionary<string, string[]>
        {
            { "AudioSource", new[] { "m_audioClip", "m_Volume", "m_Pitch", "Loop", "m_Mute", "m_PlayOnAwake" } },
            { "SpriteRenderer", new[] { "m_Sprite", "m_Color", "m_Materials" } },
            { "MeshRenderer", new[] { "m_Materials" } },
            { "SkinnedMeshRenderer", new[] { "m_Mesh", "m_Materials" } },
            { "MeshFilter", new[] { "m_Mesh" } },
            { "Image", new[] { "m_Sprite", "m_Color" } },
            { "RawImage", new[] { "m_Texture", "m_Color" } },
            { "Text", new[] { "m_Text", "m_Color" } },
            { "TMP_Text", new[] { "m_text", "m_fontColor", "m_fontSize" } },
            { "Camera", new[] { "field of view", "orthographic size", "m_BackGroundColor" } },
            { "Light", new[] { "m_Color", "m_Intensity" } },
        };

        class OptEntry
        {
            public Object target;
            public GameObject go;
            public string prop, owner, script;
            public OptKind kind;
            public bool asset;
        }

        const int OptionsTab = 1;
        const int MaxPerGroup = 150;

        List<OptEntry> opts;
        Dictionary<Object, SerializedObject> soCache;
        bool optPrefabs, optAssets = true, optUnity = true;
        int optGroup;
        string optFilter = "", optScript = "";
        readonly Dictionary<string, bool> optFold = new Dictionary<string, bool>();
        readonly Dictionary<OptKind, Object> bulkFrom = new Dictionary<OptKind, Object>();
        readonly Dictionary<OptKind, Object> bulkTo = new Dictionary<OptKind, Object>();

        void DrawOptions()
        {
            EditorGUILayout.BeginHorizontal();
            optUnity = EditorGUILayout.ToggleLeft("Component Unity", optUnity, GUILayout.Width(120));
            optAssets = EditorGUILayout.ToggleLeft("ScriptableObject", optAssets, GUILayout.Width(115));
            optPrefabs = EditorGUILayout.ToggleLeft("Prefab", optPrefabs, GUILayout.Width(65));
            EditorGUILayout.EndHorizontal();
            if (GUILayout.Button(opts == null ? "Quét mọi tuỳ chọn" : "Quét lại", GUILayout.Height(26))) ScanOptions();
            if (opts == null)
            {
                EditorGUILayout.HelpBox("Gom mọi tuỳ chọn của các script game trong scene đang mở (và AudioSource, Renderer, Image, Text, Camera, Light) " +
                                        "vào một danh sách, xếp theo loại để sửa thẳng: thay âm thanh, thay skin / material / sprite, đổi tốc độ, máu, " +
                                        "thời gian, bật / tắt tính năng, đổi màu, đổi phím, đổi chữ…\n" +
                                        "ScriptableObject = file cấu hình của game. Prefab = object mẫu (địch, item…) được tạo ra khi chơi.", MessageType.None);
                return;
            }
            opts.RemoveAll(e => e.target == null);
            optGroup = GUILayout.Toolbar(optGroup, new[] { "Theo loại", "Theo script", "Theo object" });
            optFilter = EditorGUILayout.TextField("Lọc", optFilter);
            if (!string.IsNullOrEmpty(optScript))
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("Chỉ script: " + optScript, EditorStyles.boldLabel);
                if (GUILayout.Button("Bỏ lọc", EditorStyles.miniButton, GUILayout.Width(60))) optScript = "";
                EditorGUILayout.EndHorizontal();
            }
            if (opts.Any(e => e.asset))
                EditorGUILayout.HelpBox("Mục ghi “asset” là prefab / ScriptableObject: sửa là lưu thẳng vào file, kể cả khi đang Play (Ctrl+Z để hoàn tác).", MessageType.Warning);

            var list = opts.Where(Matches).ToList();
            GUILayout.Label(list.Count + " tuỳ chọn", EditorStyles.miniLabel);
            if (optGroup == 0)
            {
                foreach (OptKind k in Enum.GetValues(typeof(OptKind)))
                {
                    var items = list.Where(e => e.kind == k).ToList();
                    if (items.Count == 0) continue;
                    if (!Fold("k" + k, OptKindNames[(int)k] + "  (" + items.Count + ")", k == OptKind.Sound || k == OptKind.Visual)) continue;
                    if (k == OptKind.Sound || k == OptKind.Visual || k == OptKind.Prefab) DrawBulk(k, items);
                    DrawEntries(items, true, true);
                }
            }
            else
            {
                var groups = list.GroupBy(e => optGroup == 1 ? e.script : e.owner).OrderBy(g => g.Key);
                foreach (var g in groups)
                {
                    if (!Fold((optGroup == 1 ? "s" : "o") + g.Key, g.Key + "  (" + g.Count() + ")", !string.IsNullOrEmpty(optScript))) continue;
                    DrawEntries(g.OrderBy(e => e.kind).ToList(), optGroup == 1, optGroup == 2);
                }
            }
        }

        bool Matches(OptEntry e)
        {
            if (!string.IsNullOrEmpty(optScript) && e.script != optScript) return false;
            if (string.IsNullOrEmpty(optFilter)) return true;
            return (e.owner + " " + e.script + " " + e.prop).IndexOf(optFilter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        bool Fold(string key, string label, bool dflt)
        {
            bool open;
            if (!optFold.TryGetValue(key, out open)) open = dflt;
            open = EditorGUILayout.Foldout(open, label, true, EditorStyles.foldoutHeader);
            optFold[key] = open;
            return open;
        }

        void DrawEntries(List<OptEntry> items, bool showOwner, bool showScript)
        {
            foreach (var e in items.Take(MaxPerGroup)) DrawEntry(e, showOwner, showScript);
            if (items.Count > MaxPerGroup)
                EditorGUILayout.HelpBox("Còn " + (items.Count - MaxPerGroup) + " mục — gõ vào ô Lọc để thu hẹp.", MessageType.None);
        }

        void DrawEntry(OptEntry e, bool showOwner, bool showScript)
        {
            SerializedObject so;
            if (!soCache.TryGetValue(e.target, out so) || so.targetObject == null) return;
            so.Update();
            var p = so.FindProperty(e.prop);
            if (p == null) return;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            string head = (showOwner ? e.owner : "") + (showOwner && showScript ? "  ·  " : "") + (showScript ? e.script : "") + (e.asset ? "  ·  asset" : "");
            if (head.Length > 0 && GUILayout.Button(head, EditorStyles.miniLabel))
            {
                if (e.go != null) { Selection.activeGameObject = e.go; EditorGUIUtility.PingObject(e.go); }
                else { Selection.activeObject = e.target; EditorGUIUtility.PingObject(e.target); }
            }
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(p, new GUIContent(p.displayName), true);
            var clip = p.propertyType == SerializedPropertyType.ObjectReference ? p.objectReferenceValue as AudioClip : null;
            if (clip != null && GUILayout.Button("▶", GUILayout.Width(24))) PreviewClip(clip);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
            if (so.ApplyModifiedProperties() && !e.asset) MarkDirty(e.go);
        }

        void DrawBulk(OptKind k, List<OptEntry> items)
        {
            Type t = k == OptKind.Sound ? typeof(AudioClip) : k == OptKind.Prefab ? typeof(GameObject) : typeof(Object);
            Object from, to;
            bulkFrom.TryGetValue(k, out from);
            bulkTo.TryGetValue(k, out to);
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            GUILayout.Label("Thay tất cả", GUILayout.Width(70));
            from = EditorGUILayout.ObjectField(from, t, false);
            GUILayout.Label("→", GUILayout.Width(16));
            to = EditorGUILayout.ObjectField(to, t, false);
            GUI.enabled = from != null && to != null;
            if (GUILayout.Button("Thay", GUILayout.Width(48)))
            {
                if (from.GetType() != to.GetType())
                    ShowNotification(new GUIContent("Hai asset phải cùng loại (" + from.GetType().Name + ")"));
                else
                    ShowNotification(new GUIContent("Đã thay " + ReplaceAll(items, from, to) + " chỗ"));
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
            bulkFrom[k] = from;
            bulkTo[k] = to;
        }

        int ReplaceAll(List<OptEntry> items, Object from, Object to)
        {
            int n = 0;
            foreach (var e in items)
            {
                SerializedObject so;
                if (!soCache.TryGetValue(e.target, out so) || so.targetObject == null) continue;
                so.Update();
                var p = so.FindProperty(e.prop);
                if (p == null) continue;
                int before = n;
                if (p.propertyType == SerializedPropertyType.ObjectReference)
                {
                    if (p.objectReferenceValue == from) { p.objectReferenceValue = to; n++; }
                }
                else if (p.isArray)
                {
                    for (int i = 0; i < p.arraySize; i++)
                    {
                        var el = p.GetArrayElementAtIndex(i);
                        if (el.propertyType == SerializedPropertyType.ObjectReference && el.objectReferenceValue == from) { el.objectReferenceValue = to; n++; }
                    }
                }
                if (n > before && so.ApplyModifiedProperties() && !e.asset) MarkDirty(e.go);
            }
            return n;
        }

        void ScanOptions()
        {
            opts = new List<OptEntry>();
            soCache = new Dictionary<Object, SerializedObject>();
            foreach (var go in SceneObjects())
                foreach (var c in go.GetComponents<Component>())
                    AddComponentOptions(c, PathOf(go.transform), false);
            if (optPrefabs)
            {
                foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (path.Contains("/CreativeRecorder/") || IsVendor(path)) continue;
                    var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (root == null) continue;
                    foreach (var c in root.GetComponentsInChildren<Component>(true))
                        if (c != null && (IsGameType(c.GetType()) || c is AudioSource))
                            AddComponentOptions(c, "Prefab " + PathOf(c.transform), true);
                }
            }
            if (optAssets)
            {
                foreach (var guid in AssetDatabase.FindAssets("t:ScriptableObject", new[] { "Assets" }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (path.Contains("/CreativeRecorder/") || path.Contains("/Editor/") || IsVendor(path)) continue;
                    foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                    {
                        var so = o as ScriptableObject;
                        if (so != null && IsGameType(so.GetType()))
                            AddProps(so, null, Path.GetFileName(path), so.GetType().Name, true, null);
                    }
                }
            }
        }

        void AddComponentOptions(Component c, string owner, bool asset)
        {
            if (c == null || c is Transform) return;
            if (IsGameType(c.GetType()))
            {
                AddProps(c, c.gameObject, owner, c.GetType().Name, asset, null);
                return;
            }
            if (!optUnity && !asset) return;
            for (var t = c.GetType(); t != null && t != typeof(Component); t = t.BaseType)
            {
                string[] props;
                if (UnityProps.TryGetValue(t.Name, out props))
                {
                    AddProps(c, c.gameObject, owner, c.GetType().Name, asset, props);
                    return;
                }
            }
        }

        void AddProps(Object target, GameObject go, string owner, string script, bool asset, string[] only)
        {
            var so = new SerializedObject(target);
            int before = opts.Count;
            if (only != null)
            {
                foreach (var name in only)
                {
                    var p = so.FindProperty(name);
                    if (p != null) AddEntry(p, target, go, owner, script, asset);
                }
            }
            else
            {
                var it = so.GetIterator();
                bool enter = true;
                while (it.NextVisible(enter))
                {
                    enter = false;
                    if (it.name == "m_Script") continue;
                    AddEntry(it, target, go, owner, script, asset);
                }
            }
            if (opts.Count > before) soCache[target] = so;
        }

        void AddEntry(SerializedProperty p, Object target, GameObject go, string owner, string script, bool asset)
        {
            opts.Add(new OptEntry { target = target, go = go, prop = p.propertyPath, owner = owner, script = script, kind = KindOf(p), asset = asset });
        }

        static OptKind KindOf(SerializedProperty p)
        {
            bool list = p.isArray && p.propertyType != SerializedPropertyType.String;
            string t = list ? p.arrayElementType : p.type;
            var m = Regex.Match(t ?? "", @"PPtr<\$?(\w+)>");
            if (m.Success)
            {
                switch (m.Groups[1].Value)
                {
                    case "AudioClip": return OptKind.Sound;
                    case "Material": case "Sprite": case "Texture": case "Texture2D": case "Mesh": case "RenderTexture": case "Cubemap": case "Font": case "TMP_FontAsset":
                        return OptKind.Visual;
                }
                return RefersToAsset(p, list) ? OptKind.Prefab : OptKind.Reference;
            }
            if (list)
            {
                switch (t)
                {
                    case "float": case "int": return OptKind.Number;
                    case "bool": return OptKind.Toggle;
                    case "string": return OptKind.Text;
                    case "ColorRGBA": case "Color": return OptKind.Color;
                }
                return OptKind.Other;
            }
            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.Float:
                case SerializedPropertyType.Vector2:
                case SerializedPropertyType.Vector3:
                case SerializedPropertyType.Vector4:
                case SerializedPropertyType.Vector2Int:
                case SerializedPropertyType.Vector3Int:
                    return OptKind.Number;
                case SerializedPropertyType.Boolean: return OptKind.Toggle;
                case SerializedPropertyType.Color: return OptKind.Color;
                case SerializedPropertyType.String: return OptKind.Text;
                case SerializedPropertyType.Enum:
                    return p.enumNames.Contains("Mouse0") && p.enumNames.Contains("Backspace") ? OptKind.Key : OptKind.Choice;
            }
            return OptKind.Other;
        }

        static bool RefersToAsset(SerializedProperty p, bool list)
        {
            if (!list) return p.objectReferenceValue != null && EditorUtility.IsPersistent(p.objectReferenceValue);
            for (int i = 0; i < p.arraySize && i < 8; i++)
            {
                var o = p.GetArrayElementAtIndex(i).objectReferenceValue;
                if (o != null) return EditorUtility.IsPersistent(o);
            }
            return false;
        }

        /// <summary>Script của game (không phải của Unity, package hay tool này).</summary>
        static bool IsGameType(Type t)
        {
            if (!typeof(MonoBehaviour).IsAssignableFrom(t) && !typeof(ScriptableObject).IsAssignableFrom(t)) return false;
            string ns = t.Namespace ?? "";
            if (ns.StartsWith("UnityEngine") || ns.StartsWith("UnityEditor") || ns.StartsWith("Unity.") || ns.StartsWith("TMPro") ||
                ns.StartsWith("CreativeRecorder") || ns.StartsWith("DG.") || ns.StartsWith("Cinemachine")) return false;
            string asm = t.Assembly.GetName().Name;
            return !asm.StartsWith("Unity") && !asm.StartsWith("UnityEngine");
        }
    }
}
