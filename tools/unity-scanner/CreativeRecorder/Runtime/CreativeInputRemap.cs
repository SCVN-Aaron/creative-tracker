using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;

namespace CreativeRecorder
{
    /// <summary>
    /// Khi camera bị xoay (đảo 180°, xoay quanh, góc cố định…), đổi hướng giá trị joystick ngay trước khi script
    /// nhân vật đọc, để đẩy lên là nhân vật chạy lên trên màn hình. Chạy trước script game (FixedUpdate và Update),
    /// cuối frame trả lại giá trị gốc (CreativeInputRestore) để lần sau luôn đọc đúng giá trị joystick thật.
    /// Tìm joystick theo tên class (Joystick Pack: FloatingJoystick, DynamicJoystick… và joystick tự viết),
    /// đổi biến Vector2 chứa hướng (input, direction, inputVector…).
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [AddComponentMenu("")]
    public class CreativeInputRemap : MonoBehaviour
    {
        public CreativeDirector director;
        public CreativeCameraLate cam;

        class Slot
        {
            public Component comp;
            public FieldInfo field;
            public Vector2 raw, written;
            public bool pending;
        }

        static readonly Regex JoyType = new Regex(@"joystick|joy[_ ]?stick|onscreenstick|virtualstick|thumbstick|touchstick|analogstick|virtualpad|touchpad", RegexOptions.IgnoreCase);
        static readonly Regex Wanted = new Regex(@"input|direction|dir|axis|value|move|movement|vector|output|delta", RegexOptions.IgnoreCase);
        static readonly Regex Unwanted = new Regex(@"handle|background|bg|range|radius|dead|zone|pos|position|center|centre|start|origin|size|pivot|anchor|offset|default|threshold|scale|limit|last|prev|previous|touch|pointer|screen|rect", RegexOptions.IgnoreCase);
        static readonly Dictionary<Type, FieldInfo[]> fieldCache = new Dictionary<Type, FieldInfo[]>();

        readonly List<Slot> slots = new List<Slot>();
        float nextFind;

        public int Count { get { return slots.Count; } }

        void FixedUpdate() { Apply(); }

        void Update()
        {
            if (Time.unscaledTime >= nextFind)
            {
                nextFind = Time.unscaledTime + 1f;
                Find();
            }
            Apply();
        }

        void Apply()
        {
            var p = director != null ? director.preset : null;
            bool active = p != null && p.enabled && p.cam != null && p.cam.fixJoystick && cam != null;
            foreach (var s in slots)
            {
                if (s.comp == null) continue;
                Vector2 v;
                try { v = (Vector2)s.field.GetValue(s.comp); }
                catch (Exception) { continue; }
                if (!(s.pending && v == s.written)) s.raw = v;   // giá trị thật của joystick (chưa xoay trong frame này)
                var w = active ? cam.MapInput(s.raw) : s.raw;
                if (w != v)
                {
                    try { s.field.SetValue(s.comp, w); }
                    catch (Exception) { continue; }
                }
                s.written = w;
                s.pending = true;
            }
        }

        /// <summary>Cuối frame: trả joystick về giá trị thật (nếu trong frame joystick không tự ghi giá trị mới).</summary>
        public void EndFrame()
        {
            foreach (var s in slots)
            {
                if (!s.pending || s.comp == null) continue;
                s.pending = false;
                try
                {
                    var v = (Vector2)s.field.GetValue(s.comp);
                    if (v == s.written && s.written != s.raw) s.field.SetValue(s.comp, s.raw);
                }
                catch (Exception) { }
            }
        }

        void Find()
        {
            slots.RemoveAll(s => s.comp == null);
            if (director == null) return;
            foreach (var t in director.Objects)
            {
                if (t == null) continue;
                foreach (var c in t.GetComponents<MonoBehaviour>())
                {
                    if (c == null || !IsJoystick(c.GetType())) continue;
                    foreach (var f in Fields(c.GetType()))
                    {
                        bool known = false;
                        foreach (var s in slots) if (s.comp == c && s.field == f) { known = true; break; }
                        if (!known) slots.Add(new Slot { comp = c, field = f });
                    }
                }
            }
        }

        static bool IsJoystick(Type t)
        {
            for (var x = t; x != null && x != typeof(MonoBehaviour); x = x.BaseType)
                if (JoyType.IsMatch(x.Name)) return true;
            return false;
        }

        static FieldInfo[] Fields(Type t)
        {
            FieldInfo[] fs;
            if (fieldCache.TryGetValue(t, out fs)) return fs;
            var list = new List<FieldInfo>();
            for (var x = t; x != null && x != typeof(MonoBehaviour); x = x.BaseType)
                foreach (var f in x.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (f.FieldType != typeof(Vector2) || f.IsInitOnly) continue;
                    string n = f.Name.Replace("k__BackingField", "");
                    if (Wanted.IsMatch(n) && !Unwanted.IsMatch(n)) list.Add(f);
                }
            fs = list.ToArray();
            fieldCache[t] = fs;
            return fs;
        }
    }
}

namespace CreativeRecorder
{
    /// <summary>Chạy sau mọi script game: gọi CreativeInputRemap.EndFrame.</summary>
    [DefaultExecutionOrder(10001)]
    [AddComponentMenu("")]
    public class CreativeInputRestore : MonoBehaviour
    {
        public CreativeInputRemap remap;
        void LateUpdate() { if (remap != null) remap.EndFrame(); }
    }
}
