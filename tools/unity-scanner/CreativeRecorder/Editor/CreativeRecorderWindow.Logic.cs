using System;
using System.Collections.Generic;
using System.IO;
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
    /// Tab "Logic game": quét mọi script của game, xếp vào nhóm logic (điều khiển, camera, kẻ địch…)
    /// và giải thích script đó làm gì, có tuỳ chọn gì, đang gắn ở đâu trong scene.
    /// </summary>
    public partial class CreativeRecorderWindow
    {
        class LogicCategory
        {
            public readonly string name, desc;
            public readonly Regex nameRx, codeRx;

            public LogicCategory(string name, string desc, string nameRx, string codeRx)
            {
                this.name = name;
                this.desc = desc;
                this.nameRx = new Regex(@"\b(" + nameRx + @")\b", RegexOptions.IgnoreCase);
                this.codeRx = new Regex(codeRx);
            }
        }

        static readonly LogicCategory[] Categories =
        {
            new LogicCategory("Điều khiển nhân vật", "Nhận input (joystick, vuốt, chạm, phím) và di chuyển nhân vật chính. Chỉnh tốc độ, lực nhảy… ở đây sẽ đổi cảm giác điều khiển khi quay.",
                "player|controller|movement|move|mover|character|hero|runner|ball|car|drive|driver|swipe|drag|plane|ship",
                @"GetAxis|velocity|CharacterController|MovePosition|AddForce|Joystick|\.Horizontal|\.Vertical"),
            new LogicCategory("Camera", "Camera bám theo nhân vật, rung, zoom, đổi góc. Đổi offset / khoảng cách / góc nhìn để có khung hình đẹp hơn.",
                "camera|cam|follow|shake|zoom|cinemachine|view",
                @"Camera\.main|fieldOfView|orthographicSize|LookAt|Cinemachine|SmoothDamp"),
            new LogicCategory("Kẻ địch / AI", "Hành vi đối thủ: đuổi theo, tấn công, tuần tra. Chỉnh máu, tốc độ, sát thương để trận đấu dễ / khó, kịch tính hơn.",
                "enemy|enemies|ai|boss|zombie|monster|bot|npc|mob|opponent|creep|minion",
                @"NavMeshAgent|SetDestination|[Cc]hase|[Aa]ttack|[Pp]atrol"),
            new LogicCategory("Sinh object (Spawner)", "Tạo object theo thời gian / vị trí: địch, item, chướng ngại vật. Chỉnh số lượng và nhịp spawn để màn hình đông hay vắng.",
                "spawn|spawner|spawning|generator|pool|pooler|wave|waves|emitter",
                @"Instantiate|InvokeRepeating|[Ss]pawn[A-Z]\w*|ObjectPool"),
            new LogicCategory("Luồng game (Game Manager)", "Điều khiển trạng thái game: bắt đầu, thắng, thua, qua màn, hiện end card. Thường chứa phím cheat Win / Lose.",
                "game|manager|state|states|flow|director|session|match|round|loop",
                @"\bWin\b|\bLose\b|GameOver|LoadScene|timeScale|Victory|\bFail\b|IsGameOver"),
            new LogicCategory("Tiền & phần thưởng", "Coin, gem, điểm, nâng cấp, cửa hàng. Chỉnh tiền ban đầu / giá để quay cảnh nâng cấp, mua đồ nhanh.",
                "coin|coins|money|gem|gems|currency|cash|gold|reward|rewards|shop|store|upgrade|upgrades|wallet|score|price|economy|bank",
                @"[Cc]oins?\b|[Mm]oney|[Gg]old\b|[Ss]core|[Pp]rice|[Cc]ost\b"),
            new LogicCategory("Hướng dẫn (Tutorial)", "Tay chỉ, mũi tên, chữ hướng dẫn đầu playable. Thường cần tắt khi quay creative.",
                "tutorial|tut|hand|hint|guide|finger|onboarding|pointer|arrow",
                @"[Tt]utorial|[Hh]and\b"),
            new LogicCategory("End card / CTA / SDK quảng cáo", "Màn kết, nút tải game, gọi SDK playable (Luna, MRAID, ironSource…). Không ảnh hưởng gameplay; thường ẩn khi quay.",
                "endcard|end card|cta|install|download|packshot|luna|playable|mraid|ad|ads|redirect",
                @"Luna|Playable|mraid|MRAID|OpenURL|InstallFullGame|LunaPlaygroundField|GameEnded|ClickToStore"),
            new LogicCategory("Giao diện (UI)", "Thanh máu, điểm, nút bấm, popup. Ẩn / hiện, đổi chữ, đổi màu cho khung hình sạch.",
                "ui|hud|panel|popup|button|bar|menu|screen|text|label|canvas|window|dialog|counter|indicator|progress",
                @"UnityEngine\.UI|TMPro|TextMeshPro|\bImage\b|\bSlider\b|CanvasGroup|fillAmount"),
            new LogicCategory("Âm thanh", "Quản lý nhạc nền và SFX. Thay clip, chỉnh âm lượng ở đây.",
                "audio|sound|sounds|sfx|music|bgm",
                @"AudioSource|AudioClip|PlayOneShot"),
            new LogicCategory("Hiệu ứng (VFX / feedback)", "Particle, rung màn hình, nảy, nhấp nháy, tween. Tăng / giảm để cảnh quay “đã” hơn.",
                "effect|effects|fx|vfx|particle|particles|shake|juice|feedback|tween|flash|trail|blink|bounce|squash",
                @"ParticleSystem|DOTween|\.DO[A-Z]\w*\(|LeanTween|\.Emit\("),
            new LogicCategory("Vật lý / va chạm", "Va chạm, nhặt đồ, trúng đòn, rơi, nổ. Chỉnh lực, khối lượng, phạm vi.",
                "collision|collider|trigger|physics|bullet|projectile|obstacle|obstacles|barricade|barrier|missile|rocket|trap|spike|laser|damage|hit|hitbox|health|explosion|bomb|weapon|gun",
                @"OnTriggerEnter|OnCollisionEnter|Rigidbody|Physics\.|Raycast|TakeDamage"),
            new LogicCategory("Animation", "Điều khiển Animator / clip animation của nhân vật, object.",
                "anim|animation|animator|animated",
                @"Animator\b|SetTrigger|SetBool|SetFloat|\.Play\("""),
            new LogicCategory("Màn chơi / Map", "Dựng màn, đường đi, ô lưới, môi trường. Đổi theme map, độ dài màn.",
                "level|levels|map|track|tile|tiles|grid|road|stage|world|environment|terrain|platform|board|chunk|segment|path",
                @"[Tt]ile|[Gg]rid\b|[Ll]evel"),
            new LogicCategory("Skin / tuỳ biến", "Danh sách skin, đổi trang phục, màu, vũ khí, xe.",
                "skin|skins|outfit|costume|cosmetic|customize|customization|wardrobe|appearance",
                @"[Ss]kin|Material\[\]|sharedMaterial"),
            new LogicCategory("Input / Joystick", "Đọc joystick, chạm, vuốt và chuyển cho script khác dùng.",
                "input|joystick|touch|swipe|gesture|tap|stick",
                @"Input\.|IPointerDownHandler|IDragHandler|Touchscreen|Joystick"),
            new LogicCategory("Dữ liệu / cấu hình", "ScriptableObject hoặc class chứa thông số (máu, giá, danh sách level). Sửa ở đây đổi cho cả game.",
                "data|config|configs|settings|database|db|definition|info|table|preset|so",
                @"ScriptableObject|CreateAssetMenu|\[Serializable\]|\[System\.Serializable\]"),
            new LogicCategory("Tiện ích", "Code dùng chung: singleton, extension, helper. Thường không cần đụng tới.",
                "util|utils|utility|helper|helpers|extension|extensions|singleton|tools|common|base",
                @"static class|Singleton<"),
        };

        static readonly LogicCategory OtherCategory = new LogicCategory("Khác", "Chưa phân loại được — bấm Mở để xem code.", "$^", "$^");

        static readonly string[][] FactPatterns =
        {
            new[] { @"Joystick|\.Horizontal\b", "Dùng joystick" },
            new[] { @"GetMouseButton|touchCount|GetTouch|IPointer(Down|Click|Up)Handler|IDragHandler", "Đọc chạm / chuột" },
            new[] { @"Instantiate\s*\(", "Tạo object mới (Instantiate)" },
            new[] { @"Destroy\s*\(", "Xoá object (Destroy)" },
            new[] { @"PlayOneShot|AudioSource|PlayClipAtPoint", "Phát âm thanh" },
            new[] { @"ParticleSystem|\.Emit\(", "Bật particle / VFX" },
            new[] { @"DOTween|\.DO[A-Z]\w*\(|LeanTween", "Dùng tween (DOTween / LeanTween)" },
            new[] { @"OnTriggerEnter|OnCollisionEnter", "Xử lý va chạm" },
            new[] { @"LoadScene", "Đổi scene / chơi lại" },
            new[] { @"timeScale", "Đổi Time.timeScale (slow-mo / dừng game)" },
            new[] { @"StartCoroutine|WaitForSeconds|Invoke\(", "Chạy theo thời gian (coroutine / Invoke)" },
            new[] { @"Animator|SetTrigger|SetBool", "Điều khiển Animator" },
            new[] { @"NavMeshAgent", "Tìm đường bằng NavMesh" },
            new[] { @"static\s+\w+\s+Instance\b|Instance\s*=\s*this", "Singleton (gọi qua .Instance)" },
            new[] { @"PlayerPrefs", "Lưu dữ liệu (PlayerPrefs)" },
            new[] { @"Luna|mraid|MRAID|OpenURL|InstallFullGame", "Gọi SDK playable / mở store" },
            new[] { @"SetActive\s*\(\s*true", "Bật object khác (popup, end card…)" },
            new[] { @"Camera\.main", "Dùng camera chính" },
        };

        static readonly HashSet<string> UnityMessages = new HashSet<string>
        {
            "Awake", "Start", "Update", "LateUpdate", "FixedUpdate", "OnEnable", "OnDisable", "OnDestroy", "OnValidate",
            "Reset", "OnGUI", "OnDrawGizmos", "OnDrawGizmosSelected", "OnApplicationPause", "OnApplicationQuit", "OnApplicationFocus"
        };

        class ScriptInfo
        {
            public string name, path, code;
            public MonoScript script;
            public Type type;
            public LogicCategory cat;
            public readonly List<LogicCategory> tags = new List<LogicCategory>();
            public readonly List<string> facts = new List<string>();
            public readonly List<string> options = new List<string>();
            public readonly List<string> actions = new List<string>();
            public bool vendor, expanded;
            public int inScene;
        }

        List<ScriptInfo> logic;
        string logicFilter = "";
        bool logicVendor;
        readonly Dictionary<string, bool> logicFold = new Dictionary<string, bool>();

        void DrawLogic()
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(logic == null ? "Quét logic game" : "Quét lại", GUILayout.Height(26))) ScanLogic();
            GUI.enabled = logic != null;
            if (GUILayout.Button("Copy giải thích cho AI", GUILayout.Width(150), GUILayout.Height(26))) CopyLogic();
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
            if (logic == null)
            {
                EditorGUILayout.HelpBox("Quét mọi script của game trong Assets, xếp vào nhóm logic (điều khiển nhân vật, camera, kẻ địch, spawner, " +
                                        "luồng thắng/thua, tiền, tutorial, end card, UI, âm thanh, VFX…) và giải thích từng script: làm gì, có tuỳ chọn gì, " +
                                        "đang gắn ở object nào. Bấm “Tuỳ chỉnh” cạnh script để sửa mọi tuỳ chọn của nó.", MessageType.None);
                return;
            }
            logicFilter = EditorGUILayout.TextField("Lọc", logicFilter);
            logicVendor = EditorGUILayout.ToggleLeft("Hiện cả script thư viện bên thứ 3", logicVendor);

            var shown = logic.Where(s => (logicVendor || !s.vendor) &&
                                         (string.IsNullOrEmpty(logicFilter) ||
                                          (s.name + " " + s.cat.name + " " + string.Join(" ", s.facts.ToArray())).IndexOf(logicFilter, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
            foreach (var cat in Categories.Concat(new[] { OtherCategory }))
            {
                var items = shown.Where(s => s.cat == cat).ToList();
                if (items.Count == 0) continue;
                bool open;
                if (!logicFold.TryGetValue(cat.name, out open)) open = true;
                open = EditorGUILayout.Foldout(open, cat.name + "  (" + items.Count + ")", true, EditorStyles.foldoutHeader);
                logicFold[cat.name] = open;
                if (!open) continue;
                GUILayout.Label(cat.desc, EditorStyles.wordWrappedMiniLabel);
                foreach (var s in items) DrawScriptInfo(s);
                GUILayout.Space(4);
            }
        }

        void DrawScriptInfo(ScriptInfo s)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            s.expanded = EditorGUILayout.Foldout(s.expanded, s.name, true);
            GUILayout.FlexibleSpace();
            if (s.inScene > 0 && GUILayout.Button(s.inScene + " trong scene", EditorStyles.miniButton, GUILayout.Width(86))) SelectInScene(s.type);
            GUI.enabled = s.type != null;
            if (GUILayout.Button("Tuỳ chỉnh", EditorStyles.miniButton, GUILayout.Width(64)))
            {
                optScript = s.name;
                if (opts == null) ScanOptions();
                tab = OptionsTab;
            }
            GUI.enabled = true;
            if (GUILayout.Button("Mở", EditorStyles.miniButton, GUILayout.Width(34))) AssetDatabase.OpenAsset(s.script);
            EditorGUILayout.EndHorizontal();
            string sub = s.facts.Count > 0 ? string.Join(" · ", s.facts.Take(4).ToArray()) : "";
            if (s.tags.Count > 0) sub = "cũng là: " + string.Join(", ", s.tags.Select(t => t.name).ToArray()) + (sub.Length > 0 ? "  —  " + sub : "");
            if (sub.Length > 0) GUILayout.Label(sub, EditorStyles.wordWrappedMiniLabel);
            if (s.expanded)
            {
                GUILayout.Label(Explain(s), EditorStyles.wordWrappedLabel);
                if (s.options.Count > 0) GUILayout.Label("Tuỳ chọn trong Inspector: " + string.Join(", ", s.options.ToArray()), EditorStyles.wordWrappedMiniLabel);
                if (s.actions.Count > 0) GUILayout.Label("Hàm gọi được: " + string.Join(", ", s.actions.ToArray()), EditorStyles.wordWrappedMiniLabel);
                GUILayout.Label(s.path, EditorStyles.miniLabel);
            }
            EditorGUILayout.EndVertical();
        }

        static string Explain(ScriptInfo s)
        {
            var sb = new StringBuilder();
            sb.Append(s.name).Append(" thuộc nhóm “").Append(s.cat.name).Append("”: ").Append(s.cat.desc);
            if (s.facts.Count > 0) sb.Append("\nTrong code: ").Append(string.Join("; ", s.facts.ToArray())).Append('.');
            if (s.type != null && typeof(ScriptableObject).IsAssignableFrom(s.type))
                sb.Append("\nĐây là ScriptableObject — dữ liệu nằm trong file .asset, sửa là đổi cho mọi nơi dùng nó.");
            else if (s.inScene == 0 && s.type != null && typeof(MonoBehaviour).IsAssignableFrom(s.type))
                sb.Append("\nKhông gắn trong scene đang mở — có thể nằm trong prefab, được tạo khi chơi, hoặc không dùng.");
            return sb.ToString();
        }

        void ScanLogic()
        {
            logic = new List<ScriptInfo>();
            var counts = new Dictionary<Type, int>();
            foreach (var go in SceneObjects())
                foreach (var c in go.GetComponents<MonoBehaviour>())
                {
                    if (c == null) continue;
                    int n;
                    counts.TryGetValue(c.GetType(), out n);
                    counts[c.GetType()] = n + 1;
                }
            foreach (var guid in AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".cs") || path.Contains("/Editor/") || path.Contains("/CreativeRecorder/")) continue;
                var ms = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (ms == null) continue;
                var s = new ScriptInfo { script = ms, path = path, code = ms.text ?? "", type = ms.GetClass(), vendor = IsVendor(path) };
                s.name = s.type != null ? s.type.Name : Path.GetFileNameWithoutExtension(path);
                Classify(s);
                Describe(s);
                int cnt;
                if (s.type != null && counts.TryGetValue(s.type, out cnt)) s.inScene = cnt;
                logic.Add(s);
            }
            logic = logic.OrderByDescending(s => s.inScene > 0).ThenBy(s => s.name).ToList();
        }

        static void Classify(ScriptInfo s)
        {
            string words = SplitWords(s.name);
            var scores = new List<KeyValuePair<LogicCategory, int>>();
            foreach (var c in Categories)
            {
                int score = c.nameRx.Matches(words).Count * 4 + Math.Min(3, c.codeRx.Matches(s.code).Count);
                if (c.name.StartsWith("Dữ liệu") && ((s.type != null && typeof(ScriptableObject).IsAssignableFrom(s.type)) ||
                                                     Regex.IsMatch(words, @"\b(data|database|config|settings|info|definition)$"))) score += 5;
                if (score > 0) scores.Add(new KeyValuePair<LogicCategory, int>(c, score));
            }
            scores = scores.OrderByDescending(x => x.Value).ToList();
            s.cat = scores.Count > 0 && scores[0].Value >= 2 ? scores[0].Key : OtherCategory;
            foreach (var x in scores.Skip(1).Where(x => x.Value >= 4).Take(2)) s.tags.Add(x.Key);
        }

        static void Describe(ScriptInfo s)
        {
            var keys = new List<string>();
            foreach (Match m in KeyRx[0].Matches(s.code)) keys.Add(PrettyKey(m.Groups[2].Value));
            foreach (Match m in KeyRx[3].Matches(s.code)) keys.Add(PrettyKey(m.Groups[1].Value));
            if (keys.Count > 0) s.facts.Add("Đọc phím " + string.Join(", ", keys.Distinct().ToArray()));
            foreach (var f in FactPatterns)
                if (Regex.IsMatch(s.code, f[0])) s.facts.Add(f[1]);
            if (s.type == null) return;
            const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            for (var t = s.type; t != null && t != typeof(MonoBehaviour) && t != typeof(ScriptableObject) && t != typeof(object); t = t.BaseType)
            {
                foreach (var f in t.GetFields(F))
                {
                    if (f.IsNotSerialized || f.IsDefined(typeof(HideInInspector), true)) continue;
                    if (!f.IsPublic && !f.IsDefined(typeof(SerializeField), true)) continue;
                    s.options.Add(f.Name + " (" + FriendlyType(f.FieldType) + ")");
                }
                foreach (var m in t.GetMethods(F))
                {
                    if (!m.IsPublic || m.IsSpecialName || UnityMessages.Contains(m.Name) || s.actions.Count >= 15) continue;
                    s.actions.Add(m.Name + "()");
                }
            }
        }

        static string FriendlyType(Type t)
        {
            if (t.IsArray) return "danh sách " + FriendlyType(t.GetElementType());
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>)) return "danh sách " + FriendlyType(t.GetGenericArguments()[0]);
            if (t == typeof(float) || t == typeof(int) || t == typeof(double)) return "số";
            if (t == typeof(bool)) return "bật/tắt";
            if (t == typeof(string)) return "chữ";
            if (t == typeof(KeyCode)) return "phím";
            if (t == typeof(Color)) return "màu";
            if (t == typeof(AudioClip)) return "âm thanh";
            if (t == typeof(Material) || t == typeof(Sprite) || typeof(Texture).IsAssignableFrom(t) || t == typeof(Mesh)) return "hình / skin";
            if (t == typeof(GameObject)) return "object / prefab";
            if (t.IsEnum) return "lựa chọn";
            return t.Name;
        }

        static void SelectInScene(Type t)
        {
            if (t == null) return;
            var gos = SceneObjects().Where(g => g.GetComponent(t) != null).ToArray();
            Selection.objects = gos.Cast<Object>().ToArray();
            if (gos.Length > 0) EditorGUIUtility.PingObject(gos[0]);
        }

        void CopyLogic()
        {
            var sb = new StringBuilder("# Logic game — " + Application.productName + "\n");
            foreach (var cat in Categories.Concat(new[] { OtherCategory }))
            {
                var items = logic.Where(s => s.cat == cat && !s.vendor).ToList();
                if (items.Count == 0) continue;
                sb.Append("\n## ").Append(cat.name).Append("\n").Append(cat.desc).Append("\n");
                foreach (var s in items)
                {
                    sb.Append("- **").Append(s.name).Append("** (").Append(s.path).Append(s.inScene > 0 ? ", " + s.inScene + " trong scene" : "").Append(")");
                    if (s.facts.Count > 0) sb.Append(": ").Append(string.Join("; ", s.facts.ToArray()));
                    if (s.options.Count > 0) sb.Append(". Tuỳ chọn: ").Append(string.Join(", ", s.options.ToArray()));
                    if (s.actions.Count > 0) sb.Append(". Hàm: ").Append(string.Join(", ", s.actions.ToArray()));
                    sb.Append("\n");
                }
            }
            EditorGUIUtility.systemCopyBuffer = sb.ToString();
            ShowNotification(new GUIContent("Đã copy"));
        }
    }
}
