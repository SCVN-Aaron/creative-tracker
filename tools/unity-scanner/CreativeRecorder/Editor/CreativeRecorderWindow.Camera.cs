using System;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CreativeRecorder
{
    /// <summary>Trang "Camera" (chỉnh camera tự do) và phần "Quay nhiều khung cùng lúc" của trang Quay.</summary>
    public partial class CreativeRecorderWindow
    {
        GameObject followPick;

        // ================================================================== CAMERA

        void DrawCameraPage()
        {
            var rig = P.cam;
            bool playing = Application.isPlaying;
            var cam = CurrentGameCamera();
            GUILayout.Label("Chỉnh camera tự do: zoom, xoay, đẩy gần / xa, góc cố định hoặc bám theo object. Không sửa camera gốc — dừng Play là như cũ.", stMuted);
            if (!playing) EditorGUILayout.HelpBox("Bấm ▶ Play để thấy camera thay đổi ngay khi kéo thanh trượt.", MessageType.None);
            GUILayout.Label("Camera: " + (cam != null ? cam.name + (cam.orthographic ? " (2D / orthographic)" : "") : "không tìm thấy"), EditorStyles.miniBoldLabel);

            string before = JsonUtility.ToJson(rig);
            EditorGUI.BeginChangeCheck();

            // ---- chế độ
            GUILayout.Label("Camera đi theo", stH2);
            int mode = GUILayout.Toolbar(rig.mode, new[] { "Theo game", "Góc cố định", "Bám theo object" }, GUILayout.Height(28));
            if (mode != rig.mode)
            {
                if (mode == CameraRig.Fixed && rig.fixedPos == Vector3.zero && rig.fixedEuler == Vector3.zero) CaptureFromGameCamera(rig, cam, null);
                rig.mode = mode;
            }
            switch (rig.mode)
            {
                case CameraRig.Game:
                    GUILayout.Label("Camera vẫn chạy theo game (bám nhân vật…); các chỉnh bên dưới cộng thêm vào.", stMuted);
                    break;
                case CameraRig.Fixed:
                    GUILayout.Label("Camera đứng yên ở một góc. Cách nhanh nhất: xoay Scene view tới góc đẹp rồi bấm “Lấy góc Scene view”.", stMuted);
                    EditorGUILayout.BeginHorizontal();
                    if (Tinted("Lấy góc Scene view", Blue, stBig)) CaptureFromSceneView(rig, null);
                    if (GUILayout.Button("Lấy góc camera game", stBig)) CaptureFromGameCamera(rig, cam, null);
                    EditorGUILayout.EndHorizontal();
                    break;
                case CameraRig.Follow:
                    GUILayout.Label("Camera giữ khoảng cách cố định với một object (nhân vật, xe…). Chọn object, rồi lấy khoảng cách từ Scene view hoặc camera game.", stMuted);
                    var cur = !string.IsNullOrEmpty(rig.followPath) ? FindScene(rig.followPath) : null;
                    if (followPick == null) followPick = cur;
                    followPick = (GameObject)EditorGUILayout.ObjectField("Bám theo", followPick, typeof(GameObject), true);
                    if (followPick != null && !EditorUtility.IsPersistent(followPick)) rig.followPath = CreativeReflect.PathOf(followPick.transform);
                    GUI.enabled = followPick != null;
                    EditorGUILayout.BeginHorizontal();
                    if (Tinted("Lấy khoảng cách từ Scene view", Blue, stBig)) CaptureFromSceneView(rig, followPick.transform);
                    if (GUILayout.Button("Lấy từ camera game", stBig)) CaptureFromGameCamera(rig, cam, followPick.transform);
                    EditorGUILayout.EndHorizontal();
                    GUI.enabled = true;
                    rig.smooth = EditorGUILayout.Slider(new GUIContent("Độ mượt", "0 = bám chặt, lớn hơn = trễ mềm như quay phim"), rig.smooth, 0f, 1f);
                    break;
            }

            // ---- zoom
            GUILayout.Space(6);
            GUILayout.Label("Zoom", stH2);
            EditorGUILayout.BeginHorizontal();
            foreach (var z in new[] { 0.5f, 1f, 1.5f, 2f, 3f, 4f })
                if (Choice(z.ToString(CultureInfo.InvariantCulture) + "×", Mathf.Approximately(rig.zoom, z))) rig.zoom = z;
            EditorGUILayout.EndHorizontal();
            rig.zoom = EditorGUILayout.Slider("Zoom", rig.zoom, 0.25f, 8f);
            rig.distance = EditorGUILayout.Slider(new GUIContent("Gần / xa", "Đưa camera lại gần (< 1) hoặc lùi ra xa (> 1) điểm đang nhìn — khác zoom: góc nhìn thay đổi thật"), rig.distance, 0.2f, 4f);

            // ---- xoay
            GUILayout.Space(6);
            GUILayout.Label("Xoay", stH2);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Đảo ngược 180°", GUILayout.Height(26))) rig.yaw = Wrap(rig.yaw + 180f);
            if (GUILayout.Button("Lộn ngược", GUILayout.Height(26))) rig.roll = Wrap(rig.roll + 180f);
            if (GUILayout.Button("↶ 45°", GUILayout.Height(26))) rig.yaw = Wrap(rig.yaw - 45f);
            if (GUILayout.Button("↷ 45°", GUILayout.Height(26))) rig.yaw = Wrap(rig.yaw + 45f);
            EditorGUILayout.EndHorizontal();
            rig.yaw = EditorGUILayout.Slider(new GUIContent("Xoay quanh", "Đi vòng quanh điểm camera đang nhìn"), rig.yaw, -180f, 180f);
            rig.pitch = EditorGUILayout.Slider(new GUIContent("Cao / thấp", "Dương = nhìn từ trên cao xuống, âm = góc thấp"), rig.pitch, -80f, 80f);
            rig.roll = EditorGUILayout.Slider(new GUIContent("Nghiêng", "Nghiêng khung hình kiểu điện ảnh"), rig.roll, -180f, 180f);
            rig.fixJoystick = EditorGUILayout.ToggleLeft(new GUIContent("Joystick đi theo góc camera mới",
                "Khi camera bị xoay (đảo 180°, xoay quanh, góc cố định, bám theo object), đẩy joystick lên là nhân vật chạy lên trên màn hình. Tắt nếu muốn giữ hướng điều khiển gốc của game."), rig.fixJoystick);
            if (playing && rig.fixJoystick)
            {
                var remap = CreativeDirector.Instance != null ? CreativeDirector.Instance.GetComponent<CreativeInputRemap>() : null;
                int found = remap != null ? remap.Count : 0;
                GUILayout.Label(found > 0 ? "Đã nhận joystick của game — hướng điều khiển tự xoay theo camera."
                                          : "Chưa thấy joystick dạng script (FloatingJoystick, DynamicJoystick…). Game điều khiển bằng vuốt màn hình / phím thì hướng chưa được xoay — báo lại để thêm.", stMuted);
            }

            // ---- dịch
            GUILayout.Space(6);
            GUILayout.Label("Dịch khung hình", stH2);
            rig.offset = EditorGUILayout.Vector3Field(new GUIContent("Trái-phải / lên-xuống / tới-lui"), rig.offset);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("←")) rig.offset.x -= 0.5f;
            if (GUILayout.Button("→")) rig.offset.x += 0.5f;
            if (GUILayout.Button("↑")) rig.offset.y += 0.5f;
            if (GUILayout.Button("↓")) rig.offset.y -= 0.5f;
            if (GUILayout.Button("Tới")) rig.offset.z += 0.5f;
            if (GUILayout.Button("Lui")) rig.offset.z -= 0.5f;
            EditorGUILayout.EndHorizontal();

            // ---- góc có sẵn
            GUILayout.Space(6);
            GUILayout.Label("Góc có sẵn", stH2);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Từ trên xuống", stBig)) { rig.ResetView(); rig.pitch = 45f; }
            if (GUILayout.Button("Góc thấp", stBig)) { rig.ResetView(); rig.pitch = -20f; rig.distance = 0.7f; }
            if (GUILayout.Button("Cận cảnh", stBig)) { rig.ResetView(); rig.zoom = 2f; rig.distance = 0.6f; }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Toàn cảnh", stBig)) { rig.ResetView(); rig.zoom = 0.75f; rig.distance = 1.6f; }
            if (GUILayout.Button("Nghiêng điện ảnh", stBig)) { rig.ResetView(); rig.yaw = 25f; rig.roll = 8f; rig.distance = 0.85f; }
            if (Tinted("Về mặc định", Red, stBig)) { rig.ResetView(); rig.mode = CameraRig.Game; }
            EditorGUILayout.EndHorizontal();

            if (EditorGUI.EndChangeCheck() || JsonUtility.ToJson(rig) != before) CreativePresetStore.Save();
        }

        static bool Choice(string text, bool on)
        {
            var old = GUI.backgroundColor;
            if (on) GUI.backgroundColor = Blue;
            bool r = GUILayout.Button(text, GUILayout.Height(28));
            GUI.backgroundColor = old;
            return r;
        }

        static float Wrap(float a)
        {
            a %= 360f;
            if (a > 180f) a -= 360f;
            if (a < -180f) a += 360f;
            return a;
        }

        static Camera CurrentGameCamera()
        {
            if (Application.isPlaying && CreativeDirector.Instance != null)
            {
                var late = CreativeDirector.Instance.GetComponent<CreativeCameraLate>();
                if (late != null && late.Target != null) return late.Target;
            }
            return CreativeCameraLate.FindCamera();
        }

        static GameObject FindScene(string path)
        {
            return SceneObjects().FirstOrDefault(g => CreativeReflect.PathOf(g.transform) == path);
        }

        void CaptureFromSceneView(CameraRig rig, Transform target)
        {
            var sv = SceneView.lastActiveSceneView;
            if (sv == null || sv.camera == null) { ShowNotification(new GUIContent("Mở cửa sổ Scene view trước")); return; }
            CapturePose(rig, sv.camera.transform.position, sv.camera.transform.rotation, sv.camera.orthographic ? 0f : sv.camera.fieldOfView, target);
        }

        void CaptureFromGameCamera(CameraRig rig, Camera cam, Transform target)
        {
            if (cam == null) { ShowNotification(new GUIContent("Không tìm thấy camera game")); return; }
            CapturePose(rig, cam.transform.position, cam.transform.rotation, cam.orthographic ? 0f : cam.fieldOfView, target);
        }

        void CapturePose(CameraRig rig, Vector3 pos, Quaternion rot, float fov, Transform target)
        {
            rig.fixedEuler = rot.eulerAngles;
            rig.fixedFov = fov;
            if (target != null) { rig.followOffset = pos - target.position; rig.mode = CameraRig.Follow; }
            else { rig.fixedPos = pos; rig.mode = CameraRig.Fixed; }
            rig.yaw = rig.pitch = rig.roll = 0f;
            rig.zoom = 1f;
            rig.distance = 1f;
            rig.offset = Vector3.zero;
            CreativePresetStore.Save();
            ShowNotification(new GUIContent("Đã lấy góc máy"));
        }

        // ================================================================== QUAY NHIỀU KHUNG

        void DrawMultiRecord()
        {
            GUILayout.Label("Quay nhiều khung cùng lúc", stH2);
            GUILayout.Label("Game view vẫn để dọc như bình thường, tool ghi cùng lúc nhiều video (dọc, ngang, vuông…). Mỗi video camera tự mở rộng đúng tỉ lệ, kèm âm thanh.", stMuted);
            if (!CreativeMultiRecorder.Installed)
            {
                EditorGUILayout.HelpBox("Cần package Unity Recorder (miễn phí, của Unity). Bấm nút dưới, chờ Unity cài xong là dùng được.", MessageType.Info);
                if (Tinted("Cài Unity Recorder", Blue, stBig)) CreativeMultiRecorder.Install();
                return;
            }
            if (P.sizes.Count == 0) { DefaultSizes(); CreativePresetStore.Save(); }

            string before = JsonUtility.ToJson(P);
            EditorGUI.BeginChangeCheck();
            RecordSize remove = null;
            foreach (var s in P.sizes)
            {
                EditorGUILayout.BeginHorizontal(stCard);
                s.enabled = EditorGUILayout.Toggle(s.enabled, GUILayout.Width(16));
                s.name = EditorGUILayout.TextField(s.name, GUILayout.Width(90));
                s.width = Mathf.Clamp(EditorGUILayout.IntField(s.width, GUILayout.Width(56)), 16, 4096);
                GUILayout.Label("×", GUILayout.Width(12));
                s.height = Mathf.Clamp(EditorGUILayout.IntField(s.height, GUILayout.Width(56)), 16, 4096);
                GUILayout.Label(Ratio(s.width, s.height), EditorStyles.miniLabel, GUILayout.Width(42));
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Xoá", EditorStyles.miniButton, GUILayout.Width(36))) remove = s;
                EditorGUILayout.EndHorizontal();
            }
            if (remove != null) P.sizes.Remove(remove);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Thêm khung:", GUILayout.Width(80));
            if (GUILayout.Button("Dọc 9:16")) AddSize("Doc", 1080, 1920);
            if (GUILayout.Button("Ngang 16:9")) AddSize("Ngang", 1920, 1080);
            if (GUILayout.Button("Vuông 1:1")) AddSize("Vuong", 1080, 1080);
            if (GUILayout.Button("4:5")) AddSize("4x5", 1080, 1350);
            if (GUILayout.Button("Tự nhập")) AddSize("Khung", 1280, 720);
            EditorGUILayout.EndHorizontal();

            var fpsOptions = new[] { 24, 25, 30, 60 };
            int fi = Array.IndexOf(fpsOptions, P.recordFps);
            fi = EditorGUILayout.Popup("Số khung hình / giây", fi < 0 ? 2 : fi, fpsOptions.Select(f => f + " fps").ToArray());
            P.recordFps = fpsOptions[fi];
            P.recordUI = EditorGUILayout.ToggleLeft("Ghi cả UI (tiền, nút, chữ…)", P.recordUI);
            P.recordAudio = EditorGUILayout.ToggleLeft("Ghi âm thanh", P.recordAudio);
            if (EditorGUI.EndChangeCheck() || JsonUtility.ToJson(P) != before) CreativePresetStore.Save();

            int n = P.sizes.Count(s => s.enabled);
            if (CreativeMultiRecorder.IsRecording)
            {
                var t = TimeSpan.FromSeconds(CreativeMultiRecorder.Elapsed);
                if (Tinted(string.Format("■  Dừng quay   {0:00}:{1:00}", (int)t.TotalMinutes, t.Seconds), Red, stBig)) CreativeMultiRecorder.Stop();
            }
            else
            {
                GUI.enabled = Application.isPlaying && n > 0;
                if (Tinted("●  Quay " + n + " khung", Green, stBig))
                {
                    string err = CreativeMultiRecorder.Start(P);
                    if (err != null) EditorUtility.DisplayDialog("Không quay được", err, "OK");
                }
                GUI.enabled = true;
                if (!Application.isPlaying) GUILayout.Label("Bấm ▶ Play trước rồi mới quay.", stMuted);
            }
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Video lưu ở thư mục Recordings/ cạnh Assets, mỗi lần quay một thư mục theo giờ.", stMuted);
            GUI.enabled = !string.IsNullOrEmpty(CreativeMultiRecorder.LastFolder);
            if (GUILayout.Button("Mở thư mục", EditorStyles.miniButton, GUILayout.Width(80))) EditorUtility.RevealInFinder(CreativeMultiRecorder.LastFolder);
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
        }

        static void DefaultSizes()
        {
            P.sizes.Add(new RecordSize { name = "Doc", width = 1080, height = 1920 });
            P.sizes.Add(new RecordSize { name = "Ngang", width = 1920, height = 1080 });
            P.sizes.Add(new RecordSize { name = "Vuong", width = 1080, height = 1080 });
        }

        static void AddSize(string name, int w, int h)
        {
            P.sizes.Add(new RecordSize { name = name, width = w, height = h });
            CreativePresetStore.Save();
        }

        static string Ratio(int w, int h)
        {
            int a = w, b = h;
            while (b != 0) { int t = a % b; a = b; b = t; }
            if (a <= 0) return "";
            int rw = w / a, rh = h / a;
            return rw > 50 || rh > 50 ? (w / (float)h).ToString("0.00", CultureInfo.InvariantCulture) : rw + ":" + rh;
        }
    }
}
