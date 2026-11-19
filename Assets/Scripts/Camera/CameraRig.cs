using System.Collections.Generic;
using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>轨道相机：右键拖动绕场地中心转，滚轮缩放；手机上单指转、双指捏合缩放。</summary>
    public class CameraRig : MonoBehaviour
    {
        public Vector3 Target;
        public float Distance = 24f;
        public float MinDistance = 8f;
        public float MaxDistance = 60f;
        public float Yaw;
        public float Pitch = 8f;

        /// <summary>手机上的操作层。给了它，被按键认领的手指就不拿来转视角。</summary>
        public MobileControls Controls;

        // 爆炸震屏：_shake 从 1 衰减到 0，位移按它的平方收，收尾不会突然停。
        float _shake;
        float _shakeAmp;

        readonly Dictionary<int, Vector2> _last = new Dictionary<int, Vector2>();
        float _pinchDist;

        /// <summary>抖一下。amount 是最大偏移（世界单位），多次调用取最大值。</summary>
        public void Shake(float amount)
        {
            _shakeAmp = Mathf.Max(_shakeAmp, amount);
            _shake = 1f;
        }

        /// <summary>
        /// 按场地尺寸反推相机距离，保证整块场地都进画面。
        ///
        /// 关键是 <see cref="Camera.aspect"/>：Unity 的 fieldOfView 是**竖直**视角，
        /// 横向视角由长宽比推出来。电脑上 16:9 横向很宽，把竖屏手机（0.45 左右）的
        /// 同一个竖直视角换算过去，横向只剩三十来度 —— 10 格宽的场地正好顶满，
        /// 稍微转一点就出画。所以这里横竖各算一个距离，取远的那个。
        ///
        /// 相机渲染区被控制条截掉一块（见 Bootstrap.SetCameraRect），viewport 变矮、
        /// aspect 跟着变窄，这里读到的就是真实的那个值，不用另外传参。
        /// </summary>
        public void FitToField(float halfWidth, float halfHeight, float margin)
        {
            Camera cam = GetComponent<Camera>();
            if (cam == null)
                return;

            float vHalf = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float byHeight = halfHeight / Mathf.Max(0.0001f, Mathf.Tan(vHalf));
            float hHalf = Mathf.Atan(Mathf.Tan(vHalf) * Mathf.Max(0.0001f, cam.aspect));
            float byWidth = halfWidth / Mathf.Max(0.0001f, Mathf.Tan(hHalf));

            Distance = Mathf.Max(byHeight, byWidth) * margin;
            MinDistance = Distance * 0.6f;
            MaxDistance = Distance * 1.9f;
        }

        void Update()
        {
            if (MobileMode.Enabled)
            {
                TouchCamera();
                // 电脑上被 -mobile 拉起来调试时没有触屏，右键仍然可以转视角
                if (MobileMode.Forced)
                    MouseCamera();
            }
            else
            {
                MouseCamera();
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

        void MouseCamera()
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
        }

        /// <summary>
        /// 触屏：单指在场地空白处拖动 = 转视角，双指 = 转视角 + 捏合缩放。
        /// 被操作层认领的手指（按在虚拟键或拖动区上的）一律跳过，
        /// 否则按着旋转键往旁边一滑，整个场地就跟着转了。
        /// </summary>
        void TouchCamera()
        {
            int free = 0;
            Vector2 sum = Vector2.zero;
            Vector2 first = Vector2.zero, second = Vector2.zero;

            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch t = Input.GetTouch(i);
                int id = t.fingerId;

                if (Controls != null && Controls.Claims(id))
                {
                    _last.Remove(id);
                    continue;
                }

                if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)
                {
                    _last.Remove(id);
                    continue;
                }

                Vector2 p = t.position;
                Vector2 prev;
                if (!_last.TryGetValue(id, out prev))
                {
                    _last[id] = p;
                    continue;     // 这一帧刚按下去，还没有位移可算
                }
                _last[id] = p;

                sum += p - prev;
                free++;
                if (free == 1)
                    first = p;
                else if (free == 2)
                    second = p;
            }

            if (free == 0)
            {
                _pinchDist = 0f;
                return;
            }

            // 单指时整段位移都算转动，双指时取平均再减半（两根手指常常不是同步动的）
            float k = free >= 2 ? 0.5f : 1f;
            Vector2 d = sum / free * k;
            Yaw += d.x * 0.35f;
            Pitch -= d.y * 0.35f;
            Pitch = Mathf.Clamp(Pitch, -80f, 80f);

            if (free >= 2)
            {
                float now = Vector2.Distance(first, second);
                if (_pinchDist > 1f && now > 1f)
                {
                    // 手指拉开 = 靠近：位移比例直接映射到距离，方向跟桌面滚轮一致
                    Distance -= (now - _pinchDist) * 0.06f;
                    Distance = Mathf.Clamp(Distance, MinDistance, MaxDistance);
                }
                _pinchDist = now;
            }
            else
            {
                _pinchDist = 0f;
            }
        }
    }
}
