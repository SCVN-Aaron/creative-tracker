using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CreativeRecorder
{
    /// <summary>
    /// Chế độ đơn giản cho người không biết code: 4 màn hình Skin · Cheat · Âm thanh · Quay.
    /// Mọi thay đổi ghi vào Bộ chỉnh (CreativePresetStore) và chỉ áp dụng khi Play — game gốc không bị sửa.
    /// </summary>
    public partial class CreativeRecorderWindow
    {
        enum Page { Home, Skin, Cheat, Sound, Camera, Unlock, Record }

        const string PrefAdvanced = "CreativeRecorder.advanced";
        Page page = Page.Home;
        bool advanced;

        static GUIStyle stTitle, stH2, stCard, stMuted, stNav, stBig;
        static readonly Color Green = new Color(0.36f, 0.82f, 0.46f);
        static readonly Color Blue = new Color(0.35f, 0.62f, 1f);
        static readonly Color Red = new Color(1f, 0.45f, 0.42f);
        static readonly Dictionary<string, Texture> iconCache = new Dictionary<string, Texture>();

        static CreativePreset P { get { return CreativePresetStore.Current; } }

        void OnEnable()
        {
            advanced = EditorPrefs.GetBool(PrefAdvanced, false);
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            SceneView.duringSceneGui += OnSceneGUI;
        }

        void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            SceneView.duringSceneGui -= OnSceneGUI;
            Tools.hidden = false;
        }

        void OnInspectorUpdate()
        {
            if (Application.isPlaying && !advanced) Repaint();
        }

        void OnPlayModeChanged(PlayModeStateChange s)
        {
            if (s == PlayModeStateChange.ExitingPlayMode) CreativeMultiRecorder.Stop();
            // Object trong scene đổi khi vào / ra Play → quét lại
            skinItems = null;
            cheatRows = null;
            events = null;
            clipUses = null;
            uiItems = null;
            nodes = null;
            moving = null;
            Repaint();
        }

        void SetAdvanced(bool on)
        {
            advanced = on;
            EditorPrefs.SetBool(PrefAdvanced, on);
        }

        // ================================================================== khung

        static void Styles()
        {
            if (stTitle != null) return;
            stTitle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 17 };
            stH2 = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
            stCard = new GUIStyle(EditorStyles.helpBox) { padding = new RectOffset(10, 10, 8, 8), margin = new RectOffset(2, 2, 3, 5) };
            stMuted = new GUIStyle(EditorStyles.wordWrappedMiniLabel);
            stMuted.normal.textColor = EditorGUIUtility.isProSkin ? new Color(0.62f, 0.62f, 0.62f) : new Color(0.38f, 0.38f, 0.38f);
            stNav = new GUIStyle(GUI.skin.button) { fontSize = 12, fixedHeight = 36, fontStyle = FontStyle.Bold };
            stBig = new GUIStyle(GUI.skin.button) { fontSize = 12, fixedHeight = 40, fontStyle = FontStyle.Bold, wordWrap = true };
        }

        static Texture Icon(params string[] names)
        {
            string key = names[0];
            Texture t;
            if (iconCache.TryGetValue(key, out t)) return t;
            foreach (var n in names)
            {
                t = EditorGUIUtility.FindTexture(n);
                if (t != null) break;
            }
            iconCache[key] = t;
            return t;
        }

        static bool Tinted(string text, Color c, GUIStyle style, params GUILayoutOption[] o)
        {
            var old = GUI.backgroundColor;
            GUI.backgroundColor = c;
            bool r = GUILayout.Button(text, style, o);
            GUI.backgroundColor = old;
            return r;
        }

        static void ColorLabel(string text, Color c, GUIStyle s)
        {
            var old = GUI.contentColor;
            GUI.contentColor = c;
            GUILayout.Label(text, s);
            GUI.contentColor = old;
        }

        static Texture Thumb(Object o)
        {
            if (o == null) return null;
            return AssetPreview.GetAssetPreview(o) ?? AssetPreview.GetMiniThumbnail(o);
        }

        void DrawStudio()
        {
            Styles();
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Creative Recorder", stTitle);
            GUILayout.FlexibleSpace();
            if (Application.isPlaying)
            {
                if (Tinted("■  Dừng", Red, stNav, GUILayout.Width(90))) EditorApplication.isPlaying = false;
            }
            else if (Tinted("▶  Play", Green, stNav, GUILayout.Width(90))) EditorApplication.isPlaying = true;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            NavButton(Page.Skin, "Skin", Icon("Material Icon", "d_Material Icon"));
            NavButton(Page.Cheat, "Cheat", Icon("d_Settings", "Settings", "SettingsIcon"));
            NavButton(Page.Sound, "Âm thanh", Icon("AudioSource Icon", "d_AudioSource Icon"));
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            NavButton(Page.Camera, "Camera", Icon("Camera Icon", "d_Camera Icon"));
            NavButton(Page.Unlock, "Unlock", Icon("d_MoveTool", "MoveTool"));
            NavButton(Page.Record, "Quay", Icon("Animation.Record", "d_Animation.Record"));
            EditorGUILayout.EndHorizontal();

            DrawPresetBar();

            scroll = EditorGUILayout.BeginScrollView(scroll);
            switch (page)
            {
                case Page.Home: DrawHome(); break;
                case Page.Skin: DrawSkinPage(); break;
                case Page.Cheat: DrawCheatPage(); break;
                case Page.Sound: DrawSoundPage(); break;
                case Page.Camera: DrawCameraPage(); break;
                case Page.Unlock: DrawUnlockPage(); break;
                case Page.Record: DrawRecordPage(); break;
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.BeginHorizontal();
            if (page != Page.Home && GUILayout.Button("← Trang đầu", EditorStyles.miniButton, GUILayout.Width(90))) page = Page.Home;
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Chế độ nâng cao →", EditorStyles.miniButton, GUILayout.Width(120))) SetAdvanced(true);
            EditorGUILayout.EndHorizontal();
        }

        void NavButton(Page p, string text, Texture icon)
        {
            var old = GUI.backgroundColor;
            if (page == p) GUI.backgroundColor = Blue;
            if (GUILayout.Button(new GUIContent(" " + text, icon), stNav)) page = p;
            GUI.backgroundColor = old;
        }

        void DrawPresetBar()
        {
            EditorGUILayout.BeginHorizontal(stCard);
            bool en = EditorGUILayout.Toggle(P.enabled, GUILayout.Width(16));
            if (en != P.enabled) { P.enabled = en; CreativePresetStore.Save(); }
            GUILayout.Label(string.Format("Bộ chỉnh:  {0} skin  ·  {1} cheat  ·  {2} âm thanh", P.skins.Count, P.cheats.Count, P.ActiveSfx + P.clipSwaps.Count),
                            EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Khác ▾", EditorStyles.miniButton, GUILayout.Width(60))) PresetMenu();
            EditorGUILayout.EndHorizontal();
            GUILayout.Label(!P.enabled ? "Bộ chỉnh đang tắt — game chạy như gốc." :
                            Application.isPlaying ? "Đang Play — thay đổi áp dụng ngay. Dừng Play là game trở lại như cũ, Bộ chỉnh vẫn được giữ." :
                            "Tool không sửa game gốc: thay đổi được lưu vào Bộ chỉnh và tự áp dụng mỗi khi bấm Play.", stMuted);
        }

        void PresetMenu()
        {
            var m = new GenericMenu();
            m.AddItem(new GUIContent("Lưu Bộ chỉnh ra file…"), false, () =>
            {
                string f = EditorUtility.SaveFilePanel("Lưu Bộ chỉnh", "", Application.productName + "-creative.json", "json");
                if (!string.IsNullOrEmpty(f)) CreativePresetStore.ExportTo(f);
            });
            m.AddItem(new GUIContent("Mở Bộ chỉnh từ file…"), false, () =>
            {
                string f = EditorUtility.OpenFilePanel("Mở Bộ chỉnh", "", "json");
                if (!string.IsNullOrEmpty(f)) CreativePresetStore.ImportFrom(f);
            });
            m.AddSeparator("");
            m.AddItem(new GUIContent("Xoá hết Bộ chỉnh"), false, () =>
            {
                if (EditorUtility.DisplayDialog("Xoá hết Bộ chỉnh?", "Mọi skin, cheat, âm thanh đã chọn sẽ bị xoá.", "Xoá", "Huỷ"))
                    CreativePresetStore.Clear();
            });
            m.ShowAsContext();
        }

        void DrawHome()
        {
            GUILayout.Space(4);
            HomeCard(Page.Skin, "Đổi skin", "Đổi material, texture, sprite, mesh của nhân vật, môi trường, đồ vật, UI — kéo thả là xong. Có sẵn bộ skin của game thì bấm chọn.");
            HomeCard(Page.Cheat, "Cheat thông số", "Chỉnh tiền, điểm, tốc độ, máu, thời gian, số lượng… như menu cheat. Bấm Khoá để giữ nguyên giá trị (tiền không bao giờ hết).");
            HomeCard(Page.Sound, "Âm thanh & SFX", "Tool tự tìm hành động trong game: nhận tiền, stack, ăn điểm, combo, va chạm, nhặt đồ, popup thắng/thua. Chọn âm thanh cho từng hành động là xong.");
            HomeCard(Page.Camera, "Camera", "Zoom ×2 ×3 ×4, xoay quanh, đảo ngược 180°, góc thấp / từ trên xuống, góc cố định lấy từ Scene view, bám theo nhân vật.");
            HomeCard(Page.Unlock, "Unlock Node", "Ô mở khoá trả tiền để xây dựng / nâng cấp / mở rộng: xem giá, object đi kèm, kéo node và mọi thứ đi kèm sang chỗ khác bằng một tay cầm.");
            HomeCard(Page.Record, "Quay", "Quay cùng lúc nhiều khung (dọc, ngang, vuông — tự chỉnh kích thước), ẩn joystick (vẫn điều khiển được), ẩn tay hướng dẫn, slow-mo, chụp ảnh.");
            GUILayout.Space(6);
            GUILayout.Label("Cách dùng: chọn mục → chỉnh → bấm ▶ Play rồi quay. Lần sau mở lại project, Bộ chỉnh vẫn còn.", stMuted);
        }

        void HomeCard(Page p, string title, string desc)
        {
            EditorGUILayout.BeginVertical(stCard);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(title, stH2);
            GUILayout.FlexibleSpace();
            if (Tinted("Mở", Blue, EditorStyles.miniButton, GUILayout.Width(60))) page = p;
            EditorGUILayout.EndHorizontal();
            GUILayout.Label(desc, stMuted);
            EditorGUILayout.EndVertical();
        }

        static string Words(string s) { return SplitWords(s.Replace("/", " / ")); }
        static string[] Tokens(string s) { return Words(s).Split(new[] { ' ', '/' }, StringSplitOptions.RemoveEmptyEntries); }

        /// <summary>Tên biến gọn: bỏ m_, _, &lt;X&gt;k__BackingField.</summary>
        static string CleanName(string n)
        {
            if (n.StartsWith("<"))
            {
                int e = n.IndexOf('>');
                if (e < 0 || !n.Contains("k__BackingField")) return null;
                n = n.Substring(1, e - 1);
            }
            if (n.StartsWith("m_")) n = n.Substring(2);
            return n.TrimStart('_');
        }

        static IEnumerable<FieldInfo> GameFields(Type t)
        {
            for (var x = t; x != null && x != typeof(MonoBehaviour) && x != typeof(ScriptableObject) && x != typeof(object); x = x.BaseType)
                foreach (var f in x.GetFields(CreativeReflect.AllFields))
                    yield return f;
        }

        static IEnumerable<KeyValuePair<GameObject, MonoBehaviour>> GameComponents()
        {
            foreach (var go in SceneObjects())
                foreach (var c in go.GetComponents<MonoBehaviour>())
                    if (c != null && IsGameType(c.GetType())) yield return new KeyValuePair<GameObject, MonoBehaviour>(go, c);
        }

        // ================================================================== SKIN

        class SkinItem
        {
            public string path, name, group, comp;
            public int count;
            public Component sample;
            public bool open;
        }

        class SkinSet
        {
            public string title;
            public Object[] items;
            public Component owner;
        }

        class SkinMethod
        {
            public string title;
            public MethodInfo method;
            public Component owner;
            public int choices;
        }

        static readonly string[] SkinGroups = { "Nhân vật", "Môi trường & map", "Đồ vật & item", "Giao diện (UI)" };
        static readonly Regex CharRx = new Regex(@"\b(player|character|char|hero|man|woman|girl|boy|body|skin|avatar|human|stickman|chef|worker|customer|npc|enemy|enemies|zombie|boss|guy|people|person|soldier|knight|cat|dog|animal|pet|head|hair|face|cloth|clothes|outfit|hat|model|rig)\b", RegexOptions.IgnoreCase);
        static readonly Regex EnvRx = new Regex(@"\b(ground|floor|terrain|map|road|grass|wall|walls|water|sky|background|bg|env|environment|tile|tiles|stage|building|buildings|house|tree|trees|rock|rocks|fence|street|island|sand|level|plane|platform|arena|world|mountain|cloud|clouds|decor|props?)\b", RegexOptions.IgnoreCase);

        List<SkinItem> skinItems;
        List<SkinSet> skinSets;
        List<SkinMethod> skinMethods;
        string skinFilter = "";
        readonly Dictionary<string, bool> studioFold = new Dictionary<string, bool>();

        bool StudioFold(string key, string label, bool dflt)
        {
            bool open;
            if (!studioFold.TryGetValue(key, out open)) open = dflt;
            open = EditorGUILayout.Foldout(open, label, true, EditorStyles.foldoutHeader);
            studioFold[key] = open;
            return open;
        }

        void DrawSkinPage()
        {
            if (skinItems == null) ScanSkins();
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Bấm “Đổi” rồi kéo material / texture / sprite mới vào ô. Đổi một lần là áp dụng cho mọi object cùng tên.", stMuted);
            if (GUILayout.Button("Làm mới", EditorStyles.miniButton, GUILayout.Width(64))) ScanSkins();
            EditorGUILayout.EndHorizontal();
            skinFilter = EditorGUILayout.TextField("Tìm", skinFilter);

            if (skinSets.Count > 0 || skinMethods.Count > 0)
            {
                GUILayout.Label("Bộ skin có sẵn trong game", stH2);
                foreach (var set in skinSets) DrawSkinSet(set);
                foreach (var sm in skinMethods) DrawSkinMethod(sm);
            }

            foreach (var g in SkinGroups)
            {
                var items = skinItems.Where(i => i.group == g && (string.IsNullOrEmpty(skinFilter) ||
                                                                   i.path.IndexOf(skinFilter, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
                if (items.Count == 0) continue;
                if (!StudioFold("skin" + g, g + "  (" + items.Count + ")", g == SkinGroups[0] || !string.IsNullOrEmpty(skinFilter))) continue;
                foreach (var it in items.Take(80)) DrawSkinItem(it);
                if (items.Count > 80) GUILayout.Label("Còn " + (items.Count - 80) + " mục — gõ vào ô Tìm để thu hẹp.", stMuted);
            }
            if (skinItems.Count == 0) EditorGUILayout.HelpBox("Không thấy object có hình trong scene đang mở.", MessageType.None);
        }

        void ScanSkins()
        {
            var map = new Dictionary<string, SkinItem>();
            foreach (var go in SceneObjects())
            {
                foreach (var c in go.GetComponents<Component>())
                {
                    if (!IsVisual(c)) continue;
                    string path = CreativeReflect.PathOf(go.transform);
                    string key = path + "|" + c.GetType().Name;
                    SkinItem it;
                    if (map.TryGetValue(key, out it)) { it.count++; continue; }
                    map[key] = new SkinItem { path = path, name = CreativeReflect.BaseName(go.name), comp = c.GetType().Name, sample = c, count = 1, group = SkinGroupOf(go, c, path) };
                }
            }
            skinItems = map.Values.OrderBy(i => Array.IndexOf(SkinGroups, i.group)).ThenBy(i => i.path).ToList();
            FindSkinSets();
        }

        static bool IsVisual(Component c)
        {
            if (c == null) return false;
            if (c is Renderer) return !(c is ParticleSystemRenderer) && !(c is TrailRenderer) && !(c is LineRenderer);
            return c is Behaviour && !IsGameType(c.GetType()) && c.GetType().GetProperty("sprite", typeof(Sprite)) != null;
        }

        static string SkinGroupOf(GameObject go, Component c, string path)
        {
            if (!(c is Renderer) && go.GetComponent<RectTransform>() != null) return SkinGroups[3];
            string words = Words(path);
            if (c is SkinnedMeshRenderer || CharRx.IsMatch(words)) return SkinGroups[0];
            var r = c as Renderer;
            string mats = r != null ? string.Join(" ", r.sharedMaterials.Where(m => m != null).Select(m => SplitWords(m.name)).ToArray()) : "";
            return EnvRx.IsMatch(words + " " + mats) ? SkinGroups[1] : SkinGroups[2];
        }

        void FindSkinSets()
        {
            skinSets = new List<SkinSet>();
            skinMethods = new List<SkinMethod>();
            var seen = new HashSet<string>();
            foreach (var kv in GameComponents())
            {
                var c = kv.Value;
                if (!seen.Add(CreativeReflect.PathOf(kv.Key.transform) + "|" + c.GetType().Name)) continue;
                int biggest = 0;
                foreach (var f in GameFields(c.GetType()))
                {
                    if (f.IsStatic) continue;
                    var ft = f.FieldType;
                    var el = ft.IsArray ? ft.GetElementType() : ft.IsGenericType && ft.GetGenericTypeDefinition() == typeof(List<>) ? ft.GetGenericArguments()[0] : null;
                    if (el == null || !(typeof(Material).IsAssignableFrom(el) || typeof(Sprite).IsAssignableFrom(el) || typeof(Mesh).IsAssignableFrom(el) || typeof(Texture).IsAssignableFrom(el))) continue;
                    string name = CleanName(f.Name);
                    if (name == null || !SkinRx.IsMatch(SplitWords(name))) continue;
                    var list = CreativeReflect.Get(c, f) as IList;
                    if (list == null) continue;
                    var items = list.Cast<object>().OfType<Object>().Where(o => o != null).ToArray();
                    if (items.Length == 0) continue;
                    biggest = Math.Max(biggest, items.Length);
                    skinSets.Add(new SkinSet { title = CreativeReflect.BaseName(kv.Key.name) + " · " + ObjectNames.NicifyVariableName(name), items = items, owner = c });
                }
                foreach (var m in c.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    var ps = m.GetParameters();
                    if (m.IsSpecialName || !SkinRx.IsMatch(SplitWords(m.Name)) || ps.Length > 1 || (ps.Length == 1 && ps[0].ParameterType != typeof(int))) continue;
                    skinMethods.Add(new SkinMethod { title = CreativeReflect.BaseName(kv.Key.name) + " · " + ObjectNames.NicifyVariableName(m.Name), method = m, owner = c, choices = ps.Length == 0 ? 0 : Math.Max(biggest, 5) });
                }
            }
        }

        void DrawSkinSet(SkinSet set)
        {
            EditorGUILayout.BeginVertical(stCard);
            GUILayout.Label(set.title + "  (" + set.items.Length + ")", EditorStyles.boldLabel);
            int perRow = Mathf.Max(1, (int)((position.width - 40) / 58));
            for (int i = 0; i < set.items.Length; i += perRow)
            {
                EditorGUILayout.BeginHorizontal();
                for (int k = i; k < Math.Min(i + perRow, set.items.Length); k++)
                {
                    var o = set.items[k];
                    var content = new GUIContent(Thumb(o), o.name + " — bấm để dùng skin này");
                    if (GUILayout.Button(content, GUILayout.Width(52), GUILayout.Height(52))) ApplySkinSetItem(set, o);
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();
            if (AssetPreview.IsLoadingAssetPreviews()) Repaint();
        }

        void DrawSkinMethod(SkinMethod sm)
        {
            if (sm.owner == null) return;
            EditorGUILayout.BeginVertical(stCard);
            GUILayout.Label(sm.title + (Application.isPlaying ? "" : "  — bấm Play để dùng"), EditorStyles.boldLabel);
            GUI.enabled = Application.isPlaying;
            EditorGUILayout.BeginHorizontal();
            if (sm.choices == 0)
            {
                if (GUILayout.Button("Đổi skin", GUILayout.Width(90))) Invoke(sm, null);
            }
            else
                for (int i = 0; i < sm.choices; i++)
                    if (GUILayout.Button((i + 1).ToString(), GUILayout.Width(30))) Invoke(sm, new object[] { i });
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            GUI.enabled = true;
            EditorGUILayout.EndVertical();
        }

        static void Invoke(SkinMethod sm, object[] args)
        {
            try { sm.method.Invoke(sm.owner, args); }
            catch (Exception e) { Debug.LogException(e.InnerException ?? e); }
        }

        void ApplySkinSetItem(SkinSet set, Object o)
        {
            Func<SkinItem, bool> ok;
            if (o is Sprite) ok = i => !(i.sample is Renderer) || i.sample is SpriteRenderer;
            else if (o is Mesh) ok = i => i.sample is SkinnedMeshRenderer;
            else ok = i => i.sample is Renderer && !(i.sample is SpriteRenderer);
            string op = CreativeReflect.PathOf(set.owner.transform);
            var target = skinItems.FirstOrDefault(i => i.path.StartsWith(op) && ok(i)) ??
                         skinItems.FirstOrDefault(i => i.group == SkinGroups[0] && ok(i)) ?? skinItems.FirstOrDefault(ok);
            if (target == null) { ShowNotification(new GUIContent("Không tìm thấy object để áp skin")); return; }
            if (o is Sprite) SetSkin(target, "sprite", 0, null, o);
            else if (o is Mesh) SetSkin(target, "mesh", 0, null, o);
            else if (o is Texture)
            {
                var m = ((Renderer)target.sample).sharedMaterials.FirstOrDefault();
                string prop = MainTexProp(m);
                if (prop != null) SetSkin(target, "tex", 0, prop, o);
            }
            else SetSkin(target, "mat", 0, null, o);
            target.open = true;
            ShowNotification(new GUIContent("Đã đổi: " + target.name));
        }

        void DrawSkinItem(SkinItem it)
        {
            var c = it.sample;
            if (c == null) return;
            bool changed = P.skins.Any(s => s.path == it.path && s.component == it.comp);
            EditorGUILayout.BeginVertical(stCard);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(Thumb(CurrentVisual(it)), GUILayout.Width(40), GUILayout.Height(40));
            EditorGUILayout.BeginVertical();
            GUILayout.Label(it.name + (it.count > 1 ? "   ×" + it.count : ""), EditorStyles.boldLabel);
            if (changed) ColorLabel("●  đã đổi", Green, EditorStyles.miniBoldLabel);
            else GUILayout.Label(it.path, stMuted);
            EditorGUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Chọn", EditorStyles.miniButton, GUILayout.Width(44))) { Selection.activeGameObject = c.gameObject; EditorGUIUtility.PingObject(c.gameObject); }
            if (Tinted(it.open ? "Thu gọn" : "Đổi", it.open ? Color.white : Blue, EditorStyles.miniButton, GUILayout.Width(60))) it.open = !it.open;
            EditorGUILayout.EndHorizontal();
            if (it.open) DrawSkinSlots(it);
            EditorGUILayout.EndVertical();
        }

        Object CurrentVisual(SkinItem it)
        {
            var ov = P.skins.FirstOrDefault(s => s.path == it.path && s.component == it.comp && s.kind != "tex" && s.assetObj != null);
            if (ov != null) return ov.assetObj;
            var r = it.sample as Renderer;
            if (it.sample is SpriteRenderer || r == null) return CurrentSprite(it.sample);
            return r.sharedMaterials.FirstOrDefault(m => m != null);
        }

        static Sprite CurrentSprite(Component c)
        {
            var p = c.GetType().GetProperty("sprite", typeof(Sprite));
            return p != null ? p.GetValue(c, null) as Sprite : null;
        }

        static string MainTexProp(Material m)
        {
            if (m == null) return null;
            foreach (var p in new[] { "_BaseMap", "_MainTex", "_BaseColorMap", "_Albedo" })
                if (m.HasProperty(p)) return p;
            return null;
        }

        void DrawSkinSlots(SkinItem it)
        {
            var r = it.sample as Renderer;
            if (it.sample is SpriteRenderer || r == null)
            {
                SlotField(it, "sprite", 0, null, typeof(Sprite), "Hình (sprite)", CurrentSprite(it.sample));
                return;
            }
            var smr = r as SkinnedMeshRenderer;
            if (smr != null) SlotField(it, "mesh", 0, null, typeof(Mesh), "Hình dáng (mesh)", smr.sharedMesh);
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                var ov = FindSkin(it, "mat", i, null);
                var shown = ov != null ? ov.assetObj as Material : mats[i];
                SlotField(it, "mat", i, null, typeof(Material), mats.Length > 1 ? "Material " + (i + 1) : "Material", mats[i]);
                string prop = MainTexProp(shown);
                if (prop != null) SlotField(it, "tex", i, prop, typeof(Texture), "    Texture", shown.GetTexture(prop));
            }
        }

        void SlotField(SkinItem it, string kind, int slot, string prop, Type type, string label, Object current)
        {
            var ov = FindSkin(it, kind, slot, prop);
            var shown = ov != null ? ov.assetObj : current;
            EditorGUILayout.BeginHorizontal();
            var nv = EditorGUILayout.ObjectField(label, shown, type, false);
            if (ov != null && GUILayout.Button("Về gốc", EditorStyles.miniButton, GUILayout.Width(52)))
            {
                P.skins.Remove(ov);
                CreativePresetStore.Save();
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.EndHorizontal();
            if (nv != shown) SetSkin(it, kind, slot, prop, nv);
        }

        SkinOverride FindSkin(SkinItem it, string kind, int slot, string prop)
        {
            return P.skins.FirstOrDefault(s => s.path == it.path && s.component == it.comp && s.kind == kind && s.slot == slot && (s.prop ?? "") == (prop ?? ""));
        }

        void SetSkin(SkinItem it, string kind, int slot, string prop, Object asset)
        {
            var ov = FindSkin(it, kind, slot, prop);
            if (asset == null)
            {
                if (ov != null) P.skins.Remove(ov);
            }
            else
            {
                if (ov == null)
                {
                    ov = new SkinOverride { path = it.path, component = it.comp, kind = kind, slot = slot, prop = prop ?? "", label = it.name };
                    P.skins.Add(ov);
                }
                ov.asset = CreativePresetStore.Ref(asset);
                ov.assetObj = asset;
            }
            CreativePresetStore.Save();
        }

        // ================================================================== CHEAT

        class CheatRow
        {
            public string path, comp, member, label, hint, owner;
            public FieldInfo field;
            public Component sample;
            public int count = 1;
        }

        static readonly string[][] Hints =
        {
            new[] { "Tiền", "money coin coins cash gold gem gems dollar dollars diamond diamonds currency wallet balance bucks" },
            new[] { "Điểm", "score scores point points xp exp star stars" },
            new[] { "Combo", "combo combos streak chain multiplier" },
            new[] { "Stack / sức chứa", "stack stacks stacked capacity carry carried pile backpack bag inventory" },
            new[] { "Máu / mạng", "hp health life lives heart hearts shield armor armour" },
            new[] { "Sát thương", "damage dmg power attack strength" },
            new[] { "Tốc độ", "speed velocity acceleration accel" },
            new[] { "Thời gian", "time timer duration delay countdown seconds" },
            new[] { "Số lượng", "count amount num number total quantity" },
            new[] { "Level", "level lvl wave stage round" },
            new[] { "Giá", "price cost" },
            new[] { "Thu nhập", "reward income earn earning profit bonus" },
            new[] { "Nhảy / lực", "jump gravity force thrust" },
            new[] { "Nhịp / tần suất", "cooldown rate interval frequency spawn" },
            new[] { "Tầm / kích thước", "range radius distance scale size" },
            new[] { "Cheat có sẵn", "god invincible immortal cheat cheats debug unlock unlocked infinite free unlimited test skip auto" },
        };

        List<CheatRow> cheatRows;
        string cheatFilter = "";

        static string HintOf(string name)
        {
            var toks = Tokens(name);
            foreach (var h in Hints)
            {
                var set = h[1].Split(' ');
                if (toks.Any(t => set.Contains(t))) return h[0];
            }
            return null;
        }

        static bool Cheatable(Type t) { return CreativeReflect.IsNumber(t) || t == typeof(bool) || t.IsEnum; }

        void ScanCheats()
        {
            cheatRows = new List<CheatRow>();
            var byKey = new Dictionary<string, List<CheatRow>>();
            var statics = new HashSet<string>();
            foreach (var kv in GameComponents())
            {
                var c = kv.Value;
                var t = c.GetType();
                string path = CreativeReflect.PathOf(kv.Key.transform);
                string key = path + "|" + t.Name;
                List<CheatRow> existing;
                if (byKey.TryGetValue(key, out existing)) { foreach (var r in existing) r.count++; continue; }
                var rows = new List<CheatRow>();
                foreach (var f in GameFields(t))
                {
                    if (f.IsLiteral || f.IsInitOnly || !Cheatable(f.FieldType)) continue;
                    string name = CleanName(f.Name);
                    if (string.IsNullOrEmpty(name)) continue;
                    if (f.IsStatic && !statics.Add(f.DeclaringType.FullName + "." + f.Name)) continue;
                    string hint = HintOf(name);
                    if (hint == "Cheat có sẵn" && f.FieldType != typeof(bool)) hint = null;
                    rows.Add(new CheatRow
                    {
                        path = path, comp = t.Name, member = f.Name, label = ObjectNames.NicifyVariableName(name), hint = hint,
                        owner = CreativeReflect.BaseName(kv.Key.name) + " · " + t.Name, field = f, sample = c
                    });
                }
                byKey[key] = rows;
                cheatRows.AddRange(rows);
            }
        }

        void DrawCheatPage()
        {
            if (cheatRows == null) ScanCheats();
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Đổi thông số như menu cheat. Bấm “Khoá” để giữ nguyên giá trị suốt lúc chơi (vd: tiền không bao giờ hết).", stMuted);
            if (GUILayout.Button("Làm mới", EditorStyles.miniButton, GUILayout.Width(64))) ScanCheats();
            EditorGUILayout.EndHorizontal();
            if (!Application.isPlaying)
                EditorGUILayout.HelpBox("Đang ở Edit mode: số hiện là giá trị trong scene. Bấm ▶ Play để thấy giá trị thật lúc chơi và chỉnh trực tiếp.", MessageType.None);
            cheatFilter = EditorGUILayout.TextField("Tìm", cheatFilter);

            var rows = cheatRows.Where(r => string.IsNullOrEmpty(cheatFilter) ||
                                            (r.label + " " + r.owner + " " + r.hint).IndexOf(cheatFilter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            if (cheatRows.Count == 0) EditorGUILayout.HelpBox("Không thấy script game nào trong scene đang mở.", MessageType.None);

            if (P.cheats.Count > 0)
            {
                EditorGUILayout.BeginHorizontal();
                ColorLabel("●  Đang cheat " + P.cheats.Count + " thông số", Green, EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Bỏ hết cheat", EditorStyles.miniButton, GUILayout.Width(90))) { P.cheats.Clear(); CreativePresetStore.Save(); }
                EditorGUILayout.EndHorizontal();
            }

            GUILayout.Label("Thông số quan trọng", stH2);
            bool any = false;
            foreach (var h in Hints)
            {
                var items = rows.Where(r => r.hint == h[0]).ToList();
                if (items.Count == 0) continue;
                any = true;
                EditorGUILayout.BeginVertical(stCard);
                GUILayout.Label(h[0], EditorStyles.boldLabel);
                foreach (var r in items.Take(30)) DrawCheatRow(r, true);
                EditorGUILayout.EndVertical();
            }
            if (!any) GUILayout.Label("Không có — xem trong “Tất cả thông số”.", stMuted);

            GUILayout.Space(6);
            if (StudioFold("cheatAll", "Tất cả thông số  (" + rows.Count + ")", !string.IsNullOrEmpty(cheatFilter)))
            {
                foreach (var g in rows.GroupBy(r => r.owner).OrderBy(g => g.Key))
                {
                    if (!StudioFold("cheat" + g.Key, g.Key + (g.First().count > 1 ? "  ×" + g.First().count : ""), !string.IsNullOrEmpty(cheatFilter))) continue;
                    EditorGUILayout.BeginVertical(stCard);
                    foreach (var r in g.Take(80)) DrawCheatRow(r, false);
                    EditorGUILayout.EndVertical();
                }
            }
        }

        void DrawCheatRow(CheatRow row, bool showOwner)
        {
            var cv = P.cheats.FirstOrDefault(x => x.path == row.path && x.component == row.comp && x.member == row.member);
            object live = row.sample != null ? CreativeReflect.Get(row.sample, row.field) : null;
            string liveText = CreativeReflect.Format(live);
            string cur = cv != null ? cv.value : liveText;
            var t = row.field.FieldType;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.BeginVertical();
            GUILayout.Label(new GUIContent(row.label, row.comp + "." + row.member), cv != null ? EditorStyles.boldLabel : EditorStyles.label);
            if (showOwner) GUILayout.Label(row.owner + (row.count > 1 ? "  ×" + row.count : ""), stMuted);
            EditorGUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            string nv = ValueField(t, cur);
            if ((row.hint == "Tiền" || row.hint == "Điểm") && CreativeReflect.IsNumber(t))
            {
                double baseVal;
                if (!double.TryParse(Application.isPlaying ? liveText : cur, NumberStyles.Float, CultureInfo.InvariantCulture, out baseVal)) baseVal = 0;
                if (GUILayout.Button("+100", EditorStyles.miniButton, GUILayout.Width(40))) nv = CreativeReflect.Format(baseVal + 100);
                if (GUILayout.Button("+1000", EditorStyles.miniButton, GUILayout.Width(46))) nv = CreativeReflect.Format(baseVal + 1000);
            }
            if (cv != null)
            {
                var old = GUI.backgroundColor;
                if (cv.locked) GUI.backgroundColor = Green;
                bool lk = GUILayout.Toggle(cv.locked, "Khoá", "Button", GUILayout.Width(46));
                GUI.backgroundColor = old;
                if (lk != cv.locked) { cv.locked = lk; CreativePresetStore.Save(); }
                if (GUILayout.Button("Bỏ", EditorStyles.miniButton, GUILayout.Width(32))) { P.cheats.Remove(cv); CreativePresetStore.Save(); GUIUtility.ExitGUI(); }
            }
            else GUILayout.Space(82);
            EditorGUILayout.EndHorizontal();
            if (cv != null && Application.isPlaying && live != null && liveText != cv.value && !cv.locked)
                GUILayout.Label("đang chạy: " + liveText + " (game đã đổi — bấm Khoá để giữ " + cv.value + ")", stMuted);
            if (nv != cur) SetCheat(row, nv);
        }

        static string ValueField(Type t, string cur)
        {
            if (t == typeof(bool))
            {
                bool b = cur == "true";
                bool nb = EditorGUILayout.Toggle(b, GUILayout.Width(90));
                return nb != b ? (nb ? "true" : "false") : cur;
            }
            if (t.IsEnum)
            {
                Enum e;
                try { e = (Enum)Enum.Parse(t, cur); } catch (Exception) { e = (Enum)Enum.GetValues(t).GetValue(0); }
                var ne = EditorGUILayout.EnumPopup(e, GUILayout.Width(110));
                return ne.Equals(e) ? cur : ne.ToString();
            }
            double d;
            double.TryParse(cur, NumberStyles.Float, CultureInfo.InvariantCulture, out d);
            double nd = EditorGUILayout.DelayedDoubleField(d, GUILayout.Width(90));
            return nd != d ? CreativeReflect.Format(nd) : cur;
        }

        void SetCheat(CheatRow row, string value)
        {
            var cv = P.cheats.FirstOrDefault(x => x.path == row.path && x.component == row.comp && x.member == row.member);
            if (cv == null)
            {
                cv = new CheatValue { path = row.path, component = row.comp, member = row.member, label = row.label };
                P.cheats.Add(cv);
            }
            cv.value = value;
            CreativePresetStore.Save();
            if (Application.isPlaying && CreativeDirector.Instance != null) CreativeDirector.Instance.ApplyCheatNow(cv);
        }

        // ================================================================== ÂM THANH

        static readonly string[] SfxGroups = { "Tiền & stack", "Điểm & combo", "Va chạm & nhặt đồ", "Máu · hạ gục · level", "Popup & thắng/thua", "Tự thêm", "Khác" };

        static readonly string[][] ValueCats =
        {
            new[] { "combo", "combo combos streak chain multiplier" },
            new[] { "money", "money coin coins cash gold gem gems dollar dollars currency wallet balance diamond diamonds bank bucks" },
            new[] { "score", "score point points xp exp star stars" },
            new[] { "stack", "stack stacks stacked carry carried carrying pile piles tower holding held bag backpack inventory cargo load items bricks boxes" },
            new[] { "hp", "hp health life lives heart hearts" },
            new[] { "kill", "kill kills killed defeated dead deaths" },
            new[] { "upgrade", "upgrade upgrades lvl tier" },
            new[] { "level", "level wave stage round" },
        };

        static readonly HashSet<string> NotEventTokens = new HashSet<string>
        {
            "max", "min", "limit", "speed", "rate", "delay", "duration", "price", "cost", "per", "value", "values", "prefab", "prefabs",
            "text", "txt", "label", "ui", "offset", "height", "spacing", "distance", "interval", "timer", "radius", "step", "target",
            "start", "initial", "default", "base", "needed", "required", "threshold", "sound", "sfx", "audio", "clip", "anim", "effect",
            "fx", "icon", "sprite", "color", "pos", "position", "index", "prefab", "parent", "point", "spawn", "data", "config", "time"
        };

        static readonly Regex StackGoRx = new Regex(@"\b(stack|stacks|holder|carry|pile|backpack|bag|tower|stackpoint|stack point)\b", RegexOptions.IgnoreCase);
        static readonly Regex CollectRx = new Regex(@"\b(coin|coins|money|cash|gem|gems|diamond|pickup|collectible|collectable|item|star|food|fruit|token|crystal|bill|banknote|brick|log|wood|box|crate|orb|ore|stone|bottle|cup|burger|pizza|dollar)\b", RegexOptions.IgnoreCase);
        static readonly Regex PopupRx = new Regex(@"\b(end ?card|win|victory|complete|completed|lose|lost|fail|failed|game ?over|popup|level ?up|reward|unlock|unlocked|congrats?|congratulations|perfect|great|awesome|cool|excellent|amazing|wow|nice|good|combo|bonus|new ?record)\b", RegexOptions.IgnoreCase);
        static readonly string[] TouchMethods = { "OnTriggerEnter", "OnTriggerEnter2D", "OnCollisionEnter", "OnCollisionEnter2D", "OnControllerColliderHit" };

        List<SfxRule> events;
        readonly Dictionary<SfxRule, Component> eventSample = new Dictionary<SfxRule, Component>();
        string soundFilter = "";
        List<AudioClip> projectClips;
        readonly Dictionary<string, bool> unityTypeCache = new Dictionary<string, bool>();

        class ClipUse
        {
            public AudioClip clip;
            public int count;
            public string where;
        }

        List<ClipUse> clipUses;

        // thêm sự kiện thủ công
        SfxTrigger addTrig = SfxTrigger.Touch;
        GameObject addObj;
        int addMember;
        string addFilter = "";
        KeyCode addKey = KeyCode.Space;

        static string TriggerLabel(SfxTrigger t)
        {
            switch (t)
            {
                case SfxTrigger.ValueUp: return "Một con số tăng (tiền, điểm, combo…)";
                case SfxTrigger.ValueDown: return "Một con số giảm";
                case SfxTrigger.ChildAdded: return "Có vật được thêm vào (stack)";
                case SfxTrigger.ChildRemoved: return "Có vật bị lấy ra (stack)";
                case SfxTrigger.ObjectsGone: return "Object biến mất (bị ăn / nhặt)";
                case SfxTrigger.ObjectsAppear: return "Object xuất hiện (spawn)";
                case SfxTrigger.Touch: return "Object va chạm";
                case SfxTrigger.Activated: return "Object hiện ra (popup, hiệu ứng)";
                case SfxTrigger.KeyDown: return "Nhấn phím";
                default: return "Chạm / click màn hình";
            }
        }

        void DrawSoundPage()
        {
            if (events == null) DetectEvents();
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Tool tự tìm hành động trong game. Chọn âm thanh cho hành động nào cần — bấm ▶ để nghe thử, Play để nghe trong game.", stMuted);
            if (GUILayout.Button("Làm mới", EditorStyles.miniButton, GUILayout.Width(64))) { DetectEvents(); clipUses = null; projectClips = null; }
            EditorGUILayout.EndHorizontal();
            soundFilter = EditorGUILayout.TextField("Tìm", soundFilter);

            // sự kiện tìm được + sự kiện đã lưu (kể cả tự thêm)
            var shown = new List<SfxRule>(events);
            foreach (var r in P.sfx) if (!shown.Any(x => x.id == r.id)) shown.Add(r);
            foreach (var g in SfxGroups)
            {
                var items = shown.Where(r => (r.group ?? "Khác") == g && (string.IsNullOrEmpty(soundFilter) ||
                                                 (r.label + " " + r.note).IndexOf(soundFilter, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
                if (items.Count == 0) continue;
                int on = items.Count(r => P.sfx.Contains(r) && r.enabled && r.clipObj != null);
                if (!StudioFold("sfx" + g, g + "  (" + items.Count + (on > 0 ? " · " + on + " đã có âm thanh" : "") + ")",
                                g != "Khác" || !string.IsNullOrEmpty(soundFilter))) continue;
                foreach (var r in items.Take(60)) DrawSfxCard(r);
            }
            if (events.Count == 0) EditorGUILayout.HelpBox("Chưa tìm thấy hành động nào — bấm Play rồi Làm mới, hoặc thêm thủ công ở dưới.", MessageType.None);

            GUILayout.Space(6);
            if (StudioFold("sfxAdd", "Thêm hành động khác", false)) DrawAddEvent();
            if (StudioFold("sfxSwap", "Thay âm thanh có sẵn trong game", false)) DrawClipSwaps();
        }

        void DrawSfxCard(SfxRule r)
        {
            bool saved = P.sfx.Contains(r);
            EditorGUILayout.BeginVertical(stCard);
            EditorGUILayout.BeginHorizontal();
            if (saved && r.clipObj != null)
            {
                bool en = EditorGUILayout.Toggle(r.enabled, GUILayout.Width(16));
                if (en != r.enabled) { r.enabled = en; CreativePresetStore.Save(); }
            }
            GUILayout.Label(r.label, EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            string live = LiveValue(r);
            if (live != null) GUILayout.Label("hiện tại: " + live, EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(r.note)) GUILayout.Label(r.note, stMuted);

            EditorGUILayout.BeginHorizontal();
            var nc = (AudioClip)EditorGUILayout.ObjectField(r.clipObj, typeof(AudioClip), false);
            var quick = QuickClips(r.group);
            int pick = EditorGUILayout.Popup(0, new[] { "Gợi ý ▾" }.Concat(quick.Select(q => q.name)).ToArray(), GUILayout.Width(80));
            if (pick > 0) nc = quick[pick - 1];
            GUI.enabled = nc != null;
            if (GUILayout.Button("▶", GUILayout.Width(26))) PreviewClip(nc);
            GUI.enabled = Application.isPlaying && saved && r.clipObj != null && CreativeDirector.Instance != null;
            if (GUILayout.Button("Thử", GUILayout.Width(40))) CreativeDirector.Instance.Fire(r);
            GUI.enabled = true;
            if (saved && GUILayout.Button("Bỏ", EditorStyles.miniButton, GUILayout.Width(32))) { P.sfx.Remove(r); CreativePresetStore.Save(); GUIUtility.ExitGUI(); }
            EditorGUILayout.EndHorizontal();
            if (nc != r.clipObj) SetSfxClip(r, nc);

            if (saved && r.clipObj != null)
            {
                float vol = EditorGUILayout.Slider("Âm lượng", r.volume, 0f, 1f);
                bool rising = r.trigger == SfxTrigger.ValueUp || r.trigger == SfxTrigger.ValueDown || r.trigger == SfxTrigger.ChildAdded ||
                              r.trigger == SfxTrigger.ChildRemoved || r.trigger == SfxTrigger.ObjectsGone || r.trigger == SfxTrigger.Touch;
                float ps = rising ? EditorGUILayout.Slider(new GUIContent("Cao dần khi liên tiếp", "Mỗi lần phát liên tiếp (trong 1.5 giây) âm thanh cao hơn một chút — hợp cho combo, stack tiền"), r.pitchStep, 0f, 0.2f) : r.pitchStep;
                if (Math.Abs(vol - r.volume) > 0.001f || Math.Abs(ps - r.pitchStep) > 0.001f)
                {
                    r.volume = vol;
                    r.pitchStep = ps;
                    CreativePresetStore.Save();
                }
            }
            EditorGUILayout.EndVertical();
        }

        void SetSfxClip(SfxRule r, AudioClip clip)
        {
            r.clipObj = clip;
            r.clip = CreativePresetStore.Ref(clip);
            r.enabled = clip != null;
            if (clip != null && !P.sfx.Contains(r)) P.sfx.Add(r);
            if (clip == null && !r.id.StartsWith("custom|")) P.sfx.Remove(r);
            CreativePresetStore.Save();
        }

        string LiveValue(SfxRule r)
        {
            if (Application.isPlaying && CreativeDirector.Instance != null)
            {
                string s = CreativeDirector.Instance.Live(r);
                if (s != null) return s;
            }
            Component c;
            if (!eventSample.TryGetValue(r, out c) || c == null) return null;
            if (r.trigger == SfxTrigger.ValueUp || r.trigger == SfxTrigger.ValueDown)
                return CreativeReflect.Format(CreativeReflect.ReadNumber(c, r.member));
            if (r.trigger == SfxTrigger.ChildAdded || r.trigger == SfxTrigger.ChildRemoved)
                return c.transform.childCount.ToString();
            return null;
        }

        List<AudioClip> QuickClips(string group)
        {
            if (projectClips == null)
                projectClips = AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets" })
                                            .Select(g => AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(g)))
                                            .Where(c => c != null).OrderBy(c => c.name).ToList();
            string kw;
            switch (group)
            {
                case "Tiền & stack": kw = "coin|cash|money|ching|collect|pick|gold|stack|pop|plop|drop|put|place|click|tick"; break;
                case "Điểm & combo": kw = "point|score|ding|pop|combo|perfect|great|chain|bonus|star"; break;
                case "Va chạm & nhặt đồ": kw = "hit|collect|pick|pop|coin|bump|impact|punch|crash|smash|eat|bite"; break;
                case "Máu · hạ gục · level": kw = "hit|hurt|damage|impact|die|death|kill|level|upgrade"; break;
                case "Popup & thắng/thua": kw = "win|victory|complete|fanfare|lose|fail|popup|whoosh|reward|cheer|tada"; break;
                default: kw = "click|tap|pop|whoosh|button"; break;
            }
            var rx = new Regex(kw, RegexOptions.IgnoreCase);
            return projectClips.Where(c => rx.IsMatch(c.name)).Concat(projectClips.Where(c => !rx.IsMatch(c.name))).Take(40).ToList();
        }

        void AddEvent(List<SfxRule> list, SfxRule r, Component sample)
        {
            r.id = r.trigger + "|" + r.path + "|" + r.component + "|" + r.member + "|" + r.filter;
            if (list.Any(x => x.id == r.id)) return;
            var saved = P.sfx.FirstOrDefault(x => x.id == r.id);
            var use = saved ?? r;
            if (saved != null) { saved.label = r.label; saved.note = r.note; saved.group = r.group; }
            list.Add(use);
            if (sample != null) eventSample[use] = sample;
        }

        /// <summary>Tự tìm hành động: biến tiền / điểm / combo / stack / máu…, chỗ chứa stack, va chạm, đồ nhặt được, popup.</summary>
        void DetectEvents()
        {
            var list = new List<SfxRule>();
            eventSample.Clear();
            var seen = new HashSet<string>();
            foreach (var kv in GameComponents())
            {
                var go = kv.Key;
                var c = kv.Value;
                var type = c.GetType();
                string path = CreativeReflect.PathOf(go.transform);
                if (!seen.Add(path + "|" + type.Name)) continue;
                string goName = CreativeReflect.BaseName(go.name);

                foreach (var f in GameFields(type))
                {
                    string name = CleanName(f.Name);
                    if (string.IsNullOrEmpty(name)) continue;
                    var toks = Tokens(name);
                    if (toks.Any(t => NotEventTokens.Contains(t))) continue;
                    var ft = f.FieldType;
                    bool numeric = CreativeReflect.IsNumber(ft) || (typeof(ICollection).IsAssignableFrom(ft) && ft != typeof(string));
                    string nice = ObjectNames.NicifyVariableName(name);
                    string note = goName + " · " + type.Name + "." + nice;
                    if (numeric)
                    {
                        string cat = null;
                        foreach (var vc in ValueCats)
                        {
                            var set = vc[1].Split(' ');
                            if (toks.Any(t => set.Contains(t))) { cat = vc[0]; break; }
                        }
                        if (cat == null) continue;
                        foreach (var s in ValueSuggestions(cat))
                            AddEvent(list, new SfxRule { trigger = s.Key, label = s.Value[0], group = s.Value[1], pitchStep = float.Parse(s.Value[2], CultureInfo.InvariantCulture),
                                                         note = note, path = path, component = type.Name, member = f.Name, filter = "" }, c);
                    }
                    else if ((ft == typeof(Transform) || ft == typeof(GameObject)) && StackGoRx.IsMatch(SplitWords(name)))
                    {
                        var v = CreativeReflect.Get(c, f);
                        var tr = v is GameObject ? ((GameObject)v).transform : v as Transform;
                        if (tr == null || !tr.gameObject.scene.IsValid()) continue;
                        AddStackEvents(list, tr);
                    }
                }

                if (TouchMethods.Any(m => type.GetMethod(m, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) != null))
                {
                    var partners = Partners(c);
                    if (partners.Count == 0) partners.Add("");
                    foreach (var p in partners.Take(4))
                        AddEvent(list, new SfxRule
                        {
                            trigger = SfxTrigger.Touch, group = SfxGroups[2], path = path, component = "", member = "", filter = p,
                            label = p == "" ? goName + " va chạm" : goName + " chạm " + p,
                            note = "Khi " + goName + " (" + type.Name + ") va chạm với " + (p == "" ? "bất kỳ vật gì" : "object có tên / tag / script “" + p + "”"),
                            pitchStep = 0f
                        }, null);
                }

                // prefab đồ nhặt được (coin, money…) mà script tạo ra
                foreach (var f in GameFields(type))
                {
                    if (f.IsStatic) continue;
                    var v = CreativeReflect.Get(c, f);
                    var objs = v is GameObject ? new[] { (GameObject)v } : (v as IList) != null ? ((IList)v).Cast<object>().OfType<GameObject>().ToArray() : new GameObject[0];
                    foreach (var o in objs)
                        if (o != null && EditorUtility.IsPersistent(o) && CollectRx.IsMatch(SplitWords(o.name))) AddCollectEvents(list, CreativeReflect.BaseName(o.name));
                }
            }

            var names = new Dictionary<string, int>();
            foreach (var go in SceneObjects())
            {
                string bn = CreativeReflect.BaseName(go.name);
                string w = SplitWords(bn);
                if (StackGoRx.IsMatch(w)) AddStackEvents(list, go.transform);
                if (CollectRx.IsMatch(w)) { int n; names.TryGetValue(bn, out n); names[bn] = n + 1; }
                if (PopupRx.IsMatch(w) && go.GetComponent<Canvas>() == null)
                    AddEvent(list, new SfxRule
                    {
                        trigger = SfxTrigger.Activated, group = SfxGroups[4], path = CreativeReflect.PathOf(go.transform), component = "", member = "", filter = "",
                        label = "Hiện " + bn, note = "Khi " + CreativeReflect.PathOf(go.transform) + " bật lên"
                    }, null);
            }
            foreach (var kv in names) AddCollectEvents(list, kv.Key);

            AddEvent(list, new SfxRule { trigger = SfxTrigger.Tap, group = SfxGroups[6], path = "", component = "", member = "", filter = "",
                                         label = "Chạm / click màn hình", note = "Mỗi lần chạm hoặc click vào màn hình" }, null);
            events = list;
        }

        static IEnumerable<KeyValuePair<SfxTrigger, string[]>> ValueSuggestions(string cat)
        {
            string money = SfxGroups[0], score = SfxGroups[1], life = SfxGroups[3];
            switch (cat)
            {
                case "money":
                    yield return new KeyValuePair<SfxTrigger, string[]>(SfxTrigger.ValueUp, new[] { "Nhận tiền", money, "0.02" });
                    yield return new KeyValuePair<SfxTrigger, string[]>(SfxTrigger.ValueDown, new[] { "Tiêu tiền", money, "0" });
                    break;
                case "score":
                    yield return new KeyValuePair<SfxTrigger, string[]>(SfxTrigger.ValueUp, new[] { "Ăn điểm", score, "0.03" });
                    break;
                case "combo":
                    yield return new KeyValuePair<SfxTrigger, string[]>(SfxTrigger.ValueUp, new[] { "Combo tăng", score, "0.08" });
                    yield return new KeyValuePair<SfxTrigger, string[]>(SfxTrigger.ValueDown, new[] { "Mất combo", score, "0" });
                    break;
                case "stack":
                    yield return new KeyValuePair<SfxTrigger, string[]>(SfxTrigger.ValueUp, new[] { "Stack thêm", money, "0.03" });
                    yield return new KeyValuePair<SfxTrigger, string[]>(SfxTrigger.ValueDown, new[] { "Stack bớt (giao / bán)", money, "0.03" });
                    break;
                case "hp":
                    yield return new KeyValuePair<SfxTrigger, string[]>(SfxTrigger.ValueDown, new[] { "Trúng đòn / mất máu", life, "0" });
                    yield return new KeyValuePair<SfxTrigger, string[]>(SfxTrigger.ValueUp, new[] { "Hồi máu", life, "0" });
                    break;
                case "kill":
                    yield return new KeyValuePair<SfxTrigger, string[]>(SfxTrigger.ValueUp, new[] { "Hạ gục", life, "0.05" });
                    break;
                case "upgrade":
                    yield return new KeyValuePair<SfxTrigger, string[]>(SfxTrigger.ValueUp, new[] { "Nâng cấp", life, "0" });
                    break;
                case "level":
                    yield return new KeyValuePair<SfxTrigger, string[]>(SfxTrigger.ValueUp, new[] { "Lên level / qua màn", life, "0" });
                    break;
            }
        }

        void AddStackEvents(List<SfxRule> list, Transform tr)
        {
            string p = CreativeReflect.PathOf(tr);
            string n = CreativeReflect.BaseName(tr.name);
            AddEvent(list, new SfxRule { trigger = SfxTrigger.ChildAdded, group = SfxGroups[0], path = p, component = "", member = "", filter = "", pitchStep = 0.03f,
                                         label = "Stack thêm vào " + n, note = "Khi có vật được đặt vào " + p }, tr);
            AddEvent(list, new SfxRule { trigger = SfxTrigger.ChildRemoved, group = SfxGroups[0], path = p, component = "", member = "", filter = "", pitchStep = 0.03f,
                                         label = "Lấy ra khỏi " + n, note = "Khi có vật rời khỏi " + p + " (giao, bán, ném)" }, tr);
        }

        void AddCollectEvents(List<SfxRule> list, string name)
        {
            AddEvent(list, new SfxRule { trigger = SfxTrigger.ObjectsGone, group = SfxGroups[2], path = "", component = "", member = "", filter = name, pitchStep = 0.02f,
                                         label = "Nhặt / ăn " + name, note = "Khi một " + name + " biến mất khỏi màn hình" }, null);
            AddEvent(list, new SfxRule { trigger = SfxTrigger.ObjectsAppear, group = SfxGroups[6], path = "", component = "", member = "", filter = name,
                                         label = name + " xuất hiện", note = "Khi có thêm " + name + " trên màn hình" }, null);
        }

        /// <summary>Đối tượng va chạm đọc từ code của hàm OnTrigger/OnCollision: CompareTag("Coin"), GetComponent&lt;Enemy&gt;()…</summary>
        List<string> Partners(MonoBehaviour c)
        {
            var result = new List<string>();
            var ms = MonoScript.FromMonoBehaviour(c);
            string code = ms != null ? ms.text ?? "" : "";
            var body = new StringBuilder();
            foreach (var m in TouchMethods)
            {
                int i = code.IndexOf(m + "(", StringComparison.Ordinal);
                if (i < 0) continue;
                int open = code.IndexOf('{', i);
                if (open < 0) continue;
                int depth = 0, k = open;
                for (; k < code.Length; k++)
                {
                    if (code[k] == '{') depth++;
                    else if (code[k] == '}' && --depth == 0) break;
                }
                body.Append(code, open, Math.Min(k, code.Length - 1) - open + 1);
            }
            string b = body.ToString();
            foreach (Match m in Regex.Matches(b, @"CompareTag\s*\(\s*""([^""]+)""|\.tag\s*==\s*""([^""]+)""|\.name\.Contains\s*\(\s*""([^""]+)""|GetComponent(?:InParent|InChildren)?\s*<\s*(\w+)\s*>|TryGetComponent\s*<\s*(\w+)\s*>|TryGetComponent\s*\(\s*out\s+(\w+)\s+\w+"))
            {
                string v = "";
                for (int g = 1; g < m.Groups.Count; g++) if (m.Groups[g].Success) { v = m.Groups[g].Value; break; }
                if (v.Length == 0 || IsUnityType(v) || result.Contains(v)) continue;
                result.Add(v);
            }
            return result;
        }

        bool IsUnityType(string name)
        {
            bool r;
            if (unityTypeCache.TryGetValue(name, out r)) return r;
            r = name == "var" || AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name.StartsWith("UnityEngine") && a.GetType("UnityEngine." + name) != null);
            unityTypeCache[name] = r;
            return r;
        }

        void DrawAddEvent()
        {
            EditorGUILayout.BeginVertical(stCard);
            var trigs = (SfxTrigger[])Enum.GetValues(typeof(SfxTrigger));
            int ti = EditorGUILayout.Popup("Khi nào", Array.IndexOf(trigs, addTrig), trigs.Select(TriggerLabel).ToArray());
            addTrig = trigs[Mathf.Max(0, ti)];
            bool needObj = addTrig != SfxTrigger.ObjectsGone && addTrig != SfxTrigger.ObjectsAppear && addTrig != SfxTrigger.KeyDown && addTrig != SfxTrigger.Tap;
            string[] memberNames = new string[0];
            var members = new List<KeyValuePair<Component, FieldInfo>>();
            if (needObj)
            {
                if (addObj == null && Selection.activeGameObject != null && !EditorUtility.IsPersistent(Selection.activeGameObject)) addObj = Selection.activeGameObject;
                addObj = (GameObject)EditorGUILayout.ObjectField(addTrig == SfxTrigger.Touch ? "Object va chạm" : addTrig == SfxTrigger.ChildAdded || addTrig == SfxTrigger.ChildRemoved ? "Chỗ chứa stack" : "Object",
                                                                 addObj, typeof(GameObject), true);
            }
            if ((addTrig == SfxTrigger.ValueUp || addTrig == SfxTrigger.ValueDown) && addObj != null)
            {
                foreach (var c in addObj.GetComponents<MonoBehaviour>())
                {
                    if (c == null || !IsGameType(c.GetType())) continue;
                    foreach (var f in GameFields(c.GetType()))
                    {
                        var ft = f.FieldType;
                        string n = CleanName(f.Name);
                        if (n != null && (CreativeReflect.IsNumber(ft) || (typeof(ICollection).IsAssignableFrom(ft) && ft != typeof(string))))
                            members.Add(new KeyValuePair<Component, FieldInfo>(c, f));
                    }
                }
                memberNames = members.Select(m => m.Key.GetType().Name + " / " + ObjectNames.NicifyVariableName(CleanName(m.Value.Name))).ToArray();
                if (members.Count == 0) EditorGUILayout.HelpBox("Object này không có biến số nào trong script.", MessageType.None);
                else addMember = EditorGUILayout.Popup("Con số", Mathf.Min(addMember, members.Count - 1), memberNames);
            }
            if (addTrig == SfxTrigger.Touch) addFilter = EditorGUILayout.TextField("Chạm với (tên / tag)", addFilter);
            if (addTrig == SfxTrigger.ObjectsGone || addTrig == SfxTrigger.ObjectsAppear) addFilter = EditorGUILayout.TextField("Tên object", addFilter);
            if (addTrig == SfxTrigger.KeyDown) addKey = (KeyCode)EditorGUILayout.EnumPopup("Phím", addKey);

            bool ready = (!needObj || addObj != null) && ((addTrig != SfxTrigger.ValueUp && addTrig != SfxTrigger.ValueDown) || members.Count > 0) &&
                         ((addTrig != SfxTrigger.ObjectsGone && addTrig != SfxTrigger.ObjectsAppear) || addFilter.Trim().Length > 0);
            GUI.enabled = ready;
            if (Tinted("Thêm hành động", Blue, stBig))
            {
                var r = new SfxRule
                {
                    id = "custom|" + Guid.NewGuid().ToString("N"), trigger = addTrig, group = "Tự thêm", key = addKey, filter = addFilter.Trim(),
                    path = addObj != null ? CreativeReflect.PathOf(addObj.transform) : "", component = "", member = "", enabled = false
                };
                if (members.Count > 0 && (addTrig == SfxTrigger.ValueUp || addTrig == SfxTrigger.ValueDown))
                {
                    var m = members[Mathf.Min(addMember, members.Count - 1)];
                    r.component = m.Key.GetType().Name;
                    r.member = m.Value.Name;
                    eventSample[r] = m.Key;
                }
                r.label = TriggerLabel(addTrig) + (addObj != null ? " — " + addObj.name : "") + (r.filter.Length > 0 ? " — " + r.filter : "") +
                          (addTrig == SfxTrigger.KeyDown ? " — " + addKey : "");
                r.note = "Chọn âm thanh bên dưới để bật.";
                P.sfx.Add(r);
                CreativePresetStore.Save();
                studioFold["sfxTự thêm"] = true;
            }
            GUI.enabled = true;
            EditorGUILayout.EndVertical();
        }

        void DrawClipSwaps()
        {
            if (clipUses == null) ScanClipUses();
            GUILayout.Label("Âm thanh game đang dùng. Chọn âm thanh khác ở cột “Thay bằng”, hoặc Tắt tiếng.", stMuted);
            if (clipUses.Count == 0) GUILayout.Label("Không thấy âm thanh nào gán trong scene.", stMuted);
            foreach (var u in clipUses)
            {
                var cs = P.clipSwaps.FirstOrDefault(x => x.fromClip == u.clip);
                EditorGUILayout.BeginVertical(stCard);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("▶", GUILayout.Width(26))) PreviewClip(u.clip);
                GUILayout.Label(u.clip.name + "   (" + u.count + " chỗ)", cs != null ? EditorStyles.boldLabel : EditorStyles.label);
                GUILayout.FlexibleSpace();
                bool mute = GUILayout.Toggle(cs != null && cs.mute, "Tắt tiếng", "Button", GUILayout.Width(70));
                EditorGUILayout.EndHorizontal();
                GUILayout.Label(u.where, stMuted);
                EditorGUILayout.BeginHorizontal();
                var to = (AudioClip)EditorGUILayout.ObjectField("Thay bằng", cs != null ? cs.toClip : null, typeof(AudioClip), false);
                GUI.enabled = to != null;
                if (GUILayout.Button("▶", GUILayout.Width(26))) PreviewClip(to);
                GUI.enabled = true;
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                if ((cs != null && (to != cs.toClip || mute != cs.mute)) || (cs == null && (to != null || mute)))
                {
                    if (cs == null) { cs = new ClipSwap { from = CreativePresetStore.Ref(u.clip), fromClip = u.clip }; P.clipSwaps.Add(cs); }
                    cs.toClip = to;
                    cs.to = CreativePresetStore.Ref(to);
                    cs.mute = mute;
                    if (to == null && !mute) P.clipSwaps.Remove(cs);
                    CreativePresetStore.Save();
                }
            }
        }

        void ScanClipUses()
        {
            var map = new Dictionary<AudioClip, ClipUse>();
            Action<AudioClip, string> add = (clip, where) =>
            {
                if (clip == null) return;
                ClipUse u;
                if (!map.TryGetValue(clip, out u)) { u = new ClipUse { clip = clip }; map[clip] = u; }
                u.count++;
                if (u.where == null) u.where = where;
                else if (!u.where.Contains(where) && u.where.Length < 120) u.where += ", " + where;
            };
            foreach (var go in SceneObjects())
            {
                foreach (var c in go.GetComponents<Component>())
                {
                    var src = c as AudioSource;
                    if (src != null) { add(src.clip, CreativeReflect.BaseName(go.name)); continue; }
                    var mb = c as MonoBehaviour;
                    if (mb != null && IsGameType(mb.GetType()))
                        CollectClips(mb, CreativeReflect.BaseName(go.name) + " · " + mb.GetType().Name, add, 0, new HashSet<object>());
                }
            }
            clipUses = map.Values.OrderBy(u => u.clip.name).ToList();
        }

        static void CollectClips(object o, string where, Action<AudioClip, string> add, int depth, HashSet<object> visited)
        {
            if (o == null || depth > 2 || !visited.Add(o)) return;
            foreach (var f in GameFields(o.GetType()))
            {
                if (f.IsStatic || f.FieldType.IsPrimitive || f.FieldType.IsEnum || f.FieldType == typeof(string)) continue;
                if (typeof(Component).IsAssignableFrom(f.FieldType) || f.FieldType == typeof(GameObject)) continue;
                object v;
                try { v = f.GetValue(o); } catch (Exception) { continue; }
                if (v == null) continue;
                if (v is AudioClip) { add((AudioClip)v, where); continue; }
                var list = v as IList;
                if (list != null)
                {
                    foreach (var el in list)
                    {
                        if (el is AudioClip) add((AudioClip)el, where);
                        else if (el is ScriptableObject || (el != null && !(el is Object) && el.GetType().IsClass)) CollectClips(el, where, add, depth + 1, visited);
                    }
                    continue;
                }
                if (v is ScriptableObject || (!(v is Object) && v.GetType().IsClass && !v.GetType().Namespace.StartsWith("System")))
                    CollectClips(v, where, add, depth + 1, visited);
            }
        }

        // ================================================================== QUAY

        void DrawRecordPage()
        {
            bool playing = Application.isPlaying;
            DrawMultiRecord();
            GUILayout.Space(10);
            GUILayout.Label("Màn hình sạch", stH2);
            if (!playing) GUILayout.Label("Bấm ▶ Play trước rồi dùng các nút dưới đây. Dừng Play là mọi thứ hiện lại như cũ.", stMuted);
            GUI.enabled = playing;
            EditorGUILayout.BeginHorizontal();
            if (Tinted("Ẩn joystick\n(vẫn điều khiển được)", Blue, stBig)) SetAll(true, true);
            if (Tinted("Ẩn tay hướng dẫn\n/ tutorial / CTA", Blue, stBig)) HideTutorial(true);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Ẩn toàn bộ UI", stBig)) HideAllCanvases(true);
            if (GUILayout.Button("Hiện lại tất cả", stBig)) { SetAll(false, false); HideAllCanvases(false); }
            EditorGUILayout.EndHorizontal();
            bool cur = GUILayout.Toggle(!Cursor.visible, "Ẩn con trỏ chuột trong Game view", "Button");
            if (cur == Cursor.visible) Cursor.visible = !cur;

            GUILayout.Space(8);
            GUILayout.Label("Tốc độ game", stH2);
            float ts = EditorGUILayout.Slider("Tốc độ", Time.timeScale, 0f, 3f);
            if (!Mathf.Approximately(ts, Time.timeScale)) Time.timeScale = ts;
            EditorGUILayout.BeginHorizontal();
            foreach (var v in new[] { 0.25f, 0.5f, 1f, 1.5f, 2f })
            {
                var old = GUI.backgroundColor;
                if (Mathf.Approximately(Time.timeScale, v)) GUI.backgroundColor = Blue;
                if (GUILayout.Button(v.ToString(CultureInfo.InvariantCulture) + "×", GUILayout.Height(26))) Time.timeScale = v;
                GUI.backgroundColor = old;
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(EditorApplication.isPaused ? "▶ Tiếp tục" : "❚❚ Tạm dừng", GUILayout.Height(26))) EditorApplication.isPaused = !EditorApplication.isPaused;
            if (GUILayout.Button("Tiến 1 frame", GUILayout.Height(26))) EditorApplication.Step();
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(8);
            GUILayout.Label("Ảnh chụp (thumbnail)", stH2);
            EditorGUILayout.BeginHorizontal();
            foreach (var s in new[] { 1, 2, 4 })
                if (GUILayout.Button("Chụp ×" + s, GUILayout.Height(26))) Screenshot(s);
            EditorGUILayout.EndHorizontal();
            GUI.enabled = true;
            GUILayout.Label("Ảnh lưu ở thư mục Screenshots/ cạnh Assets. Quay video: Window ▸ General ▸ Recorder (package Unity Recorder) hoặc phần mềm quay màn hình; " +
                            "đặt Game view đúng tỉ lệ 9:16 / 16:9 trước khi quay.", stMuted);
        }

        void HideTutorial(bool hide)
        {
            if (uiItems == null) ScanUi();
            foreach (var it in uiItems.Where(x => x.go != null && !x.joystick)) SetHidden(it.go, hide);
        }
    }
}
