// 浏览器里给 Unity 用的「中文输入」浮层。
//
// 为什么需要它：Unity WebGL 的 IMGUI TextField 只接收键盘上直接敲出来的字符，
// 输入法的候选/上屏事件进不来 —— 玩家只能打英文（创意工坊改名时踩到的）。
// 这里在画布上方盖一个真正的 HTML <input>，用浏览器自己的输入法收字，
// 再把值读回 C#（见 WebGLTextInput.cs）。

mergeInto(LibraryManager.library, {
  WBIME_Open: function (gx, gy, gw, gh, sw, sh, initialPtr) {
    var canvas = document.querySelector('#unity-canvas') || document.querySelector('canvas');
    if (!canvas) return;

    var el = document.getElementById('wb-ime-input');
    if (!el) {
      el = document.createElement('input');
      el.id = 'wb-ime-input';
      el.type = 'text';
      el.maxLength = 24;
      el.autocomplete = 'off';
      el.spellcheck = false;
      el.style.position = 'fixed';
      el.style.zIndex = '9999';
      el.style.boxSizing = 'border-box';
      el.style.padding = '2px 8px';
      el.style.fontFamily = '"Microsoft YaHei", "PingFang SC", sans-serif';
      el.style.border = '2px solid #4aa3ff';
      el.style.borderRadius = '4px';
      el.style.outline = 'none';
      // 键盘事件必须自己吃掉：Unity WebGL 在 window 上注册了 keydown 并 preventDefault，
      // 冒泡上去以后 Backspace 的默认动作就被拦掉了 —— 表现就是「只能打字、删不掉字」。
      // 这里 stopPropagation + 自己删字符，最稳。
      el.addEventListener('keydown', function (ev) {
        ev.stopPropagation();

        if (ev.key === 'Backspace') {
          ev.preventDefault();
          var v = el.value, st = el.selectionStart, en = el.selectionEnd;
          if (st !== en) {
            el.value = v.slice(0, st) + v.slice(en);
            el.selectionStart = el.selectionEnd = st;
          } else if (st > 0) {
            el.value = v.slice(0, st - 1) + v.slice(st);
            el.selectionStart = el.selectionEnd = st - 1;
          }
          ev.stopImmediatePropagation();
        } else if (ev.key === 'Delete') {
          ev.preventDefault();
          el.value = '';
        } else if (ev.key === 'Enter') {
          ev.preventDefault();
          // 给 C# 一个「按了回车」的标记：那边当成「确定」处理
          Module.WBIME_ENTER = 1;
          el.blur();
        }
      }, true);

      el.addEventListener('keyup', function (ev) { ev.stopPropagation(); }, true);
      el.addEventListener('keypress', function (ev) { ev.stopPropagation(); }, true);
      document.body.appendChild(el);
    }

    var r = canvas.getBoundingClientRect();
    var k = sw > 0 ? r.width / sw : 1;      // 游戏像素 → 页面像素
    el.style.fontSize = Math.max(14, gh * k * 0.55) + 'px';
    el.style.left = (r.left + gx * k) + 'px';
    el.style.top = (r.top + gy * k) + 'px';
    el.style.width = (gw * k) + 'px';
    el.style.height = (gh * k) + 'px';
    el.value = UTF8ToString(initialPtr) || '';
    el.style.display = 'block';
    el.focus();
    el.select();
  },

  WBIME_Close: function () {
    var el = document.getElementById('wb-ime-input');
    if (el) {
      el.blur();
      el.style.display = 'none';
    }
  },

  WBIME_Active: function () {
    var el = document.getElementById('wb-ime-input');
    return (el && el.style.display === 'block') ? 1 : 0;
  },

  // 读一次「刚按过回车」，读完就清掉
  WBIME_ConsumeEnter: function () {
    var v = Module.WBIME_ENTER ? 1 : 0;
    Module.WBIME_ENTER = 0;
    return v;
  },

  WBIME_Value: function (buf, max) {
    var el = document.getElementById('wb-ime-input');
    var v = el ? el.value : '';
    stringToUTF8(v || '', buf, max);
  }
});
