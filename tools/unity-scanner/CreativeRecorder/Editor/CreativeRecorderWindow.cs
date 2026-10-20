using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace CreativeRecorder
{
    /// <summary>
    /// Tools ▸ Creative Recorder — cửa sổ hỗ trợ quay creative trong project playable:
    /// logic game, tuỳ chỉnh mọi option, phím tắt, ẩn joystick/UI, đổi skin, đổi texture, gắn SFX, chỉnh tốc độ khi quay.
    /// Tab Logic game và Tuỳ chỉnh nằm ở CreativeRecorderWindow.Logic.cs / .Options.cs.
    /// </summary>
    public partial class CreativeRecorderWindow : EditorWindow
    {
        const string GeneratedFolder = "Assets/CreativeRecorder/Generated";

        static readonly string[] TabNames = { "Logic game", "Tuỳ chỉnh", "Phím tắt", "Joystick & UI", "Skin", "Texture", "SFX", "Quay" };
        static readonly Regex JoyRx = new Regex(@"joystick|joy[_ ]?stick|onscreenstick|virtualstick|thumbstick|touchstick|analogstick|stickcontroller", RegexOptions.IgnoreCase);
        static readonly Regex HideRx = new Regex(@"\b(hand|tutorial|tut|finger|pointer|cursor|cta|download|install|logo|end ?card|packshot|banner|fps|debug|play ?now|store|watermark|hint|guide)\b", RegexOptions.IgnoreCase);
        static readonly Regex SkinRx = new Regex(@"\b(skins?|outfits?|costumes?|cosmetics?|hats?|helmet|clothe?s?|appearance|variants?|themes?|palette|avatars?|characters?|heroe?s?|weapons?|hair|suit|armou?r|cars?|vehicles?)\b", RegexOptions.IgnoreCase);
        static readonly Regex[] KeyRx =
        {
            new Regex(@"Input\s*\.\s*(GetKeyDown|GetKeyUp|GetKey)\s*\(\s*KeyCode\s*\.\s*(\w+)"),
            new Regex(@"Input\s*\.\s*(GetKeyDown|GetKeyUp|GetKey)\s*\(\s*""([^""]+)"""),
            new Regex(@"Input\s*\.\s*(GetKeyDown|GetKeyUp|GetKey)\s*\(\s*([A-Za-z_][\w\.]*)\s*\)"),
            new Regex(@"Keyboard\s*\.\s*current\s*\.\s*(\w+?)Key\s*\.\s*(wasPressedThisFrame|isPressed|wasReleasedThisFrame)"),
            new Regex(@"Input\s*\.\s*(GetButtonDown|GetButtonUp|GetButton)\s*\(\s*""([^""]+)"""),
            new Regex(@"Input\s*\.\s*(GetMouseButtonDown|GetMouseButtonUp|GetMouseButton)\s*\(\s*(\d)"),
        };
        static readonly Regex MethodRx = new Regex(@"^\s*(?:\[[^\]]*\]\s*)*(?:(?:public|private|protected|internal|static|virtual|override|async|sealed|new)\s+)*[\w<>\[\],\.]+\s+(\w+)\s*\([^;]*$");
        static readonly Regex KeyFieldRx = new Regex(@"KeyCode\s+(\w+)\s*=\s*KeyCode\s*\.\s*(\w+)");
        static readonly HashSet<string> NotMethods = new HashSet<string> { "if", "for", "foreach", "while", "switch", "catch", "using", "lock", "return", "new", "else" };

        [MenuItem("Tools/Creative Recorder")]
        public static void Open()
        {
            var w = GetWindow<CreativeRecorderWindow>("Creative Recorder");
            w.minSize = new Vector2(430, 320);
        }

        int tab;
        Vector2 scroll;

        void OnSelectionChange() { Repaint(); }
        void OnHierarchyChange() { Repaint(); }


        void OnGUI()
        {
            if (!advanced)
            {
                DrawStudio();
                return;
            }
            if (GUILayout.Button("← Chế độ đơn giản", EditorStyles.miniButton)) { SetAdvanced(false); return; }
            tab = GUILayout.SelectionGrid(tab, TabNames, 4, GUILayout.Height(44));
            if (Application.isPlaying)
                EditorGUILayout.HelpBox("Đang Play: mọi thay đổi sẽ tự mất khi dừng Play — an toàn để quay.", MessageType.None);
            else
                EditorGUILayout.HelpBox("Đang ở Edit mode: thay đổi được lưu vào scene (Ctrl+Z để hoàn tác). Nên bấm Play rồi mới chỉnh.", MessageType.Info);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            switch (tab)
            {
                case 0: DrawLogic(); break;
                case OptionsTab: DrawOptions(); break;
                case 2: DrawHotkeys(); break;
                case 3: DrawUi(); break;
                case 4: DrawSkin(); break;
                case 5: DrawTexture(); break;
                case 6: DrawSfx(); break;
                case 7: DrawRecord(); break;
            }
            EditorGUILayout.EndScrollView();
        }

        // ------------------------------------------------------------------ Phím tắt

        class Hotkey
        {
            public string key, how, file, method, action;
            public int line;
            public bool editorOnly, vendor;
        }

        List<Hotkey> hotkeys;
        string hotkeyFilter = "";
        bool showVendor;

        void DrawHotkeys()
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(hotkeys == null ? "Quét script tìm phím tắt" : "Quét lại", GUILayout.Height(26))) ScanHotkeys();
            GUI.enabled = hotkeys != null && hotkeys.Count > 0;
            if (GUILayout.Button("Copy danh sách", GUILayout.Width(110), GUILayout.Height(26))) CopyHotkeys();
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
            if (hotkeys == null)
            {
                EditorGUILayout.HelpBox("Quét mọi file .cs trong Assets để tìm Input.GetKey…, Keyboard.current…, GetButton, GetMouseButton.\n" +
                                        "Báo cáo đầy đủ hơn (skin, texture, âm thanh) — chạy unity_scan.py.", MessageType.None);
                return;
            }
            hotkeyFilter = EditorGUILayout.TextField("Lọc", hotkeyFilter);
            showVendor = EditorGUILayout.ToggleLeft("Hiện cả phím trong thư viện bên thứ 3 (Plugins, Joystick Pack…)", showVendor);
            var list = hotkeys.Where(h => (showVendor || !h.vendor) &&
                                          (string.IsNullOrEmpty(hotkeyFilter) ||
                                           (h.key + h.file + h.method + h.action).IndexOf(hotkeyFilter, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
            if (list.Count == 0) EditorGUILayout.HelpBox("Không thấy phím tắt nào.", MessageType.None);
            var keyStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
            foreach (var h in list)
            {
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                GUILayout.Label(h.key, keyStyle, GUILayout.Width(110));
                EditorGUILayout.BeginVertical();
                GUILayout.Label(h.how + (string.IsNullOrEmpty(h.action) ? "" : "  →  " + h.action), EditorStyles.wordWrappedLabel);
                GUILayout.Label(Path.GetFileName(h.file) + ":" + h.line + (h.method != null ? "  ·  " + h.method + "()" : "") +
                                (h.editorOnly ? "  ·  chỉ Editor" : ""), EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();
                if (GUILayout.Button("Mở", GUILayout.Width(40)))
                    AssetDatabase.OpenAsset(AssetDatabase.LoadAssetAtPath<MonoScript>(h.file), h.line);
                EditorGUILayout.EndHorizontal();
            }
        }

        void ScanHotkeys()
        {
            hotkeys = new List<Hotkey>();
            string root = Application.dataPath;
            foreach (var path in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                string rel = "Assets" + path.Substring(root.Length).Replace('\\', '/');
                if (rel.Contains("/Editor/") || rel.Contains("/CreativeRecorder/")) continue;
                string[] lines;
                try { lines = File.ReadAllLines(path); } catch (IOException) { continue; }
                bool vendor = IsVendor(rel);
                var keyFields = new Dictionary<string, string>();
                foreach (var l in lines)
                {
                    var m = KeyFieldRx.Match(l);
                    if (m.Success) keyFields[m.Groups[1].Value] = m.Groups[2].Value;
                }
                string method = null;
                var ifStack = new List<bool>();
                bool inComment = false;
                for (int i = 0; i < lines.Length; i++)
                {
                    string l = StripComments(lines[i], ref inComment);
                    string t = l.Trim();
                    if (t.StartsWith("#if")) { ifStack.Add(Regex.IsMatch(t, @"(?<!!)\bUNITY_EDITOR\b")); continue; }
                    if (t.StartsWith("#el")) { if (ifStack.Count > 0) ifStack[ifStack.Count - 1] = false; continue; }
                    if (t.StartsWith("#endif")) { if (ifStack.Count > 0) ifStack.RemoveAt(ifStack.Count - 1); continue; }
                    var mm = MethodRx.Match(l);
                    if (mm.Success && !NotMethods.Contains(mm.Groups[1].Value)) method = mm.Groups[1].Value;
                    for (int p = 0; p < KeyRx.Length; p++)
                    {
                        foreach (Match m in KeyRx[p].Matches(l))
                        {
                            string how = m.Groups[1].Value, key;
                            switch (p)
                            {
                                case 0: key = PrettyKey(m.Groups[2].Value); break;
                                case 1: key = PrettyKey(m.Groups[2].Value); break;
                                case 2:
                                    string v = m.Groups[2].Value;
                                    if (v.StartsWith("KeyCode")) continue;
                                    string dflt;
                                    key = keyFields.TryGetValue(v.Split('.').Last(), out dflt) ? PrettyKey(dflt) + " (biến " + v + ")" : "(biến " + v + ")";
                                    break;
                                case 3:
                                    key = PrettyKey(m.Groups[1].Value);
                                    how = m.Groups[2].Value == "isPressed" ? "GetKey" : m.Groups[2].Value == "wasPressedThisFrame" ? "GetKeyDown" : "GetKeyUp";
                                    break;
                                case 4: key = "\"" + m.Groups[2].Value + "\""; break;
                                default: key = m.Groups[2].Value == "0" ? "Chuột trái" : m.Groups[2].Value == "1" ? "Chuột phải" : "Chuột giữa"; break;
                            }
                            hotkeys.Add(new Hotkey
                            {
                                key = key, how = HowVi(how), file = rel, line = i + 1, method = method,
                                action = ActionAfter(lines, i, l.Substring(m.Index)),
                                editorOnly = ifStack.Contains(true), vendor = vendor
                            });
                        }
                    }
                }
            }
            hotkeys = hotkeys.OrderBy(h => h.vendor).ThenBy(h => h.key.StartsWith("Chuột")).ThenBy(h => h.key).ToList();
        }

        void CopyHotkeys()
        {
            var sb = new StringBuilder("Phím tắt — " + Application.productName + "\n");
            foreach (var h in hotkeys.Where(x => !x.vendor))
                sb.AppendFormat("[{0}] {1} → {2}   ({3}:{4}{5})\n", h.key, h.how, h.action, Path.GetFileName(h.file), h.line, h.editorOnly ? ", chỉ Editor" : "");
            EditorGUIUtility.systemCopyBuffer = sb.ToString();
            ShowNotification(new GUIContent("Đã copy"));
        }

        static string StripComments(string line, ref bool inBlock)
        {
            var sb = new StringBuilder();
            bool inStr = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i], n = i + 1 < line.Length ? line[i + 1] : '\0';
                if (inBlock)
                {
                    if (c == '*' && n == '/') { inBlock = false; i++; }
                    continue;
                }
                if (!inStr && c == '/' && n == '/') break;
                if (!inStr && c == '/' && n == '*') { inBlock = true; i++; continue; }
                if (c == '"' && (i == 0 || line[i - 1] != '\\')) inStr = !inStr;
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>Câu lệnh chạy khi điều kiện đúng: phần sau dấu ')' đóng của if, hoặc dòng kế tiếp.</summary>
        static string ActionAfter(string[] lines, int index, string fromMatch)
        {
            int depth = 0, cut = -1;
            for (int i = 0; i < fromMatch.Length; i++)
            {
                if (fromMatch[i] == '(') depth++;
                else if (fromMatch[i] == ')') { depth--; if (depth < 0) { cut = i + 1; break; } }
            }
            string rest = cut > 0 ? fromMatch.Substring(cut) : "";
            rest = rest.Trim().TrimStart('{').Trim();
            for (int k = index + 1; rest.Length == 0 && k < lines.Length && k <= index + 4; k++)
            {
                string t = lines[k].Trim();
                if (t == "{" || t.Length == 0) continue;
                rest = t;
            }
            rest = rest.TrimEnd('}').Trim();
            if (rest.StartsWith("Debug.Log") && index + 2 < lines.Length)
            {
                string next = lines[index + 2].Trim().TrimEnd('}').Trim();
                if (next.Length > 0 && next != "{") rest += "  " + next;
            }
            return rest.Length > 90 ? rest.Substring(0, 90) + "…" : rest;
        }

        static string HowVi(string how)
        {
            if (how.EndsWith("Down")) return "nhấn";
            if (how.EndsWith("Up")) return "thả";
            return "giữ";
        }

        static string PrettyKey(string k)
        {
            if (k.StartsWith("Alpha") && k.Length == 6) return k.Substring(5);
            if (k.StartsWith("digit") && k.Length == 6) return k.Substring(5);
            if (k.StartsWith("Keypad") && k.Length == 7) return "Numpad " + k.Substring(6);
            switch (k.ToLowerInvariant())
            {
                case "return": case "enter": return "Enter";
                case "escape": return "Esc";
                case "uparrow": case "up": return "↑";
                case "downarrow": case "down": return "↓";
                case "leftarrow": case "left": return "←";
                case "rightarrow": case "right": return "→";
                case "mouse0": return "Chuột trái";
                case "mouse1": return "Chuột phải";
                case "space": return "Space";
            }
            return k.Length == 1 ? k.ToUpperInvariant() : char.ToUpperInvariant(k[0]) + k.Substring(1);
        }

        static bool IsVendor(string rel)
        {
            string low = rel.ToLowerInvariant();
            foreach (var v in new[] { "/plugins/", "/thirdparty/", "/3rdparty/", "/textmesh pro/", "/joystick pack/", "/demigiant/", "/samples/", "/demo/", "/examples/", "/luna" })
                if (low.Contains(v)) return true;
            return false;
        }

        // ------------------------------------------------------------------ Joystick & UI

        class UiItem
        {
            public GameObject go;
            public string path;
            public bool joystick;
        }

        List<UiItem> uiItems;

        void DrawUi()
        {
            if (GUILayout.Button(uiItems == null ? "Tìm joystick & UI trong scene" : "Tìm lại", GUILayout.Height(26))) ScanUi();
            EditorGUILayout.HelpBox("“Ẩn (vẫn chạy)” đặt CanvasGroup.alpha = 0 (UI) hoặc tắt Renderer (object 3D/2D): " +
                                    "joystick vô hình nhưng vẫn kéo được để điều khiển. “Tắt hẳn” = SetActive(false), input cũng mất.", MessageType.None);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Ẩn tất cả joystick")) SetAll(true, true);
            if (GUILayout.Button("Hiện lại tất cả")) SetAll(false, false);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Ẩn toàn bộ UI (Canvas)")) HideAllCanvases(true);
            if (GUILayout.Button("Hiện toàn bộ UI")) HideAllCanvases(false);
            EditorGUILayout.EndHorizontal();
            if (uiItems == null) return;
            uiItems.RemoveAll(x => x.go == null);
            if (uiItems.Count == 0) EditorGUILayout.HelpBox("Không thấy joystick hay UI hướng dẫn trong các scene đang mở.", MessageType.None);
            bool? lastJoy = null;
            foreach (var it in uiItems)
            {
                if (lastJoy != it.joystick)
                {
                    GUILayout.Label(it.joystick ? "Joystick" : "UI thường cần ẩn khi quay (tay hướng dẫn, CTA, logo…)", EditorStyles.boldLabel);
                    lastJoy = it.joystick;
                }
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                if (GUILayout.Button(it.go.name, EditorStyles.label, GUILayout.MinWidth(80)))
                {
                    Selection.activeGameObject = it.go;
                    EditorGUIUtility.PingObject(it.go);
                }
                GUILayout.FlexibleSpace();
                bool hidden = IsHidden(it.go);
                bool nh = GUILayout.Toggle(hidden, "Ẩn (vẫn chạy)", "Button", GUILayout.Width(100));
                if (nh != hidden) SetHidden(it.go, nh);
                bool off = !it.go.activeSelf;
                bool noff = GUILayout.Toggle(off, "Tắt hẳn", "Button", GUILayout.Width(64));
                if (noff != off)
                {
                    Record(it.go, "Toggle active");
                    it.go.SetActive(!noff);
                    MarkDirty(it.go);
                }
                EditorGUILayout.EndHorizontal();
                GUILayout.Label(it.path, EditorStyles.miniLabel);
            }
        }

        void ScanUi()
        {
            uiItems = new List<UiItem>();
            foreach (var go in SceneObjects())
            {
                bool joy = JoyRx.IsMatch(go.name) || go.GetComponents<Component>().Any(c => c != null && JoyRx.IsMatch(c.GetType().Name));
                bool hide = !joy && HideRx.IsMatch(SplitWords(go.name)) && !go.name.StartsWith("EventSystem");
                if (!joy && !hide) continue;
                // Bỏ object con nằm trong joystick khác (Background, Handle…)
                if (joy && go.transform.parent != null && go.transform.parent.GetComponentsInParent<Component>(true)
                        .Any(c => c != null && JoyRx.IsMatch(c.GetType().Name))) continue;
                uiItems.Add(new UiItem { go = go, path = PathOf(go.transform), joystick = joy });
            }
            uiItems = uiItems.OrderByDescending(x => x.joystick).ThenBy(x => x.path).ToList();
        }

        void SetAll(bool hide, bool joystickOnly)
        {
            if (uiItems == null) ScanUi();
            foreach (var it in uiItems.Where(x => x.go != null && (!joystickOnly || x.joystick))) SetHidden(it.go, hide);
        }

        void HideAllCanvases(bool hide)
        {
            foreach (var go in SceneObjects())
            {
                var c = go.GetComponent<Canvas>();
                if (c != null && c.isRootCanvas) SetHidden(go, hide);
            }
        }

        static bool IsHidden(GameObject go)
        {
            if (go.GetComponent<RectTransform>() != null)
            {
                var cg = go.GetComponent<CanvasGroup>();
                return cg != null && cg.alpha <= 0.001f;
            }
            var rs = go.GetComponentsInChildren<Renderer>(true);
            return rs.Length > 0 && rs.All(r => !r.enabled);
        }

        static void SetHidden(GameObject go, bool hide)
        {
            if (go.GetComponent<RectTransform>() != null)
            {
                var cg = go.GetComponent<CanvasGroup>();
                if (cg == null)
                {
                    if (!hide) return;
                    cg = Application.isPlaying ? go.AddComponent<CanvasGroup>() : Undo.AddComponent<CanvasGroup>(go);
                }
                Record(cg, "Hide UI");
                cg.alpha = hide ? 0f : 1f;
            }
            else
            {
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    Record(r, "Hide renderer");
                    r.enabled = !hide;
                }
            }
            MarkDirty(go);
        }

        // ------------------------------------------------------------------ Skin

        int skinPick;

        void DrawSkin()
        {
            var sel = Selection.activeGameObject;
            if (sel == null || EditorUtility.IsPersistent(sel))
            {
                EditorGUILayout.HelpBox("Chọn nhân vật / object trong Hierarchy để đổi skin.\n" +
                                        "Tab này hiện mọi Material, Mesh, Sprite trên object và các con, cùng danh sách skin có sẵn trong script.", MessageType.Info);
                return;
            }
            GUILayout.Label(sel.name, EditorStyles.largeLabel);

            // 1. Danh sách skin có sẵn trong script (mảng Material/Sprite/Mesh có tên skin…)
            bool anyList = false;
            foreach (var mb in sel.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                var so = new SerializedObject(mb);
                var it = so.GetIterator();
                bool enter = true;
                while (it.NextVisible(enter))
                {
                    enter = false;
                    if (!it.isArray || it.propertyType == SerializedPropertyType.String || it.arraySize == 0) continue;
                    if (!it.arrayElementType.StartsWith("PPtr<")) continue;
                    if (!SkinRx.IsMatch(SplitWords(it.name))) continue;
                    if (!anyList) GUILayout.Label("Skin có sẵn trong script", EditorStyles.boldLabel);
                    anyList = true;
                    GUILayout.Label(mb.GetType().Name + "." + it.name + "  (" + it.arraySize + ")", EditorStyles.miniBoldLabel);
                    for (int i = 0; i < it.arraySize; i++)
                    {
                        var el = it.GetArrayElementAtIndex(i).objectReferenceValue;
                        EditorGUILayout.BeginHorizontal();
                        GUILayout.Label("#" + i, GUILayout.Width(28));
                        EditorGUILayout.ObjectField(el, typeof(Object), true);
                        GUI.enabled = el != null && (el is Material || el is Sprite || el is Mesh);
                        if (GUILayout.Button("Áp dụng", GUILayout.Width(64))) ApplySkinAsset(sel, el);
                        GUI.enabled = true;
                        EditorGUILayout.EndHorizontal();
                    }
                }
            }

            // 2. Hàm đổi skin trong script (SetSkin(int), NextSkin()…) — gọi khi đang Play
            bool anyMethod = false;
            foreach (var mb in sel.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                foreach (var m in mb.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (!SkinRx.IsMatch(SplitWords(m.Name))) continue;
                    var ps = m.GetParameters();
                    if (ps.Length > 1 || (ps.Length == 1 && ps[0].ParameterType != typeof(int))) continue;
                    if (!anyMethod) GUILayout.Label("Hàm đổi skin trong script (bấm khi đang Play)", EditorStyles.boldLabel);
                    anyMethod = true;
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label(mb.GetType().Name + "." + m.Name + "(" + (ps.Length == 1 ? "int" : "") + ")");
                    if (ps.Length == 1) skinPick = EditorGUILayout.IntField(skinPick, GUILayout.Width(40));
                    GUI.enabled = Application.isPlaying;
                    if (GUILayout.Button("Gọi", GUILayout.Width(44)))
                    {
                        try { m.Invoke(mb, ps.Length == 1 ? new object[] { skinPick } : null); }
                        catch (Exception e) { Debug.LogException(e.InnerException ?? e); }
                    }
                    GUI.enabled = true;
                    EditorGUILayout.EndHorizontal();
                }
            }

            // 3. Mọi Material / Mesh / Sprite trên object và con — kéo thả để thay
            GUILayout.Space(6);
            GUILayout.Label("Thay trực tiếp (kéo thả material / mesh / sprite vào ô)", EditorStyles.boldLabel);
            foreach (var c in sel.GetComponentsInChildren<Component>(true))
            {
                if (c == null || c is Transform || c is MonoBehaviour && !(c.GetType().Namespace ?? "").StartsWith("UnityEngine")) continue;
                var so = new SerializedObject(c);
                var mats = so.FindProperty("m_Materials");
                var mesh = so.FindProperty("m_Mesh");
                var sprite = so.FindProperty("m_Sprite");
                if (mesh != null && !(c is MeshFilter) && !(c is SkinnedMeshRenderer)) mesh = null;
                if (mats == null && mesh == null && sprite == null) continue;
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Label(PathOf(c.transform, sel.transform) + "  ·  " + c.GetType().Name, EditorStyles.miniBoldLabel);
                if (mesh != null) EditorGUILayout.PropertyField(mesh, new GUIContent("Mesh"));
                if (sprite != null) EditorGUILayout.PropertyField(sprite, new GUIContent("Sprite"));
                if (mats != null)
                    for (int i = 0; i < mats.arraySize; i++)
                        EditorGUILayout.PropertyField(mats.GetArrayElementAtIndex(i), new GUIContent("Material " + i));
                so.ApplyModifiedProperties();
                EditorGUILayout.EndVertical();
            }
        }

        static void ApplySkinAsset(GameObject root, Object asset)
        {
            var mat = asset as Material;
            var sprite = asset as Sprite;
            var mesh = asset as Mesh;
            if (mat != null)
            {
                var r = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Cast<Renderer>().FirstOrDefault()
                        ?? root.GetComponentsInChildren<Renderer>(true).FirstOrDefault(x => !(x is ParticleSystemRenderer));
                if (r == null) return;
                Record(r, "Apply skin");
                var arr = r.sharedMaterials;
                if (arr.Length == 0) arr = new Material[1];
                arr[0] = mat;
                r.sharedMaterials = arr;
                MarkDirty(r.gameObject);
            }
            else if (sprite != null)
            {
                foreach (var c in root.GetComponentsInChildren<Component>(true))
                {
                    if (c == null) continue;
                    var so = new SerializedObject(c);
                    var p = so.FindProperty("m_Sprite");
                    if (p == null) continue;
                    p.objectReferenceValue = sprite;
                    so.ApplyModifiedProperties();
                    return;
                }
            }
            else if (mesh != null)
            {
                var smr = root.GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (smr != null) { Record(smr, "Apply mesh"); smr.sharedMesh = mesh; MarkDirty(smr.gameObject); return; }
                var mf = root.GetComponentInChildren<MeshFilter>(true);
                if (mf != null) { Record(mf, "Apply mesh"); mf.sharedMesh = mesh; MarkDirty(mf.gameObject); }
            }
        }

        // ------------------------------------------------------------------ Texture

        bool editCopy = true;

        void DrawTexture()
        {
            var sel = Selection.activeGameObject;
            editCopy = EditorGUILayout.ToggleLeft("Sửa trên bản sao material (không đụng tới material gốc)", editCopy);
            if (sel == null || EditorUtility.IsPersistent(sel))
            {
                EditorGUILayout.HelpBox("Chọn object (nhân vật, mặt đất, map…) trong Hierarchy để thay texture.\n" +
                                        "Bản sao lưu ở " + GeneratedFolder + " (Edit mode) hoặc chỉ tồn tại trong lúc Play.", MessageType.Info);
                return;
            }
            foreach (var r in sel.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int slot = 0; slot < mats.Length; slot++)
                {
                    var mat = mats[slot];
                    if (mat == null) continue;
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label(PathOf(r.transform, sel.transform) + "  ·  " + mat.name, EditorStyles.miniBoldLabel);
                    if (GUILayout.Button("Chọn", EditorStyles.miniButton, GUILayout.Width(44))) EditorGUIUtility.PingObject(mat);
                    EditorGUILayout.EndHorizontal();
                    GUILayout.Label(mat.shader != null ? mat.shader.name : "", EditorStyles.miniLabel);
                    foreach (var prop in mat.GetTexturePropertyNames())
                    {
                        if (!mat.HasProperty(prop)) continue;
                        var tex = mat.GetTexture(prop);
                        var nt = (Texture)EditorGUILayout.ObjectField(prop, tex, typeof(Texture), false);
                        if (nt != tex)
                        {
                            var target = Editable(r, slot, mat);
                            Record(target, "Change texture");
                            target.SetTexture(prop, nt);
                            mat = target;
                        }
                        if (nt != null)
                        {
                            var sc = mat.GetTextureScale(prop);
                            var nsc = EditorGUILayout.Vector2Field("   Tiling", sc);
                            if (nsc != sc) { var tg = Editable(r, slot, mat); Record(tg, "Tiling"); tg.SetTextureScale(prop, nsc); mat = tg; }
                        }
                    }
                    foreach (var cp in new[] { "_BaseColor", "_Color", "_TintColor" })
                    {
                        if (!mat.HasProperty(cp)) continue;
                        var col = mat.GetColor(cp);
                        var nc = EditorGUILayout.ColorField(cp, col);
                        if (nc != col) { var tg = Editable(r, slot, mat); Record(tg, "Color"); tg.SetColor(cp, nc); }
                        break;
                    }
                    EditorGUILayout.EndVertical();
                }
            }
        }

        /// <summary>Material để sửa: bản gốc, hoặc bản sao gắn lại vào renderer nếu đang bật “Sửa trên bản sao”.</summary>
        Material Editable(Renderer r, int slot, Material mat)
        {
            if (!editCopy || mat.name.EndsWith("(creative)")) return mat;
            var copy = new Material(mat) { name = mat.name + " (creative)" };
            if (!Application.isPlaying)
            {
                EnsureFolder(GeneratedFolder);
                AssetDatabase.CreateAsset(copy, AssetDatabase.GenerateUniqueAssetPath(GeneratedFolder + "/" + Sanitize(mat.name) + "_creative.mat"));
            }
            Record(r, "Use material copy");
            var arr = r.sharedMaterials;
            arr[slot] = copy;
            r.sharedMaterials = arr;
            MarkDirty(r.gameObject);
            return copy;
        }

        // ------------------------------------------------------------------ SFX

        AudioClip sfxClip;
        CreativeSfxTrigger.Mode sfxMode = CreativeSfxTrigger.Mode.KeyDown;
        KeyCode sfxKey = KeyCode.Space;
        float sfxVolume = 1f;
        string sfxTag = "";
        string clipFilter = "";
        bool showClips;
        string[] clipGuids;

        void DrawSfx()
        {
            GUILayout.Label("Gắn SFX vào hành động (không cần sửa code)", EditorStyles.boldLabel);
            var sel = Selection.activeGameObject;
            sfxClip = (AudioClip)EditorGUILayout.ObjectField("Âm thanh", sfxClip, typeof(AudioClip), false);
            sfxMode = (CreativeSfxTrigger.Mode)EditorGUILayout.EnumPopup("Phát khi", sfxMode);
            if (sfxMode == CreativeSfxTrigger.Mode.KeyDown) sfxKey = (KeyCode)EditorGUILayout.EnumPopup("Phím", sfxKey);
            if (sfxMode == CreativeSfxTrigger.Mode.TriggerEnter || sfxMode == CreativeSfxTrigger.Mode.CollisionEnter)
                sfxTag = EditorGUILayout.TagField("Chỉ khi chạm tag", string.IsNullOrEmpty(sfxTag) ? "Untagged" : sfxTag);
            sfxVolume = EditorGUILayout.Slider("Âm lượng", sfxVolume, 0f, 1f);
            EditorGUILayout.BeginHorizontal();
            GUI.enabled = sfxClip != null;
            if (GUILayout.Button("▶ Nghe thử", GUILayout.Width(90))) PreviewClip(sfxClip);
            GUI.enabled = sfxClip != null && sel != null && !EditorUtility.IsPersistent(sel);
            if (GUILayout.Button(sel != null ? "Gắn vào “" + sel.name + "”" : "Chọn object trong Hierarchy")) AddTrigger(sel);
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox(SfxHint(), MessageType.None);

            showClips = EditorGUILayout.Foldout(showClips, "Âm thanh có trong project", true);
            if (showClips)
            {
                if (clipGuids == null || GUILayout.Button("Tải lại", EditorStyles.miniButton)) clipGuids = AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets" });
                clipFilter = EditorGUILayout.TextField("Lọc", clipFilter);
                foreach (var g in clipGuids)
                {
                    string p = AssetDatabase.GUIDToAssetPath(g);
                    if (!string.IsNullOrEmpty(clipFilter) && p.IndexOf(clipFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("▶", GUILayout.Width(24))) PreviewClip(AssetDatabase.LoadAssetAtPath<AudioClip>(p));
                    GUILayout.Label(Path.GetFileName(p), GUILayout.MinWidth(80));
                    if (GUILayout.Button("Dùng", EditorStyles.miniButton, GUILayout.Width(44))) sfxClip = AssetDatabase.LoadAssetAtPath<AudioClip>(p);
                    EditorGUILayout.EndHorizontal();
                }
            }

            GUILayout.Space(6);
            GUILayout.Label("SFX đã gắn", EditorStyles.boldLabel);
            var triggers = SceneObjects().SelectMany(g => g.GetComponents<CreativeSfxTrigger>()).ToList();
            if (triggers.Count == 0) GUILayout.Label("Chưa có.", EditorStyles.miniLabel);
            foreach (var t in triggers)
            {
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                if (GUILayout.Button(t.gameObject.name, EditorStyles.label, GUILayout.MinWidth(80))) Selection.activeGameObject = t.gameObject;
                GUILayout.Label(ModeLabel(t) + " → " + (t.clip != null ? t.clip.name : "(trống)"), EditorStyles.miniLabel);
                if (GUILayout.Button("Xoá", EditorStyles.miniButton, GUILayout.Width(40)))
                {
                    if (Application.isPlaying) Destroy(t); else Undo.DestroyObjectImmediate(t);
                    GUIUtility.ExitGUI();
                }
                EditorGUILayout.EndHorizontal();
            }

            GUILayout.Space(6);
            GUILayout.Label("AudioSource trong scene", EditorStyles.boldLabel);
            foreach (var go in SceneObjects())
            {
                foreach (var src in go.GetComponents<AudioSource>())
                {
                    if ((src.hideFlags & HideFlags.HideInInspector) != 0) continue;
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button(go.name, EditorStyles.label, GUILayout.MinWidth(80))) Selection.activeGameObject = go;
                    GUILayout.Label((src.clip != null ? src.clip.name : "(trống)") + (src.playOnAwake ? " · PlayOnAwake" : "") + (src.loop ? " · loop" : ""), EditorStyles.miniLabel);
                    GUI.enabled = src.clip != null;
                    if (GUILayout.Button("▶", GUILayout.Width(24)))
                    {
                        if (Application.isPlaying) src.Play(); else PreviewClip(src.clip);
                    }
                    GUI.enabled = true;
                    bool mute = GUILayout.Toggle(src.mute, "Tắt tiếng", "Button", GUILayout.Width(70));
                    if (mute != src.mute) { Record(src, "Mute"); src.mute = mute; MarkDirty(go); }
                    EditorGUILayout.EndHorizontal();
                }
            }
        }

        string SfxHint()
        {
            switch (sfxMode)
            {
                case CreativeSfxTrigger.Mode.KeyDown: return "Nhấn phím trong Game view khi đang Play là phát.";
                case CreativeSfxTrigger.Mode.MouseOrTap: return "Mỗi lần click / chạm màn hình là phát. Gắn vào object nào cũng được.";
                case CreativeSfxTrigger.Mode.OnEnable: return "Phát khi object được bật — hợp cho popup, end card, hiệu ứng (gắn vào chính object đó).";
                case CreativeSfxTrigger.Mode.OnDisable: return "Phát khi object bị tắt/huỷ — hợp cho coin, item bị nhặt.";
                case CreativeSfxTrigger.Mode.TriggerEnter: return "Gắn vào object có Collider (Is Trigger): phát khi có object khác chạm vào.";
                case CreativeSfxTrigger.Mode.CollisionEnter: return "Gắn vào object có Collider + Rigidbody: phát khi va chạm.";
                default: return "Nếu object có Button, tool tự nối onClick → Play(). Cũng gọi được từ Animation Event / Timeline.\n" +
                                "Muốn chèn vào code: CreativeRecorder.CreativeSfxTrigger.PlayClip(clip);";
            }
        }

        static string ModeLabel(CreativeSfxTrigger t)
        {
            switch (t.mode)
            {
                case CreativeSfxTrigger.Mode.KeyDown: return "Phím " + t.key;
                case CreativeSfxTrigger.Mode.MouseOrTap: return "Click/chạm";
                case CreativeSfxTrigger.Mode.OnEnable: return "Khi bật";
                case CreativeSfxTrigger.Mode.OnDisable: return "Khi tắt";
                case CreativeSfxTrigger.Mode.TriggerEnter: return "Trigger";
                case CreativeSfxTrigger.Mode.CollisionEnter: return "Va chạm";
                default: return "Button/Event";
            }
        }

        void AddTrigger(GameObject go)
        {
            var t = Application.isPlaying ? go.AddComponent<CreativeSfxTrigger>() : Undo.AddComponent<CreativeSfxTrigger>(go);
            t.clip = sfxClip;
            t.mode = sfxMode;
            t.key = sfxKey;
            t.volume = sfxVolume;
            t.requiredTag = sfxTag == "Untagged" ? "" : sfxTag;
            if (sfxMode == CreativeSfxTrigger.Mode.ManualCall)
            {
                var evt = FindClickEvent(go);
                if (evt != null)
                {
                    if (Application.isPlaying) evt.AddListener(t.Play);
                    else UnityEventTools.AddPersistentListener(evt, t.Play);
                    ShowNotification(new GUIContent("Đã nối Button.onClick → Play()"));
                }
            }
            MarkDirty(go);
        }

        /// <summary>Tìm UnityEvent onClick của Button (UGUI) bằng reflection để không phụ thuộc package UI.</summary>
        static UnityEvent FindClickEvent(GameObject go)
        {
            foreach (var c in go.GetComponents<Component>())
            {
                if (c == null) continue;
                for (var t = c.GetType(); t != null && t != typeof(MonoBehaviour); t = t.BaseType)
                {
                    var f = t.GetField("m_OnClick", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    if (f != null && typeof(UnityEvent).IsAssignableFrom(f.FieldType))
                    {
                        Record(c, "Hook onClick");
                        return (UnityEvent)f.GetValue(c);
                    }
                }
            }
            return null;
        }

        static void PreviewClip(AudioClip clip)
        {
            if (clip == null) return;
            var util = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
            if (util == null) return;
            const BindingFlags F = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var stop = util.GetMethod("StopAllPreviewClips", F) ?? util.GetMethod("StopAllClips", F);
            if (stop != null) stop.Invoke(null, null);
            var args3 = new[] { typeof(AudioClip), typeof(int), typeof(bool) };
            var play = util.GetMethod("PlayPreviewClip", F, null, args3, null) ?? util.GetMethod("PlayClip", F, null, args3, null);
            if (play != null) { play.Invoke(null, new object[] { clip, 0, false }); return; }
            play = util.GetMethod("PlayClip", F, null, new[] { typeof(AudioClip) }, null);
            if (play != null) play.Invoke(null, new object[] { clip });
        }

        // ------------------------------------------------------------------ Quay

        void DrawRecord()
        {
            GUILayout.Label("Tốc độ game", EditorStyles.boldLabel);
            float ts = EditorGUILayout.Slider("Time.timeScale", Time.timeScale, 0f, 3f);
            if (!Mathf.Approximately(ts, Time.timeScale)) Time.timeScale = ts;
            EditorGUILayout.BeginHorizontal();
            foreach (var v in new[] { 0.25f, 0.5f, 1f, 1.5f, 2f })
                if (GUILayout.Button(v + "×")) Time.timeScale = v;
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label("Play", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(Application.isPlaying ? "■ Dừng" : "▶ Play")) EditorApplication.isPlaying = !EditorApplication.isPlaying;
            GUI.enabled = Application.isPlaying;
            if (GUILayout.Button(EditorApplication.isPaused ? "Tiếp tục" : "❚❚ Tạm dừng")) EditorApplication.isPaused = !EditorApplication.isPaused;
            if (GUILayout.Button("Tiến 1 frame")) EditorApplication.Step();
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label("Màn hình sạch", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Ẩn joystick")) SetAll(true, true);
            if (GUILayout.Button("Ẩn toàn bộ UI")) HideAllCanvases(true);
            if (GUILayout.Button("Hiện lại")) { SetAll(false, false); HideAllCanvases(false); }
            EditorGUILayout.EndHorizontal();
            GUI.enabled = Application.isPlaying;
            bool cur = GUILayout.Toggle(!Cursor.visible, "Ẩn con trỏ chuột trong Game view", "Button");
            if (cur == Cursor.visible) Cursor.visible = !cur;
            GUI.enabled = true;

            GUILayout.Space(6);
            GUILayout.Label("Ảnh chụp (thumbnail)", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            GUI.enabled = Application.isPlaying;
            foreach (var s in new[] { 1, 2, 4 })
                if (GUILayout.Button("Chụp ×" + s)) Screenshot(s);
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox("Ảnh lưu ở thư mục Screenshots/ cạnh Assets. Quay video: Window ▸ General ▸ Recorder (package Unity Recorder) " +
                                    "hoặc phần mềm quay màn hình; đặt Game view đúng tỉ lệ 9:16 / 16:9 trước khi quay.", MessageType.None);
        }

        static void Screenshot(int scale)
        {
            string dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Screenshots");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, Application.productName + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");
            ScreenCapture.CaptureScreenshot(file, scale);
            Debug.Log("Creative Recorder: đã chụp " + file);
        }

        // ------------------------------------------------------------------ tiện ích

        static IEnumerable<GameObject> SceneObjects()
        {
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go == null || !go.scene.IsValid() || go.hideFlags != HideFlags.None || EditorUtility.IsPersistent(go)) continue;
                yield return go;
            }
        }

        static string PathOf(Transform t, Transform stop = null)
        {
            var parts = new List<string>();
            for (; t != null && t != stop; t = t.parent) parts.Add(t.name);
            if (stop != null && t == stop && parts.Count == 0) return stop.name;
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        static string SplitWords(string name)
        {
            string s = Regex.Replace(name, @"([a-z0-9])([A-Z])", "$1 $2");
            s = Regex.Replace(s, @"([A-Z]+)([A-Z][a-z])", "$1 $2");
            return Regex.Replace(s, @"[_\-\d\.]+", " ").ToLowerInvariant();
        }

        static string Sanitize(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static void Record(Object o, string what)
        {
            if (!Application.isPlaying && o != null) Undo.RecordObject(o, "Creative Recorder: " + what);
        }

        static void MarkDirty(GameObject go)
        {
            if (Application.isPlaying || go == null) return;
            EditorUtility.SetDirty(go);
            if (go.scene.IsValid()) EditorSceneManager.MarkSceneDirty(go.scene);
        }
    }
}
