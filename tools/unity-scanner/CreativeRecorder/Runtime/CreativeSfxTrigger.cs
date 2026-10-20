using UnityEngine;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

namespace CreativeRecorder
{
    /// <summary>
    /// Phát một SFX khi có hành động — gắn vào bất kỳ GameObject nào, không cần sửa code game.
    /// Dùng cho việc quay creative; xoá thư mục CreativeRecorder trước khi build playable gửi đi.
    /// </summary>
    [AddComponentMenu("Creative Recorder/SFX Trigger")]
    public class CreativeSfxTrigger : MonoBehaviour
    {
        public enum Mode
        {
            [InspectorName("Nhấn phím")] KeyDown,
            [InspectorName("Click chuột / chạm màn hình")] MouseOrTap,
            [InspectorName("Khi object bật (OnEnable)")] OnEnable,
            [InspectorName("Khi object tắt (OnDisable)")] OnDisable,
            [InspectorName("Va chạm trigger (OnTriggerEnter)")] TriggerEnter,
            [InspectorName("Va chạm vật lý (OnCollisionEnter)")] CollisionEnter,
            [InspectorName("Gọi từ Button / UnityEvent (Play)")] ManualCall
        }

        public Mode mode = Mode.KeyDown;
        public KeyCode key = KeyCode.Space;
        public AudioClip clip;
        [Range(0f, 1f)] public float volume = 1f;
        [Tooltip("Đổi cao độ ngẫu nhiên ± giá trị này mỗi lần phát, nghe đỡ lặp")]
        [Range(0f, 0.5f)] public float pitchRandom = 0f;
        [Tooltip("Chỉ phát khi va chạm với object có tag này (để trống = mọi object)")]
        public string requiredTag = "";
        [Tooltip("Khoảng cách tối thiểu giữa hai lần phát (giây)")]
        public float cooldown = 0.05f;

        AudioSource source;
        float lastTime = -999f;
        static AudioSource shared;

        void Awake()
        {
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.hideFlags = HideFlags.HideInInspector | HideFlags.DontSave;
        }

        void OnEnable()
        {
            if (mode == Mode.OnEnable) Play();
        }

        void OnDisable()
        {
            if (mode == Mode.OnDisable && clip != null) PlayClip(clip, volume);
        }

        void Update()
        {
            if (mode == Mode.KeyDown && KeyDown(key)) Play();
            else if (mode == Mode.MouseOrTap && PointerDown()) Play();
        }

        void OnTriggerEnter(Collider other) { if (mode == Mode.TriggerEnter && TagOk(other.gameObject)) Play(); }
        void OnTriggerEnter2D(Collider2D other) { if (mode == Mode.TriggerEnter && TagOk(other.gameObject)) Play(); }
        void OnCollisionEnter(Collision c) { if (mode == Mode.CollisionEnter && TagOk(c.gameObject)) Play(); }
        void OnCollisionEnter2D(Collision2D c) { if (mode == Mode.CollisionEnter && TagOk(c.gameObject)) Play(); }

        bool TagOk(GameObject other)
        {
            return string.IsNullOrEmpty(requiredTag) || other.CompareTag(requiredTag);
        }

        /// <summary>Gọi được từ Button.onClick, Animation Event, Timeline Signal…</summary>
        public void Play()
        {
            if (clip == null || Time.unscaledTime - lastTime < cooldown) return;
            lastTime = Time.unscaledTime;
            if (source == null) Awake();
            source.pitch = 1f + Random.Range(-pitchRandom, pitchRandom);
            source.PlayOneShot(clip, volume);
        }

        /// <summary>Chèn một dòng vào code game: CreativeRecorder.CreativeSfxTrigger.PlayClip(clip);</summary>
        public static void PlayClip(AudioClip clip, float volume = 1f)
        {
            if (clip == null) return;
            if (shared == null)
            {
                var go = new GameObject("[CreativeSfx]");
                go.hideFlags = HideFlags.DontSave;
                DontDestroyOnLoad(go);
                shared = go.AddComponent<AudioSource>();
                shared.playOnAwake = false;
            }
            shared.PlayOneShot(clip, volume);
        }

        public static bool KeyDown(KeyCode k)
        {
#if ENABLE_LEGACY_INPUT_MANAGER || !ENABLE_INPUT_SYSTEM
            return Input.GetKeyDown(k);
#else
            var kb = Keyboard.current;
            if (kb == null) return false;
            string n = k.ToString();
            if (n.StartsWith("Alpha")) n = "Digit" + n.Substring(5);
            else if (n.StartsWith("Keypad") && n.Length == 7) n = "Numpad" + n.Substring(6);
            else if (n == "Return") n = "Enter";
            else if (n == "LeftControl") n = "LeftCtrl";
            else if (n == "RightControl") n = "RightCtrl";
            else if (n == "BackQuote") n = "Backquote";
            Key key;
            if (!System.Enum.TryParse(n, out key)) return false;
            return kb[key].wasPressedThisFrame;
#endif
        }

        public static bool PointerDown()
        {
#if ENABLE_LEGACY_INPUT_MANAGER || !ENABLE_INPUT_SYSTEM
            if (Input.GetMouseButtonDown(0)) return true;
            for (int i = 0; i < Input.touchCount; i++)
                if (Input.GetTouch(i).phase == TouchPhase.Began) return true;
            return false;
#else
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) return true;
            return Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame;
#endif
        }
    }
}
