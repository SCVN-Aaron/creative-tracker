using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CreativeRecorder
{
    /// <summary>
    /// Object ẩn được tool tạo khi bấm Play: áp dụng Bộ chỉnh (skin, cheat, thay âm thanh, SFX theo sự kiện)
    /// lên game đang chạy. Game gốc không bị sửa — dừng Play là mọi thứ trở lại như cũ.
    /// </summary>
    [AddComponentMenu("")]
    public class CreativeDirector : MonoBehaviour
    {
        public static CreativeDirector Instance { get; private set; }
        public CreativePreset preset;

        class RuleState
        {
            public readonly List<Component> comps = new List<Component>();
            public readonly List<Transform> trs = new List<Transform>();
            public readonly Dictionary<int, double> last = new Dictionary<int, double>();
            public readonly Dictionary<int, float> touched = new Dictionary<int, float>();
            public int count = -1, combo;
            public float lastPlay = -99f;
        }

        class Backup
        {
            public object target;
            public FieldInfo field;
            public object value;
        }

        List<Transform> all = new List<Transform>();
        readonly Dictionary<Transform, string> paths = new Dictionary<Transform, string>();
        readonly Dictionary<SfxRule, RuleState> states = new Dictionary<SfxRule, RuleState>();
        readonly List<KeyValuePair<CheatValue, Component>> cheatTargets = new List<KeyValuePair<CheatValue, Component>>();
        readonly Dictionary<string, float> firstSeen = new Dictionary<string, float>();
        readonly HashSet<string> cheatDone = new HashSet<string>();
        readonly Dictionary<Material, Material> copies = new Dictionary<Material, Material>();
        readonly List<Backup> backups = new List<Backup>();
        readonly HashSet<string> backedUp = new HashSet<string>();
        readonly Dictionary<Type, FieldInfo[]> audioFields = new Dictionary<Type, FieldInfo[]>();
        AudioSource[] voices;
        int voice;
        float nextScan;
        AudioClip silence;

        public static CreativeDirector Create(CreativePreset p)
        {
            var go = new GameObject("[Creative Recorder]");
            go.hideFlags = HideFlags.HideInHierarchy;
            DontDestroyOnLoad(go);
            var d = go.AddComponent<CreativeDirector>();
            d.SetPreset(p);
            return d;
        }

        public void SetPreset(CreativePreset p)
        {
            RestoreAssets();
            preset = p;
            states.Clear();
            cheatDone.Clear();
            nextScan = 0f;
        }

        void Awake()
        {
            Instance = this;
            voices = new AudioSource[8];
            for (int i = 0; i < voices.Length; i++)
            {
                voices[i] = gameObject.AddComponent<AudioSource>();
                voices[i].playOnAwake = false;
                voices[i].spatialBlend = 0f;
            }
        }

        void OnDestroy()
        {
            RestoreAssets();
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            if (preset == null || !preset.enabled) return;
            if (Time.unscaledTime >= nextScan)
            {
                nextScan = Time.unscaledTime + 0.25f;
                Scan();
            }
            RunSfx();
        }

        void LateUpdate()
        {
            if (preset == null || !preset.enabled) return;
            foreach (var kv in cheatTargets)
                if (kv.Key.locked && kv.Value != null) CreativeReflect.Write(kv.Value, kv.Key.member, kv.Key.value);
        }

        // ------------------------------------------------------------ quét object

        void Scan()
        {
            all = CreativeReflect.SceneTransforms();
            paths.Clear();
            foreach (var t in all) paths[t] = CreativeReflect.PathOf(t);
            ApplySkins();
            ApplyCheats();
            ApplyClipSwaps();
            BindSfx();
        }

        IEnumerable<Transform> Matching(string path)
        {
            foreach (var t in all)
            {
                string p;
                if (t != null && paths.TryGetValue(t, out p) && p == path) yield return t;
            }
        }

        // ------------------------------------------------------------ skin

        void ApplySkins()
        {
            for (int pass = 0; pass < 2; pass++)
                foreach (var s in preset.skins)
                {
                    if (s.assetObj == null || (s.kind == "tex") != (pass == 1)) continue;
                    foreach (var t in Matching(s.path))
                    {
                        var c = CreativeReflect.FindComponent(t, s.component);
                        if (c != null) ApplySkin(c, s);
                    }
                }
        }

        void ApplySkin(Component c, SkinOverride s)
        {
            var r = c as Renderer;
            switch (s.kind)
            {
                case "mat":
                {
                    var m = s.assetObj as Material;
                    if (r == null || m == null) return;
                    var arr = r.sharedMaterials;
                    if (s.slot >= arr.Length) return;
                    Material copyOfTarget;
                    if (arr[s.slot] == m || (copies.TryGetValue(m, out copyOfTarget) && arr[s.slot] == copyOfTarget)) return;
                    arr[s.slot] = m;
                    r.sharedMaterials = arr;
                    return;
                }
                case "tex":
                {
                    var tex = s.assetObj as Texture;
                    if (r == null || tex == null) return;
                    var arr = r.sharedMaterials;
                    if (s.slot >= arr.Length || arr[s.slot] == null) return;
                    var m = arr[s.slot];
                    if (!copies.ContainsValue(m))
                    {
                        Material cp;
                        if (!copies.TryGetValue(m, out cp)) { cp = new Material(m) { name = m.name + " (creative)" }; copies[m] = cp; }
                        arr[s.slot] = cp;
                        r.sharedMaterials = arr;
                        m = cp;
                    }
                    if (m.HasProperty(s.prop) && m.GetTexture(s.prop) != tex) m.SetTexture(s.prop, tex);
                    return;
                }
                case "mesh":
                {
                    var mesh = s.assetObj as Mesh;
                    var smr = c as SkinnedMeshRenderer;
                    var mf = c as MeshFilter;
                    if (smr != null && smr.sharedMesh != mesh) smr.sharedMesh = mesh;
                    if (mf != null && mf.sharedMesh != mesh) mf.sharedMesh = mesh;
                    return;
                }
                case "sprite":
                {
                    var p = c.GetType().GetProperty("sprite", typeof(Sprite));
                    if (p != null && p.CanWrite && (Object)p.GetValue(c, null) != s.assetObj) p.SetValue(c, s.assetObj, null);
                    return;
                }
            }
        }

        // ------------------------------------------------------------ cheat

        void ApplyCheats()
        {
            cheatTargets.Clear();
            foreach (var cv in preset.cheats)
                foreach (var t in Matching(cv.path))
                {
                    var c = CreativeReflect.FindComponent(t, cv.component);
                    if (c == null) continue;
                    cheatTargets.Add(new KeyValuePair<CheatValue, Component>(cv, c));
                    string key = c.GetInstanceID() + "|" + cv.member + "|" + cv.value;
                    if (cheatDone.Contains(key)) continue;
                    float seen;
                    if (!firstSeen.TryGetValue(key, out seen)) { firstSeen[key] = Time.unscaledTime; continue; }
                    if (Time.unscaledTime - seen < 0.3f) continue; // chờ Start() của game chạy xong rồi mới ghi đè
                    cheatDone.Add(key);
                    CreativeReflect.Write(c, cv.member, cv.value);
                }
        }

        /// <summary>Ghi ngay giá trị cheat vừa sửa trong cửa sổ tool.</summary>
        public void ApplyCheatNow(CheatValue cv)
        {
            foreach (var t in Matching(cv.path))
            {
                var c = CreativeReflect.FindComponent(t, cv.component);
                if (c == null) continue;
                CreativeReflect.Write(c, cv.member, cv.value);
                cheatDone.Add(c.GetInstanceID() + "|" + cv.member + "|" + cv.value);
            }
        }

        // ------------------------------------------------------------ thay âm thanh

        void ApplyClipSwaps()
        {
            if (preset.clipSwaps.Count == 0) return;
            var map = new Dictionary<AudioClip, AudioClip>();
            foreach (var cs in preset.clipSwaps)
            {
                if (cs.fromClip == null) continue;
                var to = cs.mute ? Silence() : cs.toClip;
                if (to != null) map[cs.fromClip] = to;
            }
            if (map.Count == 0) return;
            var visited = new HashSet<object>();
            foreach (var t in all)
            {
                if (t == null) continue;
                foreach (var c in t.GetComponents<Component>())
                {
                    var src = c as AudioSource;
                    if (src != null)
                    {
                        AudioClip to;
                        if (src.clip != null && map.TryGetValue(src.clip, out to))
                        {
                            bool playing = src.isPlaying;
                            src.clip = to;
                            if (playing) src.Play();
                        }
                    }
                    else if (c is MonoBehaviour && !(c is CreativeDirector))
                        SwapFields(c, map, false, 0, visited);
                }
            }
        }

        AudioClip Silence()
        {
            if (silence == null) silence = AudioClip.Create("silence", 256, 1, 44100, false);
            return silence;
        }

        FieldInfo[] AudioFieldsOf(Type t)
        {
            FieldInfo[] fs;
            if (audioFields.TryGetValue(t, out fs)) return fs;
            var list = new List<FieldInfo>();
            for (var x = t; x != null && x != typeof(MonoBehaviour) && x != typeof(ScriptableObject) && x != typeof(object); x = x.BaseType)
                foreach (var f in x.GetFields(CreativeReflect.AllFields & ~BindingFlags.Static))
                {
                    var ft = f.FieldType;
                    if (f.IsLiteral || f.IsInitOnly || ft.IsPrimitive || ft == typeof(string) || ft.IsEnum || ft.IsValueType) continue;
                    if (typeof(Component).IsAssignableFrom(ft) || ft == typeof(GameObject) || typeof(Delegate).IsAssignableFrom(ft)) continue;
                    if (ft == typeof(AudioClip) || typeof(IList).IsAssignableFrom(ft) || typeof(ScriptableObject).IsAssignableFrom(ft) ||
                        (!typeof(Object).IsAssignableFrom(ft) && ft.IsClass && IsGameType(ft)))
                        list.Add(f);
                }
            fs = list.ToArray();
            audioFields[t] = fs;
            return fs;
        }

        static bool IsGameType(Type t)
        {
            string ns = t.Namespace ?? "";
            return !ns.StartsWith("System") && !ns.StartsWith("Unity") && !ns.StartsWith("TMPro") && !ns.StartsWith("CreativeRecorder");
        }

        /// <summary>Thay AudioClip trong biến của script (cả mảng, danh sách, class lồng nhau, ScriptableObject âm thanh).</summary>
        void SwapFields(object o, Dictionary<AudioClip, AudioClip> map, bool isAsset, int depth, HashSet<object> visited)
        {
            if (o == null || depth > 3 || !visited.Add(o)) return;
            foreach (var f in AudioFieldsOf(o.GetType()))
            {
                object v;
                try { v = f.GetValue(o); } catch (Exception) { continue; }
                if (v == null) continue;
                var clip = v as AudioClip;
                if (clip != null)
                {
                    AudioClip to;
                    if (map.TryGetValue(clip, out to)) { if (isAsset) Remember(o, f, clip); f.SetValue(o, to); }
                    continue;
                }
                var so = v as ScriptableObject;
                if (so != null)
                {
                    if (IsGameType(so.GetType())) SwapFields(so, map, true, depth + 1, visited);
                    continue;
                }
                var list = v as IList;
                if (list != null)
                {
                    bool changed = false;
                    for (int i = 0; i < list.Count; i++)
                    {
                        var el = list[i];
                        var ec = el as AudioClip;
                        AudioClip to;
                        if (ec != null && map.TryGetValue(ec, out to))
                        {
                            if (isAsset && !changed) Remember(o, f, CopyList(list));
                            list[i] = to;
                            changed = true;
                        }
                        else if (el != null && !(el is Object) && el.GetType().IsClass && IsGameType(el.GetType()))
                            SwapFields(el, map, isAsset, depth + 1, visited);
                        else if (el is ScriptableObject && IsGameType(el.GetType()))
                            SwapFields(el, map, true, depth + 1, visited);
                    }
                    continue;
                }
                if (!(v is Object)) SwapFields(v, map, isAsset, depth + 1, visited);
            }
        }

        static object CopyList(IList list)
        {
            var arr = list as Array;
            if (arr != null) return arr.Clone();
            var copy = (IList)Activator.CreateInstance(list.GetType());
            foreach (var x in list) copy.Add(x);
            return copy;
        }

        void Remember(object target, FieldInfo f, object old)
        {
            string key = target.GetHashCode() + "|" + f.Name;
            if (!backedUp.Add(key)) return;
            backups.Add(new Backup { target = target, field = f, value = old });
        }

        /// <summary>Trả lại âm thanh gốc cho ScriptableObject (file asset) — tránh lưu nhầm vào project.</summary>
        void RestoreAssets()
        {
            for (int i = backups.Count - 1; i >= 0; i--)
            {
                try { backups[i].field.SetValue(backups[i].target, backups[i].value); } catch (Exception) { }
            }
            backups.Clear();
            backedUp.Clear();
        }

        // ------------------------------------------------------------ SFX theo sự kiện

        RuleState State(SfxRule r)
        {
            RuleState st;
            if (!states.TryGetValue(r, out st)) { st = new RuleState(); states[r] = st; }
            return st;
        }

        void BindSfx()
        {
            foreach (var r in preset.sfx)
            {
                if (!r.enabled || r.clipObj == null) continue;
                var st = State(r);
                st.comps.Clear();
                st.trs.Clear();
                switch (r.trigger)
                {
                    case SfxTrigger.ValueUp:
                    case SfxTrigger.ValueDown:
                        foreach (var t in Matching(r.path))
                        {
                            var c = CreativeReflect.FindComponent(t, r.component);
                            if (c != null) st.comps.Add(c);
                        }
                        break;
                    case SfxTrigger.ObjectsGone:
                    case SfxTrigger.ObjectsAppear:
                        foreach (var t in all)
                            if (t != null && !string.IsNullOrEmpty(r.filter) &&
                                CreativeReflect.BaseName(t.name).IndexOf(r.filter, StringComparison.OrdinalIgnoreCase) >= 0) st.trs.Add(t);
                        break;
                    case SfxTrigger.KeyDown:
                    case SfxTrigger.Tap:
                        break;
                    default:
                        foreach (var t in Matching(r.path))
                        {
                            st.trs.Add(t);
                            if (r.trigger == SfxTrigger.Touch && t.GetComponent<CreativeTouchRelay>() == null)
                                t.gameObject.AddComponent<CreativeTouchRelay>().hideFlags = HideFlags.HideInInspector;
                        }
                        break;
                }
            }
        }

        void RunSfx()
        {
            foreach (var r in preset.sfx)
            {
                if (!r.enabled || r.clipObj == null) continue;
                var st = State(r);
                bool fire = false;
                switch (r.trigger)
                {
                    case SfxTrigger.ValueUp:
                    case SfxTrigger.ValueDown:
                        foreach (var c in st.comps)
                        {
                            if (c == null) continue;
                            double v = CreativeReflect.ReadNumber(c, r.member);
                            if (double.IsNaN(v)) continue;
                            fire |= Changed(st, c.GetInstanceID(), v, r.trigger == SfxTrigger.ValueUp);
                        }
                        break;
                    case SfxTrigger.ChildAdded:
                    case SfxTrigger.ChildRemoved:
                        foreach (var t in st.trs)
                            if (t != null) fire |= Changed(st, t.GetInstanceID(), ActiveChildren(t), r.trigger == SfxTrigger.ChildAdded);
                        break;
                    case SfxTrigger.Activated:
                        foreach (var t in st.trs)
                            if (t != null) fire |= Changed(st, t.GetInstanceID(), t.gameObject.activeInHierarchy ? 1 : 0, true);
                        break;
                    case SfxTrigger.ObjectsGone:
                    case SfxTrigger.ObjectsAppear:
                    {
                        int n = 0;
                        foreach (var t in st.trs) if (t != null && t.gameObject.activeInHierarchy) n++;
                        if (st.count >= 0) fire = r.trigger == SfxTrigger.ObjectsGone ? n < st.count : n > st.count;
                        st.count = n;
                        break;
                    }
                    case SfxTrigger.KeyDown: fire = CreativeSfxTrigger.KeyDown(r.key); break;
                    case SfxTrigger.Tap: fire = CreativeSfxTrigger.PointerDown(); break;
                }
                if (fire) Play(r, st);
            }
        }

        static bool Changed(RuleState st, int id, double v, bool up)
        {
            double old;
            bool had = st.last.TryGetValue(id, out old);
            st.last[id] = v;
            return had && (up ? v > old + 1e-6 : v < old - 1e-6);
        }

        static int ActiveChildren(Transform t)
        {
            int n = 0;
            for (int i = 0; i < t.childCount; i++) if (t.GetChild(i).gameObject.activeSelf) n++;
            return n;
        }

        public void OnTouch(Transform self, GameObject other)
        {
            if (preset == null || !preset.enabled) return;
            foreach (var r in preset.sfx)
            {
                if (r.trigger != SfxTrigger.Touch || !r.enabled || r.clipObj == null) continue;
                var st = State(r);
                if (!st.trs.Contains(self) || !Partner(other, r.filter)) continue;
                float t;
                int id = other.GetInstanceID();
                if (st.touched.TryGetValue(id, out t) && Time.unscaledTime - t < 0.4f) continue; // cùng một vật chạm liên tục
                st.touched[id] = Time.unscaledTime;
                Play(r, st);
            }
        }

        static bool Partner(GameObject o, string filter)
        {
            if (string.IsNullOrEmpty(filter)) return true;
            var t = o.transform;
            for (int depth = 0; t != null && depth < 4; depth++, t = t.parent)
            {
                if (t.gameObject.tag == filter || t.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                foreach (var c in t.GetComponents<Component>())
                    if (c != null && c.GetType().Name == filter) return true;
            }
            return false;
        }

        void Play(SfxRule r, RuleState st)
        {
            float now = Time.unscaledTime;
            if (now - st.lastPlay < 0.04f) return;
            if (r.pitchStep > 0f) st.combo = now - st.lastPlay <= r.comboWindow ? st.combo + 1 : 0;
            st.lastPlay = now;
            voice = (voice + 1) % voices.Length;
            var v = voices[voice];
            v.pitch = 1f + Mathf.Min(st.combo * r.pitchStep, 1.5f);
            v.PlayOneShot(r.clipObj, r.volume);
        }

        /// <summary>Nút "Thử" trong cửa sổ tool.</summary>
        public void Fire(SfxRule r)
        {
            if (r.clipObj != null) Play(r, State(r));
        }

        /// <summary>Giá trị hiện tại của sự kiện (để người dùng thấy đúng biến): số, số con, số object.</summary>
        public string Live(SfxRule r)
        {
            RuleState st;
            if (!states.TryGetValue(r, out st)) return null;
            switch (r.trigger)
            {
                case SfxTrigger.ValueUp:
                case SfxTrigger.ValueDown:
                    foreach (var c in st.comps) if (c != null) return CreativeReflect.Format(CreativeReflect.ReadNumber(c, r.member));
                    return null;
                case SfxTrigger.ChildAdded:
                case SfxTrigger.ChildRemoved:
                    foreach (var t in st.trs) if (t != null) return ActiveChildren(t).ToString();
                    return null;
                case SfxTrigger.ObjectsGone:
                case SfxTrigger.ObjectsAppear:
                    return st.count >= 0 ? st.count.ToString() : null;
            }
            return null;
        }
    }
}
