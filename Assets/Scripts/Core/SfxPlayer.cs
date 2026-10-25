using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>
    /// 音效：放下按材质选放置音，单行/层消除用获得经验，多行/层用升级。
    /// 文件在 Assets/Resources/Sfx/ 下。
    /// </summary>
    public class SfxPlayer : MonoBehaviour
    {
        AudioSource _source;
        AudioClip _placeStone;
        AudioClip _placeWood;
        AudioClip _placeGlass;
        AudioClip _pickupXp;
        AudioClip _levelup;

        void Awake()
        {
            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;

            _placeStone = Load("place_stone");
            _placeWood = Load("place_wood");
            _placeGlass = Load("place_glass");
            _pickupXp = Load("pickup_xp");
            _levelup = Load("levelup");
        }

        static AudioClip Load(string name)
        {
            return Resources.Load<AudioClip>("Sfx/" + name);
        }

        public void PlayPlace(BlockDef def)
        {
            AudioClip clip;
            switch (def.PlaceSfx)
            {
                case "place_wood":
                    clip = _placeWood;
                    break;
                case "place_glass":
                    clip = _placeGlass;
                    break;
                default:
                    clip = _placeStone;
                    break;
            }

            if (clip != null)
                _source.PlayOneShot(clip);
        }

        public void PlayClear(int lines)
        {
            AudioClip clip = lines >= 2 ? _levelup : _pickupXp;
            if (clip != null)
                _source.PlayOneShot(clip);
        }
    }
}
