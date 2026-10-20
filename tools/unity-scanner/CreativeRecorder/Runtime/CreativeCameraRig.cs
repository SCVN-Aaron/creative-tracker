using UnityEngine;

namespace CreativeRecorder
{
    /// <summary>
    /// Đầu mỗi frame (trước script game): trả camera về đúng vị trí / góc / FOV mà game đã đặt,
    /// để script camera của game không bị ảnh hưởng bởi phần chỉnh của tool.
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    [AddComponentMenu("")]
    public class CreativeCameraEarly : MonoBehaviour
    {
        public CreativeCameraLate late;
        void FixedUpdate() { if (late != null) late.Restore(); }
        void Update() { if (late != null) late.Restore(); }
    }

    /// <summary>
    /// Cuối mỗi frame (sau script game và Cinemachine): áp dụng zoom, xoay, dịch, góc cố định, bám theo object.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    [AddComponentMenu("")]
    public class CreativeCameraLate : MonoBehaviour
    {
        public CreativeDirector director;

        Camera cam;
        float nextFind;
        bool applied, followInit;
        Vector3 basePos, appliedPos, followPos, followVel;
        Quaternion baseRot, appliedRot;
        float baseFov, appliedFov, baseOrtho, appliedOrtho;
        bool mapValid;
        Quaternion mapBase, mapApplied;

        public Camera Target { get { return cam; } }

        /// <summary>
        /// Đổi hướng joystick theo góc camera mới: game vẫn tính hướng chạy theo góc camera gốc (hoặc trục thế giới),
        /// nên đẩy “lên” trên màn hình phải đổi thành hướng tương ứng trong góc gốc.
        /// </summary>
        public Vector2 MapInput(Vector2 v)
        {
            if (!mapValid || v.sqrMagnitude < 1e-8f) return v;
            Vector2 ua, ra, ub, rb;
            Basis(mapApplied, out ua, out ra);
            Basis(mapBase, out ub, out rb);
            var d = v.x * ra + v.y * ua;                       // hướng người chơi muốn, trên mặt đất
            float det = rb.x * ub.y - rb.y * ub.x;
            if (Mathf.Abs(det) < 1e-4f) return v;
            var r = new Vector2((d.x * ub.y - d.y * ub.x) / det, (rb.x * d.y - rb.y * d.x) / det);
            float m = r.magnitude;
            return m > 1e-6f ? r * (v.magnitude / m) : v;
        }

        /// <summary>Hướng “lên” và “phải” của màn hình chiếu xuống mặt đất.</summary>
        static void Basis(Quaternion rot, out Vector2 up, out Vector2 right)
        {
            var u = rot * Vector3.up;
            u.y = 0f;
            if (u.sqrMagnitude < 0.04f) { u = rot * Vector3.forward; u.y = 0f; }   // camera nhìn ngang
            var rt = rot * Vector3.right;
            rt.y = 0f;
            up = new Vector2(u.x, u.z).normalized;
            right = new Vector2(rt.x, rt.z).normalized;
        }

        public void Restore()
        {
            if (!applied || cam == null) return;
            applied = false;
            var t = cam.transform;
            if (t.position == appliedPos && t.rotation == appliedRot)
            {
                t.position = basePos;
                t.rotation = baseRot;
            }
            if (cam.orthographic) { if (cam.orthographicSize == appliedOrtho) cam.orthographicSize = baseOrtho; }
            else if (cam.fieldOfView == appliedFov) cam.fieldOfView = baseFov;
        }

        void LateUpdate()
        {
            if (cam == null || Time.unscaledTime >= nextFind)
            {
                var found = FindCamera();
                if (found != cam) { Restore(); cam = found; followInit = false; }
                nextFind = Time.unscaledTime + 1f;
            }
            Restore();
            var p = director != null ? director.preset : null;
            var rig = p != null && p.enabled ? p.cam : null;
            if (rig == null || rig.IsDefault || cam == null) { followInit = false; mapValid = false; return; }

            var t = cam.transform;
            basePos = t.position;
            baseRot = t.rotation;
            baseFov = cam.fieldOfView;
            baseOrtho = cam.orthographicSize;

            Vector3 pos = basePos;
            Quaternion rot = baseRot;
            float fov = baseFov;
            if (rig.mode == CameraRig.Fixed)
            {
                pos = rig.fixedPos;
                rot = Quaternion.Euler(rig.fixedEuler);
                if (rig.fixedFov > 0f) fov = rig.fixedFov;
            }
            else if (rig.mode == CameraRig.Follow)
            {
                var target = director.FindTransform(rig.followPath);
                if (target != null)
                {
                    var want = target.position + rig.followOffset;
                    if (!followInit) { followPos = want; followVel = Vector3.zero; followInit = true; }
                    followPos = rig.smooth > 0f ? Vector3.SmoothDamp(followPos, want, ref followVel, rig.smooth, Mathf.Infinity, Time.unscaledDeltaTime) : want;
                    pos = followPos;
                    rot = Quaternion.Euler(rig.fixedEuler);
                    if (rig.fixedFov > 0f) fov = rig.fixedFov;
                }
            }

            // xoay quanh điểm camera đang nhìn, đẩy gần / xa, lăn, dịch
            if (rig.yaw != 0f || rig.pitch != 0f || rig.distance != 1f)
            {
                var pivot = Pivot(pos, rot);
                var q = Quaternion.AngleAxis(rig.yaw, Vector3.up) * Quaternion.AngleAxis(rig.pitch, rot * Vector3.right);
                pos = pivot + q * (pos - pivot) * Mathf.Max(0.05f, rig.distance);
                rot = q * rot;
            }
            rot = rot * Quaternion.Euler(0f, 0f, rig.roll);
            pos += rot * rig.offset;
            t.position = pos;
            t.rotation = rot;

            float z = Mathf.Max(0.05f, rig.zoom);
            if (cam.orthographic) cam.orthographicSize = baseOrtho / z;
            else cam.fieldOfView = Mathf.Clamp(2f * Mathf.Atan(Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) / z) * Mathf.Rad2Deg, 1f, 170f);

            appliedPos = t.position;
            appliedRot = t.rotation;
            appliedFov = cam.fieldOfView;
            appliedOrtho = cam.orthographicSize;
            applied = true;
            mapBase = baseRot;
            mapApplied = rot;
            mapValid = rig.fixJoystick;
        }

        void OnDisable() { Restore(); }

        /// <summary>Điểm camera đang nhìn vào: chỗ tia nhìn chạm vật / mặt đất, không thì cách 10m.</summary>
        static Vector3 Pivot(Vector3 pos, Quaternion rot)
        {
            var fwd = rot * Vector3.forward;
            RaycastHit hit;
            if (Physics.Raycast(pos, fwd, out hit, 500f, ~0, QueryTriggerInteraction.Ignore)) return hit.point;
            if (Mathf.Abs(fwd.y) > 0.05f)
            {
                float d = -pos.y / fwd.y;
                if (d > 0f && d < 500f) return pos + fwd * d;
            }
            return pos + fwd * 10f;
        }

        public static Camera FindCamera()
        {
            var m = Camera.main;
            if (m != null && m.isActiveAndEnabled) return m;
            Camera best = null;
            foreach (var c in Camera.allCameras)
                if (c.targetTexture == null && (best == null || c.depth > best.depth)) best = c;
            return best;
        }
    }
}
