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

        // 爆炸震屏：_shake 从 1 衰减到 0，位移按它的平方收，收尾不会突然停。
        float _shake;
        float _shakeAmp;

        /// <summary>抖一下。amount 是最大偏移（世界单位），多次调用取最大值。</summary>
        public void Shake(float amount)
        {
            _shakeAmp = Mathf.Max(_shakeAmp, amount);
            _shake = 1f;
        }

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

            if (_shake > 0f)
            {
                // 位置每帧都从 Target 重算，所以这里直接加偏移不会累积
                _shake = Mathf.Max(0f, _shake - Time.unscaledDeltaTime * 2.6f);
                float amp = _shakeAmp * _shake * _shake;
                transform.position += (transform.right * Random.Range(-1f, 1f)
                                       + transform.up * Random.Range(-1f, 1f)) * amp;
                if (_shake <= 0f)
                    _shakeAmp = 0f;
            }
        }
    }
}
