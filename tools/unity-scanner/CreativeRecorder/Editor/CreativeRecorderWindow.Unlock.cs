using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace CreativeRecorder
{
    /// <summary>
    /// Trang "Unlock Node": tìm các ô mở khoá (trả tiền → xây dựng / nâng cấp / mở rộng), những object mà ô đó bật lên,
    /// và di chuyển cả cụm (node + object đi kèm) bằng một tay cầm trong Scene view.
    /// Đây là chỉnh sửa level nên lưu thẳng vào scene (Ctrl+Z để hoàn tác, Ctrl+S để lưu).
    /// </summary>
    public partial class CreativeRecorderWindow
    {
        class UnlockNode
        {
            public GameObject go;
            public MonoBehaviour comp;
            public FieldInfo cost;
            public readonly List<GameObject> targets = new List<GameObject>();
            public readonly Dictionary<GameObject, string> via = new Dictionary<GameObject, string>();
            public readonly HashSet<GameObject> detached = new HashSet<GameObject>();
            public readonly List<MethodInfo> actions = new List<MethodInfo>();
            public readonly List<string> prefabs = new List<string>();
            public bool open;
        }

        static readonly Regex NodeVerbRx = new Regex(@"\b(unlock|unlocker|unlockable|buy|purchase|purchasable|pay|payment|paid|build|builder|building|construct|construction|upgrade|upgrader|expand|expansion|opener)\b", RegexOptions.IgnoreCase);
        static readonly Regex NodeNounRx = new Regex(@"\b(node|zone|area|spot|point|station|slot|pad|trigger|place|plot|platform|tile|circle|ring)\b", RegexOptions.IgnoreCase);
        static readonly Regex CostRx = new Regex(@"\b(price|cost|costs|required|require|needed|need|amount|money|coin|coins|cash|value|target|total|remaining|left|fill)\b", RegexOptions.IgnoreCase);
        static readonly Regex ActionRx = new Regex(@"\b(unlock|unlocked|buy|purchase|complete|completed|build|activate|open|upgrade|finish|expand|pay)\b", RegexOptions.IgnoreCase);
        static readonly Regex NotTargetRx = new Regex(@"\b(player|camera|cam|canvas|manager|joystick|ui|hud|main|game)\b", RegexOptions.IgnoreCase);

        List<UnlockNode> nodes;
        UnlockNode moving;
        string nodeFilter = "";
        float nudge = 1f;

        void DrawUnlockPage()
        {
            if (nodes == null) ScanNodes();
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Ô mở khoá: người chơi đứng vào trả tiền → bật công trình / nâng cấp / khu mới. Bấm “Di chuyển” rồi kéo tay cầm trong Scene view: node và mọi object đi kèm di chuyển cùng nhau.", stMuted);
            if (GUILayout.Button("Làm mới", EditorStyles.miniButton, GUILayout.Width(64))) ScanNodes();
            EditorGUILayout.EndHorizontal();
            if (Application.isPlaying)
                EditorGUILayout.HelpBox("Đang Play: di chuyển chỉ là thử tạm, dừng Play sẽ mất. Muốn lưu vị trí mới thì dừng Play rồi di chuyển.", MessageType.Warning);
            else
                EditorGUILayout.HelpBox("Di chuyển node là sửa level: lưu thẳng vào scene. Ctrl+Z để hoàn tác, Ctrl+S để lưu scene.", MessageType.Info);

            if (moving != null && moving.go != null) DrawMovePanel();

            nodeFilter = EditorGUILayout.TextField("Tìm", nodeFilter);
            nodes.RemoveAll(n => n.go == null);
            if (nodes.Count == 0)
                EditorGUILayout.HelpBox("Không tìm thấy node mở khoá trong scene đang mở (script có tên kiểu Unlock / Buy / Purchase / Build / Upgrade…).", MessageType.None);
            foreach (var n in nodes.Where(n => string.IsNullOrEmpty(nodeFilter) || (n.go.name + " " + n.comp.GetType().Name).IndexOf(nodeFilter, StringComparison.OrdinalIgnoreCase) >= 0))
                DrawNode(n);
        }

        void DrawNode(UnlockNode n)
        {
            if (n.comp == null) return;
            bool isMoving = moving == n;
            EditorGUILayout.BeginVertical(stCard);
            EditorGUILayout.BeginHorizontal();
            n.open = EditorGUILayout.Foldout(n.open, n.go.name, true);
            GUILayout.FlexibleSpace();
            if (n.cost != null) CostField(n);
            if (GUILayout.Button("Chọn cả cụm", EditorStyles.miniButton, GUILayout.Width(82))) SelectGroup(n);
            if (Tinted(isMoving ? "Xong" : "Di chuyển", isMoving ? Green : Blue, EditorStyles.miniButton, GUILayout.Width(70)))
            {
                moving = isMoving ? null : n;
                if (moving != null) { SelectGroup(n); if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected(); }
                SceneView.RepaintAll();
            }
            EditorGUILayout.EndHorizontal();
            var moved = n.targets.Where(t => t != null && !n.detached.Contains(t)).ToList();
            GUILayout.Label(n.comp.GetType().Name + "  ·  " + (moved.Count > 0 ? "đi kèm: " + string.Join(", ", moved.Select(t => t.name).Take(6).ToArray()) + (moved.Count > 6 ? "…" : "")
                                                                               : "không thấy object đi kèm") +
                            (n.prefabs.Count > 0 ? "  ·  tạo từ prefab: " + string.Join(", ", n.prefabs.ToArray()) : ""), stMuted);
            if (n.open)
            {
                foreach (var t in n.targets)
                {
                    if (t == null) continue;
                    EditorGUILayout.BeginHorizontal();
                    bool on = !n.detached.Contains(t);
                    bool non = EditorGUILayout.ToggleLeft(t.name + "   (" + n.via[t] + ")", on);
                    if (non != on) { if (non) n.detached.Remove(t); else n.detached.Add(t); }
                    if (GUILayout.Button("Xem", EditorStyles.miniButton, GUILayout.Width(40))) { Selection.activeGameObject = t; EditorGUIUtility.PingObject(t); }
                    EditorGUILayout.EndHorizontal();
                }
                if (n.targets.Count > 0) GUILayout.Label("Bỏ tick object nào không muốn di chuyển theo node.", stMuted);
                if (n.actions.Count > 0)
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label(Application.isPlaying ? "Mở khoá ngay:" : "Mở khoá ngay (bấm Play để dùng):", GUILayout.Width(170));
                    GUI.enabled = Application.isPlaying;
                    foreach (var m in n.actions.Take(3))
                        if (GUILayout.Button(ObjectNames.NicifyVariableName(m.Name), EditorStyles.miniButton))
                        {
                            try { m.Invoke(n.comp, null); }
                            catch (Exception e) { Debug.LogException(e.InnerException ?? e); }
                        }
                    GUI.enabled = true;
                    EditorGUILayout.EndHorizontal();
                }
            }
            EditorGUILayout.EndVertical();
        }

        void CostField(UnlockNode n)
        {
            GUILayout.Label("Giá", GUILayout.Width(26));
            if (Application.isPlaying)
            {
                string cur = CreativeReflect.Format(CreativeReflect.Get(n.comp, n.cost));
                double d;
                double.TryParse(cur, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d);
                double nd = EditorGUILayout.DelayedDoubleField(d, GUILayout.Width(70));
                if (nd != d) CreativeReflect.Write(n.comp, n.cost.Name, CreativeReflect.Format(nd));
                return;
            }
            var so = new SerializedObject(n.comp);
            var p = so.FindProperty(n.cost.Name);
            if (p == null) { GUILayout.Label(CreativeReflect.Format(CreativeReflect.Get(n.comp, n.cost)), GUILayout.Width(70)); return; }
            EditorGUILayout.PropertyField(p, GUIContent.none, GUILayout.Width(70));
            if (so.ApplyModifiedProperties()) MarkDirty(n.go);
        }

        void DrawMovePanel()
        {
            var n = moving;
            EditorGUILayout.BeginVertical(stCard);
            ColorLabel("Đang di chuyển: " + n.go.name + "  —  kéo tay cầm trong Scene view", Green, EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            nudge = EditorGUILayout.FloatField("Bước", nudge, GUILayout.Width(160));
            if (GUILayout.Button("← X")) MoveGroup(n, new Vector3(-nudge, 0, 0));
            if (GUILayout.Button("X →")) MoveGroup(n, new Vector3(nudge, 0, 0));
            if (GUILayout.Button("↓ Z")) MoveGroup(n, new Vector3(0, 0, -nudge));
            if (GUILayout.Button("Z ↑")) MoveGroup(n, new Vector3(0, 0, nudge));
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Xoay 90°")) RotateGroup(n, 90f);
            if (GUILayout.Button("Đưa tới giữa Scene view") && SceneView.lastActiveSceneView != null)
            {
                var d = SceneView.lastActiveSceneView.pivot - n.go.transform.position;
                d.y = 0f;
                MoveGroup(n, d);
            }
            if (Tinted("Xong", Green, GUI.skin.button, GUILayout.Width(60))) { moving = null; SceneView.RepaintAll(); }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        void ScanNodes()
        {
            nodes = new List<UnlockNode>();
            moving = null;
            foreach (var kv in GameComponents())
            {
                var c = kv.Value;
                var type = c.GetType();
                string words = SplitWords(type.Name), goWords = SplitWords(CreativeReflect.BaseName(kv.Key.name));
                if (!NodeVerbRx.IsMatch(words) && !NodeVerbRx.IsMatch(goWords)) continue;
                if (nodes.Any(x => x.go == kv.Key)) continue;
                FieldInfo cost = null;
                foreach (var f in GameFields(type))
                {
                    string nm = CleanName(f.Name);
                    if (f.IsStatic || nm == null || !CreativeReflect.IsNumber(f.FieldType) || !CostRx.IsMatch(SplitWords(nm))) continue;
                    cost = f;
                    break;
                }
                bool noun = NodeNounRx.IsMatch(words) || NodeNounRx.IsMatch(goWords);
                if (cost == null && !noun && !Regex.IsMatch(words, @"\b(unlock\w*|purchase|buy)\b", RegexOptions.IgnoreCase)) continue;
                var n = new UnlockNode { go = kv.Key, comp = c, cost = cost };
                CollectTargets(n);
                foreach (var m in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (!m.IsSpecialName && m.GetParameters().Length == 0 && m.ReturnType == typeof(void) && ActionRx.IsMatch(SplitWords(m.Name))) n.actions.Add(m);
                nodes.Add(n);
            }
            // Object được nhiều node cùng trỏ tới là đồ dùng chung (manager, player…) — không kéo theo
            var counts = new Dictionary<GameObject, int>();
            foreach (var n in nodes)
                foreach (var t in n.targets) { int k; counts.TryGetValue(t, out k); counts[t] = k + 1; }
            int limit = Math.Max(3, nodes.Count / 2 + 1);
            var nodeGos = new HashSet<GameObject>(nodes.Select(n => n.go));
            foreach (var n in nodes) n.targets.RemoveAll(t => nodeGos.Contains(t) || (nodes.Count > 2 && counts[t] >= limit));
            nodes = nodes.OrderBy(n => PathOf(n.go.transform)).ToList();
        }

        void CollectTargets(UnlockNode n)
        {
            foreach (var f in GameFields(n.comp.GetType()))
            {
                if (f.IsStatic) continue;
                var v = CreativeReflect.Get(n.comp, f);
                if (v == null) continue;
                string label = ObjectNames.NicifyVariableName(CleanName(f.Name) ?? f.Name);
                var ev = v as UnityEventBase;
                if (ev != null)
                {
                    for (int i = 0; i < ev.GetPersistentEventCount(); i++) AddTarget(n, ev.GetPersistentTarget(i), label);
                    continue;
                }
                var o = v as Object;
                if (o != null) { AddTarget(n, o, label); continue; }
                var list = v as IList;
                if (list != null)
                    foreach (var el in list)
                    {
                        var eo = el as Object;
                        if (eo != null) AddTarget(n, eo, label);
                    }
            }
        }

        void AddTarget(UnlockNode n, Object o, string label)
        {
            var go = o as GameObject ?? (o is Component ? ((Component)o).gameObject : null);
            if (go == null || go == n.go) return;
            if (EditorUtility.IsPersistent(go))
            {
                if (!n.prefabs.Contains(go.name)) n.prefabs.Add(go.name);
                return;
            }
            bool child = go.transform.IsChildOf(n.go.transform);
            if (!child && (NotTargetRx.IsMatch(SplitWords(go.name)) || go.GetComponent<Camera>() != null || go.GetComponent<Canvas>() != null ||
                           go.GetComponent<RectTransform>() != null)) return;
            if (child) return; // con của node: đã đi theo node sẵn
            if (n.targets.Contains(go)) return;
            n.targets.Add(go);
            n.via[go] = label;
        }

        List<Transform> MoveSet(UnlockNode n)
        {
            var list = new List<Transform> { n.go.transform };
            foreach (var t in n.targets) if (t != null && !n.detached.Contains(t)) list.Add(t.transform);
            return list.Where(t => !list.Any(o => o != t && t.IsChildOf(o))).ToList();
        }

        void SelectGroup(UnlockNode n)
        {
            Selection.objects = MoveSet(n).Select(t => (Object)t.gameObject).ToArray();
        }

        void MoveGroup(UnlockNode n, Vector3 delta)
        {
            var set = MoveSet(n);
            if (!Application.isPlaying) Undo.RecordObjects(set.Cast<Object>().ToArray(), "Move unlock node");
            foreach (var t in set) { t.position += delta; MarkDirty(t.gameObject); }
            SceneView.RepaintAll();
        }

        void RotateGroup(UnlockNode n, float angle)
        {
            var set = MoveSet(n);
            var pivot = n.go.transform.position;
            if (!Application.isPlaying) Undo.RecordObjects(set.Cast<Object>().ToArray(), "Rotate unlock node");
            foreach (var t in set) { t.RotateAround(pivot, Vector3.up, angle); MarkDirty(t.gameObject); }
            SceneView.RepaintAll();
        }

        /// <summary>Vẽ tên + giá của mọi node, đường nối tới object đi kèm, và tay cầm di chuyển cả cụm.</summary>
        void OnSceneGUI(SceneView sv)
        {
            bool active = !advanced && page == Page.Unlock && nodes != null;
            Tools.hidden = active && moving != null && moving.go != null;
            if (!active) return;
            foreach (var n in nodes)
            {
                if (n.go == null || n.comp == null) continue;
                var p = n.go.transform.position;
                Handles.color = moving == n ? Green : new Color(1f, 0.85f, 0.3f);
                foreach (var t in n.targets)
                    if (t != null && !n.detached.Contains(t)) Handles.DrawDottedLine(p, t.transform.position, 4f);
                string cost = n.cost != null ? "  ·  " + CreativeReflect.Format(CreativeReflect.Get(n.comp, n.cost)) : "";
                Handles.Label(p + Vector3.up * 1.5f, n.go.name + cost, EditorStyles.whiteBoldLabel);
            }
            if (moving == null || moving.go == null) return;
            var pos = moving.go.transform.position;
            var np = Handles.PositionHandle(pos, Quaternion.identity);
            if (np != pos) MoveGroup(moving, np - pos);
        }
    }
}
