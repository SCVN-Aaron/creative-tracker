using UnityEngine;

namespace CreativeRecorder
{
    /// <summary>Gắn tạm lúc Play vào object cần nghe va chạm, báo cho CreativeDirector phát SFX.</summary>
    [AddComponentMenu("")]
    public class CreativeTouchRelay : MonoBehaviour
    {
        void OnTriggerEnter(Collider other) { Send(other.gameObject); }
        void OnTriggerEnter2D(Collider2D other) { Send(other.gameObject); }
        void OnCollisionEnter(Collision c) { Send(c.gameObject); }
        void OnCollisionEnter2D(Collision2D c) { Send(c.gameObject); }
        void OnControllerColliderHit(ControllerColliderHit hit) { Send(hit.gameObject); }

        void Send(GameObject other)
        {
            var d = CreativeDirector.Instance;
            if (d != null && other != null) d.OnTouch(transform, other);
        }
    }
}
