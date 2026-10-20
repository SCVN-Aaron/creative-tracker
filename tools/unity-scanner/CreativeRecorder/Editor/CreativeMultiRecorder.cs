using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace CreativeRecorder
{
    /// <summary>
    /// Quay nhiều khung hình cùng lúc (dọc, ngang, vuông…) bằng package Unity Recorder: mỗi khung là một Movie Recorder
    /// lấy hình từ camera đang chạy với kích thước riêng, nên camera tự mở rộng đúng tỉ lệ của từng video.
    /// Gọi Recorder qua reflection để tool vẫn biên dịch khi project chưa cài package và chạy được với nhiều phiên bản.
    /// </summary>
    public static class CreativeMultiRecorder
    {
        const string Package = "com.unity.recorder";
        static object controller;
        static double startTime;

        public static string LastFolder { get; private set; }

        public static bool Installed { get { return T("UnityEditor.Recorder.RecorderController") != null; } }

        public static bool IsRecording
        {
            get
            {
                if (controller == null) return false;
                try { return (bool)Call(controller, "IsRecording"); }
                catch (Exception) { return false; }
            }
        }

        public static double Elapsed { get { return EditorApplication.timeSinceStartup - startTime; } }

        public static void Install()
        {
            UnityEditor.PackageManager.Client.Add(Package);
        }

        /// <summary>Bắt đầu quay. Trả về null nếu thành công, ngược lại là lý do lỗi.</summary>
        public static string Start(CreativePreset p)
        {
            Stop();
            var sizes = p.sizes.Where(s => s.enabled && s.width > 0 && s.height > 0).ToList();
            if (sizes.Count == 0) return "Chưa chọn khung hình nào.";
            try
            {
                var tSettings = T("UnityEditor.Recorder.RecorderControllerSettings");
                var tMovie = T("UnityEditor.Recorder.MovieRecorderSettings");
                var tCamera = T("UnityEditor.Recorder.Input.CameraInputSettings");
                var tSource = T("UnityEditor.Recorder.Input.ImageSource");
                var tController = T("UnityEditor.Recorder.RecorderController");
                if (tSettings == null || tMovie == null || tCamera == null || tSource == null || tController == null)
                    return "Không tìm thấy Unity Recorder — cài package " + Package + ".";

                string root = Path.GetDirectoryName(Application.dataPath);
                LastFolder = Path.Combine(Path.Combine(root, "Recordings"), DateTime.Now.ToString("yyyy-MM-dd_HHmmss"));
                Directory.CreateDirectory(LastFolder);

                var settings = ScriptableObject.CreateInstance(tSettings);
                foreach (var s in sizes)
                {
                    var movie = ScriptableObject.CreateInstance(tMovie);
                    movie.name = s.name;
                    Set(movie, "Enabled", true);
                    string file = Safe(s.name) + "_" + Even(s.width) + "x" + Even(s.height);
                    Set(movie, "OutputFile", Path.Combine(LastFolder, file).Replace('\\', '/'));

                    var input = Activator.CreateInstance(tCamera);
                    Set(input, "Source", Enum.Parse(tSource, "ActiveCamera"));
                    Set(input, "OutputWidth", Even(s.width));
                    Set(input, "OutputHeight", Even(s.height));
                    TrySet(input, "CaptureUI", p.recordUI);
                    Set(movie, "ImageInputSettings", input);

                    var audio = TryGet(movie, "AudioInputSettings");
                    if (audio != null) TrySet(audio, "PreserveAudio", p.recordAudio);
                    Call(settings, "AddRecorderSettings", movie);
                }
                Call(settings, "SetRecordModeToManual");
                Set(settings, "FrameRate", (float)p.recordFps);
                var tPlayback = T("UnityEditor.Recorder.FrameRatePlayback");
                if (tPlayback != null) TrySet(settings, "FrameRatePlayback", Enum.Parse(tPlayback, "Constant"));
                TrySet(settings, "CapFrameRate", true);

                controller = Activator.CreateInstance(tController, settings);
                Call(controller, "PrepareRecording");
                var ok = Call(controller, "StartRecording");
                if (ok is bool && !(bool)ok) { controller = null; return "Unity Recorder không bắt đầu được — xem Console."; }
                startTime = EditorApplication.timeSinceStartup;
                return null;
            }
            catch (Exception e)
            {
                controller = null;
                var inner = e is TargetInvocationException && e.InnerException != null ? e.InnerException : e;
                Debug.LogException(inner);
                return inner.Message;
            }
        }

        public static void Stop()
        {
            if (controller == null) return;
            try { Call(controller, "StopRecording"); }
            catch (Exception) { }
            controller = null;
        }

        /// <summary>H.264 cần kích thước chẵn.</summary>
        public static int Even(int v) { return Math.Max(2, v - v % 2); }

        static string Safe(string s)
        {
            if (string.IsNullOrEmpty(s)) s = "video";
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Replace(' ', '_');
        }

        static Type T(string name)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = a.GetType(name);
                if (t != null) return t;
            }
            return null;
        }

        static void Set(object o, string name, object v)
        {
            if (!TrySet(o, name, v)) throw new MissingMemberException(o.GetType().Name, name);
        }

        static bool TrySet(object o, string name, object v)
        {
            var t = o.GetType();
            var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            if (p != null && p.CanWrite) { p.SetValue(o, v, null); return true; }
            var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public);
            if (f != null) { f.SetValue(o, v); return true; }
            return false;
        }

        static object TryGet(object o, string name)
        {
            var t = o.GetType();
            var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            if (p != null) return p.GetValue(o, null);
            var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public);
            return f != null ? f.GetValue(o) : null;
        }

        static object Call(object o, string name, params object[] args)
        {
            var m = o.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
                     .FirstOrDefault(x => x.Name == name && x.GetParameters().Length == args.Length);
            if (m == null) throw new MissingMethodException(o.GetType().Name, name);
            return m.Invoke(o, args);
        }
    }
}
