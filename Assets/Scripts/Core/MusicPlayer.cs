using System.Collections.Generic;
using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>
    /// 背景音乐。曲库是 MC 原版的常速曲（从本机资源里提的，见 tools/extract_music.py），
    /// 随机播放，但不完全随机：
    ///
    /// - 按「洗牌袋」走，一轮里每首各放一次，听完一轮才重新洗，不会连着放同一首；
    /// - 两首之间留一段 10~24 秒的静默——原版就是这个节奏，一直不停反而吵；
    /// - 每首开头淡入、结尾淡出。曲子本身结尾是自然收的，但玩家中途按 Esc 暂停、
    ///   或者从游戏退回标题（换曲库）时，硬切会有一下爆音，所以统一走淡出。
    ///
    /// 标题界面和游戏内用两套曲库（game_* / menu_*），切界面时换库并重新洗牌。
    /// 音频文件按 Streaming 导入（见 Editor/AudioImportSettings.cs），不会一次解码十几首歌。
    /// </summary>
    public class MusicPlayer : MonoBehaviour
    {
        /// <summary>总音量 0~1。0 = 静音（设置里调到 0%）。</summary>
        public float Volume = 0.5f;

        public float FadeIn = 2.5f;
        public float FadeOut = 4f;

        /// <summary>两首之间的静默时长（秒）。</summary>
        public float GapMin = 10f;
        public float GapMax = 24f;

        // 资源名都在 Assets/Resources/Music/ 下，一律 ASCII（中文路径打包会出编码问题）
        public static readonly string[] GameTrackPaths =
        {
            "Music/game_sweden",
            "Music/game_minecraft",
            "Music/game_calm1",
            "Music/game_calm2",
            "Music/game_calm3",
            "Music/game_hal1",
            "Music/game_hal2",
            "Music/game_hal3",
            "Music/game_haggstrom",
            "Music/game_piano1",
            "Music/game_piano2",
            "Music/game_piano3",
            "Music/game_nuance1",
            "Music/game_nuance2",
        };

        public static readonly string[] MenuTrackPaths =
        {
            "Music/menu_menu1",
            "Music/menu_moog_city_2",
        };

        AudioSource _src;
        readonly List<AudioClip> _game = new List<AudioClip>();
        readonly List<AudioClip> _menu = new List<AudioClip>();
        readonly List<AudioClip> _deck = new List<AudioClip>();

        int _deckPos;
        bool _menuMode;
        float _gapTimer = 1.5f;
        bool _paused;
        bool _stopping;
        float _stopTimer;
        AudioClip _lastPlayed;

        /// <summary>当前这首还剩多久放完（调试 / 自检用）。</summary>
        public float Remaining
        {
            get
            {
                if (_src == null || _src.clip == null || !_src.isPlaying)
                    return 0f;
                return Mathf.Max(0f, _src.clip.length - _src.time);
            }
        }

        public int GameTrackCount { get { return _game.Count; } }
        public int MenuTrackCount { get { return _menu.Count; } }

        void Awake()
        {
            _src = gameObject.AddComponent<AudioSource>();
            _src.playOnAwake = false;
            _src.loop = false;
            _src.spatialBlend = 0f;      // 纯 2D，不随相机位置变化
            _src.volume = 0f;

            LoadInto(_game, GameTrackPaths);
            LoadInto(_menu, MenuTrackPaths);

            _deck.AddRange(_game);
            Shuffle(_deck);
            Debug.Log("[music] 曲库：游戏 " + _game.Count + " 首 / 菜单 " + _menu.Count + " 首");
        }

        static void LoadInto(List<AudioClip> list, string[] paths)
        {
            for (int i = 0; i < paths.Length; i++)
            {
                AudioClip clip = Resources.Load<AudioClip>(paths[i]);
                if (clip != null)
                    list.Add(clip);
                else
                    Debug.LogWarning("[music] 缺音频: " + paths[i]);
            }
        }

        /// <summary>标题界面用菜单曲库，进游戏换回游戏曲库。切换时淡出当前这首。</summary>
        public void SetMenuMode(bool menu)
        {
            if (_menuMode == menu && _deck.Count > 0)
                return;

            _menuMode = menu;
            _deck.Clear();
            _deck.AddRange(menu ? _menu : _game);
            Shuffle(_deck);
            _deckPos = 0;
            _lastPlayed = null;

            // 正在放的话先淡出，别硬切
            if (_src != null && _src.isPlaying)
            {
                _stopping = true;
                _stopTimer = Mathf.Min(1.6f, FadeOut);
            }
            else
            {
                _gapTimer = 1.0f;
            }
        }

        public void SetPaused(bool paused)
        {
            if (_paused == paused)
                return;
            _paused = paused;
            if (_src == null || _src.clip == null)
                return;

            if (paused)
                _src.Pause();
            else
                _src.UnPause();
        }

        /// <summary>设置里改了音量（0 = 关）。</summary>
        public void SetVolume(float v)
        {
            Volume = Mathf.Clamp01(v);
            if (Volume <= 0.01f && _src != null)
            {
                // 静音就直接停掉：不然解压/流式读取还在后台空转
                _src.Stop();
                _stopping = false;
                _gapTimer = 0.6f;
                _src.volume = 0f;
            }
        }

        void Update()
        {
            if (_src == null)
                return;

            float dt = Time.unscaledDeltaTime;   // 暂停时 timeScale = 0，用 scaled 会僵住

            // 淡出进行中：先把声音收到 0 再真正 Stop
            if (_stopping)
            {
                _stopTimer -= dt;
                float k = Mathf.Clamp01(_stopTimer / Mathf.Max(0.01f, Mathf.Min(1.6f, FadeOut)));
                _src.volume = Volume * k;
                if (_stopTimer <= 0f || !_src.isPlaying)
                {
                    _src.Stop();
                    _stopping = false;
                    _src.volume = 0f;
                    _gapTimer = 1.6f;
                }
                return;
            }

            if (_paused || Volume <= 0.01f)
                return;

            if (!_src.isPlaying)
            {
                _gapTimer -= dt;
                if (_gapTimer <= 0f)
                    PlayNext();
                return;
            }

            // 音量包络：开头淡入 + 结尾淡出
            float t = _src.time;
            float env = 1f;
            if (FadeIn > 0.01f)
                env = Mathf.Min(env, Mathf.Clamp01(t / FadeIn));
            float remain = _src.clip != null ? _src.clip.length - t : 99f;
            if (FadeOut > 0.01f)
                env = Mathf.Min(env, Mathf.Clamp01(remain / FadeOut));

            _src.volume = Volume * Mathf.Clamp01(env);
        }

        void PlayNext()
        {
            if (_deckPos >= _deck.Count)
            {
                // 一轮放完：重洗。第一首别和刚放的那首重复
                Shuffle(_deck);
                _deckPos = 0;
                if (_deck.Count > 1 && _deck[0] == _lastPlayed)
                {
                    AudioClip tmp = _deck[0];
                    _deck[0] = _deck[_deck.Count - 1];
                    _deck[_deck.Count - 1] = tmp;
                }
            }

            while (_deckPos < _deck.Count)
            {
                AudioClip clip = _deck[_deckPos];
                _deckPos++;
                if (clip == null)
                    continue;

                _lastPlayed = clip;
                _src.clip = clip;
                _src.volume = 0f;
                _src.Play();
                _gapTimer = Random.Range(GapMin, GapMax);
                return;
            }
        }

        static void Shuffle(List<AudioClip> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                AudioClip tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }
    }
}
