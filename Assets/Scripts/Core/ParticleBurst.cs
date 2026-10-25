using System.Collections.Generic;
using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>
    /// 方块被消除时炸开的小碎块，仿 MC 的方块破坏粒子：
    /// 每个碎块是一个缩小的方块 mesh（带原贴图），受重力、随机旋转，寿命到就回收。
    /// </summary>
    public class ParticleBurst : MonoBehaviour
    {
        class Particle
        {
            public GameObject Go;
            public Vector3 Vel;
            public Vector3 Spin;
            public float Life;
            public float MaxLife;
        }

        readonly List<Particle> _live = new List<Particle>();
        readonly Stack<GameObject> _pool = new Stack<GameObject>();
        GridView _view;
        Transform _root;

        public void Init(GridView view)
        {
            _view = view;
            var rootGo = new GameObject("Particles");
            rootGo.transform.SetParent(transform, false);
            _root = rootGo.transform;
        }

        /// <summary>在某个格子上炸出一小撮碎块。count 是每格的数量。</summary>
        public void EmitCell(int x, int y, int z, int blockId, int count = 3)
        {
            if (_view == null || blockId <= 0)
                return;

            Mesh mesh = _view.GetMesh(blockId);
            Material mat = _view.GetMaterial(blockId);

            for (int i = 0; i < count; i++)
            {
                GameObject go = Rent();
                float s = Random.Range(0.10f, 0.22f);
                go.transform.position = new Vector3(
                    x + Random.Range(0.1f, 0.9f),
                    y + Random.Range(0.1f, 0.9f),
                    z + Random.Range(-0.4f, 0.4f));
                go.transform.rotation = Random.rotation;
                go.transform.localScale = new Vector3(s, s, s);
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                go.GetComponent<MeshRenderer>().sharedMaterial = mat;
                go.SetActive(true);

                var p = new Particle
                {
                    Go = go,
                    Vel = new Vector3(
                        Random.Range(-2.8f, 2.8f),
                        Random.Range(3.5f, 7.5f),
                        Random.Range(-1.2f, 1.2f)),
                    Spin = new Vector3(
                        Random.Range(-500f, 500f),
                        Random.Range(-500f, 500f),
                        Random.Range(-500f, 500f)),
                    MaxLife = Random.Range(0.6f, 1.1f),
                };
                p.Life = p.MaxLife;
                _live.Add(p);
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                Particle p = _live[i];
                p.Life -= dt;
                if (p.Life <= 0f)
                {
                    p.Go.SetActive(false);
                    _pool.Push(p.Go);
                    _live.RemoveAt(i);
                    continue;
                }

                p.Vel.y -= 18f * dt;
                p.Go.transform.position += p.Vel * dt;
                p.Go.transform.Rotate(p.Spin * dt, Space.World);
            }
        }

        GameObject Rent()
        {
            if (_pool.Count > 0)
                return _pool.Pop();

            var go = new GameObject("Particle");
            go.transform.SetParent(_root, false);
            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>();
            return go;
        }

        /// <summary>重开一局时把场上剩下的碎块收掉。</summary>
        public void Clear()
        {
            for (int i = 0; i < _live.Count; i++)
            {
                _live[i].Go.SetActive(false);
                _pool.Push(_live[i].Go);
            }
            _live.Clear();
        }
    }
}
