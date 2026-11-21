// 网页版：把 Unity 的「屏幕方向」设置转给页面里的 JS。
//
// 页面那边（Assets/WebGLTemplates/MinecraftMobile/index.html）收到之后：
//   landscape / portrait → 先进全屏、再调 screen.orientation.lock()，让系统真的转屏；
//   auto → 解锁，跟着设备走。
// 系统转屏之后窗口尺寸变了，页面会自己把新尺寸写回 canvas.width/height，
// Unity 因此拿到横向（或竖向）的屏幕尺寸，所有「宽 > 高」的判断自动走对应分支。
//
// 之所以不在 JS 里用 transform: rotate 去「视觉转屏」：触摸坐标是按
// getBoundingClientRect 算的，画布一转，每根手指的落点就全错位了。
mergeInto(LibraryManager.library, {
  MT_SetOrientation: function (modePtr) {
    var mode = UTF8ToString(modePtr);
    if (typeof mtSetOrientation === 'function') {
      mtSetOrientation(mode);
    }
  }
});
