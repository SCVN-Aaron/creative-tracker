using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CreativeRecorder
{
    /// <summary>Khi nào phát SFX.</summary>
    public enum SfxTrigger { ValueUp, ValueDown, ChildAdded, ChildRemoved, ObjectsGone, ObjectsAppear, Touch, Activated, KeyDown, Tap }

    /// <summary>Đổi material / sprite / mesh / texture của các object cùng đường dẫn trong Hierarchy.</summary>
    [Serializable]
    public class SkinOverride
    {
        public string path, component, kind, prop, asset, label;
        public int slot;
        [NonSerialized] public Object assetObj;
    }

    /// <summary>Đặt một biến của script game thành giá trị mới (khoá = giữ nguyên giá trị mọi frame).</summary>
    [Serializable]
    public class CheatValue
    {
        public string path, component, member, value, label;
        public bool locked;
    }

    /// <summary>Thay một âm thanh có sẵn bằng âm thanh khác (hoặc tắt tiếng) ở mọi nơi dùng nó.</summary>
    [Serializable]
    public class ClipSwap
    {
        public string from, to;
        public bool mute;
        [NonSerialized] public AudioClip fromClip, toClip;
    }

    /// <summary>Một SFX gắn vào sự kiện trong game.</summary>
    [Serializable]
    public class SfxRule
    {
        public string id, label, note, group, path, component, member, filter, clip;
        public SfxTrigger trigger;
        public KeyCode key = KeyCode.Space;
        public bool enabled = true;
        public float volume = 1f, pitchStep, comboWindow = 1.5f;
        [NonSerialized] public AudioClip clipObj;
    }

    /// <summary>Bộ chỉnh: mọi thay đổi người quay đã chọn. Lưu ở ProjectSettings/CreativeRecorderPreset.json, áp dụng khi Play.</summary>
    [Serializable]
    public class CreativePreset
    {
        public bool enabled = true;
        public List<SkinOverride> skins = new List<SkinOverride>();
        public List<CheatValue> cheats = new List<CheatValue>();
        public List<ClipSwap> clipSwaps = new List<ClipSwap>();
        public List<SfxRule> sfx = new List<SfxRule>();

        public int ActiveSfx
        {
            get
            {
                int n = 0;
                foreach (var r in sfx) if (r.enabled && !string.IsNullOrEmpty(r.clip)) n++;
                return n;
            }
        }

        public bool HasAny { get { return skins.Count > 0 || cheats.Count > 0 || clipSwaps.Count > 0 || ActiveSfx > 0; } }
    }

    /// <summary>Đọc / ghi biến của script game bằng reflection, tìm object theo đường dẫn.</summary>
    public static class CreativeReflect
    {
        public const BindingFlags AllFields = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        static readonly Dictionary<string, FieldInfo> fieldCache = new Dictionary<string, FieldInfo>();
        static readonly Regex Numbered = new Regex(@"\s*\(\d+\)$");

        public static FieldInfo Field(Type t, string name)
        {
            string key = t.FullName + "." + name;
            FieldInfo f;
            if (fieldCache.TryGetValue(key, out f)) return f;
            for (var x = t; x != null && x != typeof(MonoBehaviour) && x != typeof(object); x = x.BaseType)
            {
                f = x.GetField(name, AllFields);
                if (f != null) break;
            }
            fieldCache[key] = f;
            return f;
        }

        public static bool IsNumber(Type t)
        {
            return t == typeof(int) || t == typeof(float) || t == typeof(double) || t == typeof(long) ||
                   t == typeof(short) || t == typeof(byte) || t == typeof(uint) || t == typeof(ulong) || t == typeof(decimal);
        }

        public static object Get(Component c, FieldInfo f)
        {
            try { return f.GetValue(f.IsStatic ? null : c); }
            catch (Exception) { return null; }
        }

        /// <summary>Giá trị số của biến (danh sách → số phần tử). NaN nếu không đọc được.</summary>
        public static double ReadNumber(Component c, string member)
        {
            if (c == null) return double.NaN;
            var f = Field(c.GetType(), member);
            return f == null ? double.NaN : ToNumber(Get(c, f));
        }

        public static double ToNumber(object v)
        {
            if (v == null) return double.NaN;
            if (v is bool) return (bool)v ? 1 : 0;
            if (IsNumber(v.GetType())) return Convert.ToDouble(v, CultureInfo.InvariantCulture);
            var col = v as ICollection;
            return col != null ? col.Count : double.NaN;
        }

        public static string Format(object v)
        {
            if (v == null) return "";
            if (v is float) return ((float)v).ToString("0.###", CultureInfo.InvariantCulture);
            if (v is double) return ((double)v).ToString("0.###", CultureInfo.InvariantCulture);
            if (v is bool) return (bool)v ? "true" : "false";
            return Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        public static object Parse(string s, Type t)
        {
            try
            {
                if (t == typeof(bool)) return s == "1" || string.Equals(s, "true", StringComparison.OrdinalIgnoreCase);
                if (t.IsEnum) return Enum.Parse(t, s);
                if (t == typeof(string)) return s;
                double d = double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
                if (t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte) || t == typeof(uint) || t == typeof(ulong))
                    d = Math.Round(d);
                return Convert.ChangeType(d, t, CultureInfo.InvariantCulture);
            }
            catch (Exception) { return null; }
        }

        public static bool Write(Component c, string member, string value)
        {
            if (c == null) return false;
            var f = Field(c.GetType(), member);
            if (f == null || f.IsLiteral || f.IsInitOnly) return false;
            var v = Parse(value, f.FieldType);
            if (v == null) return false;
            try { f.SetValue(f.IsStatic ? null : c, v); return true; }
            catch (Exception) { return false; }
        }

        /// <summary>Tên gọn: bỏ "(Clone)" và số thứ tự " (3)" — Coin (3)(Clone) → Coin.</summary>
        public static string BaseName(string n)
        {
            n = n.Replace("(Clone)", "").Trim();
            return Numbered.Replace(n, "").Trim();
        }

        /// <summary>Đường dẫn Hierarchy theo tên gọn: mọi object cùng loại (Coin, Coin (1), Coin(Clone)…) có chung đường dẫn.</summary>
        public static string PathOf(Transform t)
        {
            var parts = new List<string>();
            for (; t != null; t = t.parent) parts.Add(BaseName(t.name));
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        public static List<Transform> SceneTransforms()
        {
            var list = new List<Transform>();
            foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
                if (t != null && t.gameObject.scene.IsValid() && t.gameObject.hideFlags == HideFlags.None) list.Add(t);
            return list;
        }

        public static Component FindComponent(Transform t, string typeName)
        {
            foreach (var c in t.GetComponents<Component>())
                if (c != null && c.GetType().Name == typeName) return c;
            return null;
        }
    }
}
