using System.Collections.Generic;
using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>
    /// 音效。放置音完全照 MC 原版来：
    ///
    /// - 原版是 **按材质分组** 的（SoundType），同一种材质的所有方块共用一个组，
    ///   比如所有木板都响 dig/wood*，所有石头系都响 dig/stone*，羊毛是 dig/cloth*。
    ///   分组数据在 BlockSfxData（由 tools/build_place_sfx.py 从 MC 的 sounds.json 生成）。
    /// - 每个组有好几个录音变体（3~8 个），原版是随机挑一个播，这里也一样；
    ///   再加一点音高抖动，连续放同一块就不会像复读机。
    /// - 个别组在 sounds.json 里自带音量（竹木 0.9、泥 0.5、压实泥 0.3…），照原样乘上。
    ///
    /// 文件在 Assets/Resources/Sfx/（界面音、消除音）和 Sfx/Place/（各组的放置音）下。
    /// </summary>
    public class SfxPlayer : MonoBehaviour
    {
        /// <summary>设置面板里的音效开关，关掉之后一律不播。</summary>
        public bool Enabled = true;
        public float Volume = 0.8f;

        /// <summary>放置音的音高抖动幅度（±）。MC 每次放置的 pitch 都有细微随机。</summary>
        public float PitchJitter = 0.06f;

        // 几个 AudioSource 轮着用：PlayOneShot 不会互相打断，但 pitch 是挂在 AudioSource 上的，
        // 共用一个源的话改 pitch 会把正在响的那一下也带歪，所以轮转着来。
        const int VoiceCount = 6;

        AudioSource[] _voices;
        int _nextVoice;

        AudioClip _fallbackPlace;      // 组里没素材时兜底用
        AudioClip[][] _placeClips;     // 按组下标
        float[] _placeVolume;
        Dictionary<string, int> _groupIndex;

        AudioClip _pickupXp;
        AudioClip _levelup;
        AudioClip _fizz;

        // TNT：引信是 random.fuse，爆炸是 random.explode1~4 随机一个
        AudioClip _fuse;
        AudioClip[] _explode;

        // 玻璃被压碎：MC 的 block.glass.break
        AudioClip _glassBreak;

        void Awake()
        {
            _voices = new AudioSource[VoiceCount];
            for (int i = 0; i < VoiceCount; i++)
            {
                AudioSource source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                _voices[i] = source;
            }

            _fallbackPlace = Load("Sfx/place_stone");
            _pickupXp = Load("Sfx/pickup_xp");
            _levelup = Load("Sfx/levelup");
            _fizz = Load("Sfx/fizz");

            _fuse = Load("Sfx/tnt_fuse");
            _glassBreak = Load("Sfx/break_glass");
            _explode = new[]
            {
                Load("Sfx/tnt_explode1"),
                Load("Sfx/tnt_explode2"),
                Load("Sfx/tnt_explode3"),
                Load("Sfx/tnt_explode4"),
            };

            LoadPlaceGroups();
        }

        void LoadPlaceGroups()
        {
            int count = BlockSfxData.GroupCount;
            _placeClips = new AudioClip[count][];
            _placeVolume = new float[count];
            _groupIndex = new Dictionary<string, int>(count);

            int total = 0;
            for (int g = 0; g < count; g++)
            {
                string group = BlockSfxData.Groups[g];
                _groupIndex[group] = g;
                _placeVolume[g] = BlockSfxData.GroupVolume[g];

                string[] names = BlockSfxData.GroupClips[g];
                var clips = new List<AudioClip>(names.Length);
                for (int i = 0; i < names.Length; i++)
                {
                    AudioClip clip = Load("Sfx/Place/" + names[i]);
                    if (clip != null)
                        clips.Add(clip);
                }

                if (clips.Count == 0)
                    Debug.LogWarning("[sfx] 放置音组没有素材: " + group);

                _placeClips[g] = clips.ToArray();
                total += clips.Count;
            }

            Debug.Log("[sfx] 放置音 " + count + " 组 / " + total + " 个 clip");
        }

        static AudioClip Load(string path)
        {
            return Resources.Load<AudioClip>(path);
        }

        void Play(AudioClip clip, float volume, float pitch)
        {
            if (clip == null || !Enabled || _voices == null)
                return;

            AudioSource source = _voices[_nextVoice];
            _nextVoice = (_nextVoice + 1) % _voices.Length;
            source.pitch = pitch;
            source.PlayOneShot(clip, Mathf.Clamp01(Volume * volume));
        }

        void Play(AudioClip clip)
        {
            Play(clip, 1f, 1f);
        }

        /// <summary>按组下标播一次放置音。</summary>
        void PlayPlaceGroup(int group)
        {
            if (_placeClips == null)
                return;

            if (group >= 0 && group < _placeClips.Length)
            {
                AudioClip[] clips = _placeClips[group];
                if (clips != null && clips.Length > 0)
                {
                    float pitch = 1f + Random.Range(-PitchJitter, PitchJitter);
                    Play(clips[Random.Range(0, clips.Length)], _placeVolume[group], pitch);
                    return;
                }
            }

            // 素材缺失时退回老的石头音，别让游戏变哑巴
            if (_fallbackPlace != null)
                Play(_fallbackPlace, 1f, 1f + Random.Range(-PitchJitter, PitchJitter));
        }

        /// <summary>
        /// 放下一个方块：音高跟着**落点高度**走，不跟顺序、也不跟材质。
        ///
        /// 试过的两种都不行：
        /// · 按材质 —— 材质和音高没有对应关系，连放时音高乱跳；
        /// · 按放置顺序走音阶 —— 音高一路往上爬，像在弹音阶练习，而且和屏幕上的动作无关。
        ///
        /// 现在这条有物理依据：掉到底部是低音（沉稳），堆到高处是高音（紧张）。
        /// 连放时音高随落点起伏 —— 平铺是低音、往高处堆才升调，听起来是"场地在长高"，
        /// 而不是"有人在数音阶"。
        /// </summary>
        public void PlayPlace(BlockDef def, int cellY, int gridHeight)
        {
            if (_placeClips == null || _placeClips.Length == 0)
            {
                if (_fallbackPlace != null)
                    Play(_fallbackPlace, 1f, 1f);
                return;
            }

            // 只用场地下半部分的实际高度：方块绝大多数落在这里，
            // 按整个 20 格映射的话所有音都会挤在最底下两级，听不出变化。
            const float Span = 10f;
            float t = Mathf.Clamp01(cellY / Span);
            int step = Mathf.Clamp(Mathf.RoundToInt(t * (_placeClips.Length - 1)),
                                   0, _placeClips.Length - 1);

            AudioClip[] clips = _placeClips[step];
            if (clips != null && clips.Length > 0)
            {
                // 音高不抖（音阶要准），变体差异在合成阶段已经做进音色里
                Play(clips[Random.Range(0, clips.Length)], _placeVolume[step], 1f);
                return;
            }

            if (_fallbackPlace != null)
                Play(_fallbackPlace, 1f, 1f);
        }

        public void PlayClear(int lines)
        {
            AudioClip clip = lines >= 2 ? _levelup : _pickupXp;
            Play(clip);
        }

        /// <summary>水浇到岩浆上结晶时的嘶声（MC 的 random.fizz）。素材缺的时候退回石头音。</summary>
        public void PlayFizz()
        {
            if (_fizz != null)
                Play(_fizz);
            else
                PlayPlaceGroup(0);
        }

        /// <summary>玻璃被压碎。素材缺的时候退回石头音，别让这一下没声音。</summary>
        public void PlayGlassBreak()
        {
            if (_glassBreak != null)
                Play(_glassBreak, 0.85f, 1f + Random.Range(-0.06f, 0.06f));
            else
                PlayPlaceGroup(0);
        }

        /// <summary>TNT 被点着：引信「嘶——」。</summary>
        public void PlayFuse()
        {
            if (_fuse != null)
                Play(_fuse, 0.9f, 1f + Random.Range(-0.03f, 0.03f));
        }

        /// <summary>爆炸。原版 random.explode1~4 随机一个，音高也抖一点。</summary>
        public void PlayExplode()
        {
            if (_explode == null || _explode.Length == 0)
                return;

            AudioClip clip = _explode[Random.Range(0, _explode.Length)];
            if (clip != null)
                Play(clip, 1f, 1f + Random.Range(-0.05f, 0.05f));
        }
    }
}
