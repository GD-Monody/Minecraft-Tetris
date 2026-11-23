using System.Collections.Generic;
using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>
    /// 教程用的「点击挡板」。
    ///
    /// 为什么需要它：IMGUI 里**先画的控件先拿到事件**。教程面板在标题页之后画，
    /// 所以点「下一步」时，底下同位置的「设置 / 游戏规则」会先被点中 ——
    /// 表现就是「不点开始游戏，教程自己往下走了」。
    ///
    /// 这个组件挂在标题页**之前**，OnGUI 只画透明按钮、不画任何东西，专门把
    /// 该挡的区域吃掉。要留出来的区域（高亮那块、说明板、跳过键）由教程算好塞进来。
    ///
    /// 每帧的流程：挡板先跑（用上一帧算好的矩形）→ 标题页 / HUD → 教程（重算矩形）。
    /// 矩形一帧之内不会变，差一帧没有影响。
    /// </summary>
    public class TutorialBlocker : MonoBehaviour
    {
        static TutorialBlocker _instance;

        readonly List<Rect> _rects = new List<Rect>();

        public static void SetRects(List<Rect> rects)
        {
            if (_instance == null)
                return;
            _instance._rects.Clear();
            if (rects != null)
                _instance._rects.AddRange(rects);
        }

        public static void ClearRects()
        {
            if (_instance != null)
                _instance._rects.Clear();
        }

        void Awake()
        {
            _instance = this;
        }

        void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        void OnGUI()
        {
            if (_rects.Count == 0)
                return;

            // 透明按钮：看不见，但会登记成控件、把鼠标事件吃掉
            for (int i = 0; i < _rects.Count; i++)
            {
                Rect r = _rects[i];
                if (r.width < 1f || r.height < 1f)
                    continue;
                // 挡住的同时给一句提示：光静默吞掉点击，玩家会以为界面卡了
                if (GUI.Button(r, GUIContent.none, GUIStyle.none))
                    Tutorial.NotifyLocked();
            }
        }
    }
}
