using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>轨道相机：右键拖动绕场地中心转，滚轮缩放。</summary>
    public class CameraRig : MonoBehaviour
    {
        public Vector3 Target;
        public float Distance = 24f;
        public float MinDistance = 8f;
        public float MaxDistance = 60f;
        public float Yaw;
        public float Pitch = 8f;

        void Update()
        {
            if (Input.GetMouseButton(1))
            {
                Yaw += Input.GetAxis("Mouse X") * 4f;
                Pitch -= Input.GetAxis("Mouse Y") * 4f;
                Pitch = Mathf.Clamp(Pitch, -80f, 80f);
            }

            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.0001f)
            {
                Distance -= scroll * 6f;
                Distance = Mathf.Clamp(Distance, MinDistance, MaxDistance);
            }

            Quaternion rot = Quaternion.Euler(Pitch, Yaw, 0f);
            transform.position = Target + rot * new Vector3(0f, 0f, -Distance);
            transform.LookAt(Target);
        }
    }
}
