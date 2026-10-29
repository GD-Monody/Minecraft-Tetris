using System.Collections.Generic;
using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>
    /// 场外的傀儡：真模型 + 会自己走动，像个桌宠。
    ///
    /// 为什么放场外：傀儡要留在场地里就得处理它占不占格子、参不参与消行、会不会挡住落方块，
    /// 一个彩蛋不值得把消行逻辑再改一遍。这里只是多几个 GameObject，游戏规则一行没动。
    ///
    /// 模型：`GolemModel` 按 MC 实体贴图的官方 cube 展开切面，每个部件一个 GameObject，
    /// 头/身体/双臂/双腿分开挂，走路时绕关节摆。
    ///
    /// 铜傀儡会随时间氧化：贴图依次换成 exposed → weathered → oxidized，换完就停在最后一级。
    /// </summary>
    public class GolemPet : MonoBehaviour
    {
        /// <summary>走路速度（格 / 秒）。慢悠悠的，像原版那种散步。</summary>
        const float WalkSpeed = 0.85f;

        /// <summary>铜傀儡多久氧化一级（秒）。原版是按游戏日算的，这里压缩成半分钟量级才看得到。</summary>
        const float OxidizeInterval = 40f;

        /// <summary>走路时腿和手臂的摆动幅度（弧度）。</summary>
        const float Swing = 0.55f;

        /// <summary>走一步的摆动频率。</summary>
        const float SwingSpeed = 6.5f;

        public GolemKind Kind { get; private set; }
        /// <summary>铜傀儡的氧化等级 0~3，其它傀儡一直是 0。</summary>
        public int Oxidation { get; private set; }

        /// <summary>活动范围（世界坐标），由 Bootstrap 按场地尺寸给。</summary>
        public Vector3 RangeMin;
        public Vector3 RangeMax;

        Transform _head, _armL, _armR, _legL, _legR;
        Renderer _bodyRenderer;
        readonly List<Renderer> _renderers = new List<Renderer>();

        Vector3 _target;
        bool _walking;
        float _idleTimer;
        float _phase;
        float _yaw;
        float _oxidizeTimer;
        float _groundY;

        static readonly Dictionary<string, Texture2D> TexCache = new Dictionary<string, Texture2D>();
        static Material _matTemplate;

        /// <summary>
        /// 造一个傀儡：建部件、挂贴图、随机一个朝向。位置是脚底所在的地面点。
        /// </summary>
        public void Build(GolemKind kind, int oxidation, Vector3 groundPos, float yaw,
                          Vector3 rangeMin, Vector3 rangeMax)
        {
            Kind = kind;
            Oxidation = Mathf.Clamp(oxidation, 0, 3);
            RangeMin = rangeMin;
            RangeMax = rangeMax;
            transform.position = groundPos;
            _groundY = groundPos.y;
            _yaw = yaw;
            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            // 模型是照贴图像素做的（1 格 = 16 像素），和原版实体大小对一下：
            // 雪傀儡模型 20 像素高（原版 1.9 格 ≈ 30 像素）、铁傀儡 38 像素（原版 2.7 格 ≈ 43）、
            // 铜傀儡 21 像素（原版 0.98 格 ≈ 16，但太小了看不清，稍微放大一点）。
            float scale = Kind == GolemKind.Snow ? 1.69f : (Kind == GolemKind.Iron ? 1.13f : 0.98f);
            transform.localScale = Vector3.one * scale;

            string tex = TextureName(Kind, Oxidation);
            int texW, texH;
            GolemPartDef[] parts = Parts(Kind, out texW, out texH);

            for (int i = 0; i < parts.Length; i++)
            {
                GolemPartDef def = parts[i];
                GolemCube c = def.Cube;

                // 关节挂在根节点上，网格挂在关节下面、往里挪 center —— 这样转关节就是摆腿摆臂
                var joint = new GameObject("joint_" + def.Part);
                joint.transform.SetParent(transform, false);
                joint.transform.localPosition = c.Pivot / GolemModel.PixelsPerBlock;

                var go = new GameObject("part_" + def.Part);
                go.transform.SetParent(joint.transform, false);
                go.transform.localPosition = c.Center / GolemModel.PixelsPerBlock;

                // 雪傀儡的头在原版是南瓜头：它是单独的渲染层，实体贴图里没有，
                // 所以改用方块图集里的雕刻南瓜，按 8×8×8 缩到和头一样大。
                // 没雕刻南瓜就退回普通南瓜，两个都没有就还是用雪头（至少不会空着）。
                Mesh partMesh = GolemModel.BuildCube(c, texW, texH);
                Material partMat = MakeMaterial(tex);
                float partScale = 1f;
                if (Kind == GolemKind.Snow && def.Part == GolemPart.Head)
                {
                    int pid = BlockCatalog.CarvedPumpkinId > 0
                        ? BlockCatalog.CarvedPumpkinId
                        : BlockCatalog.PumpkinId;
                    BlockDef pd = pid > 0 ? BlockCatalog.Of(pid) : null;
                    if (pd != null)
                    {
                        partMesh = BlockMesh.Build(pd);                 // 1 格见方的方块
                        partMat = BlockMesh.GetMaterial(pd);
                        partScale = c.W / GolemModel.PixelsPerBlock;    // 8 像素 = 半格
                    }
                }

                // 雪傀儡的手臂是两根棕色树枝，实体贴图里没画这一块（老版本的 (32,0) 整片透明），
                // 借用身体的雪色贴图会白得看不出来，所以干脆用原木的方块网格压成细杆。
                if (Kind == GolemKind.Snow &&
                    (def.Part == GolemPart.ArmLeft || def.Part == GolemPart.ArmRight))
                {
                    BlockDef wd = BlockCatalog.OakLogId > 0 ? BlockCatalog.Of(BlockCatalog.OakLogId) : null;
                    if (wd != null)
                    {
                        partMesh = BlockMesh.Build(wd);
                        partMat = BlockMesh.GetMaterial(wd);
                        // 方块是 1 格见方，要缩到 2×6×2 像素 —— 非等比，所以这里单独设
                        go.transform.localScale = new Vector3(
                            c.W / GolemModel.PixelsPerBlock,
                            c.H / GolemModel.PixelsPerBlock,
                            c.D / GolemModel.PixelsPerBlock);
                        partScale = -1f;      // 标记：缩放已经单独设过，下面别再覆盖
                    }
                }

                if (partScale > 0f)
                    go.transform.localScale = Vector3.one * partScale;
                go.AddComponent<MeshFilter>().sharedMesh = partMesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = partMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                _renderers.Add(mr);

                switch (def.Part)
                {
                    case GolemPart.Head: _head = joint.transform; break;
                    case GolemPart.Body: _bodyRenderer = mr; break;
                    case GolemPart.ArmLeft: _armL = joint.transform; break;
                    case GolemPart.ArmRight: _armR = joint.transform; break;
                    case GolemPart.LegLeft: _legL = joint.transform; break;
                    case GolemPart.LegRight: _legR = joint.transform; break;
                }
            }

            if (Kind == GolemKind.Copper)
                AddLightningRod();

            PickTarget();
        }

        /// <summary>
        /// 铜傀儡头顶那根避雷针。原版是独立方块模型，实体贴图里没有，所以拿对应氧化阶段的
        /// 铜块网格压成一根细杆。不参与走路动画，就静静插在脑袋上。
        /// </summary>
        void AddLightningRod()
        {
            int id;
            switch (Oxidation)
            {
                case 1: id = BlockCatalog.ExposedCopperId; break;
                case 2: id = BlockCatalog.WeatheredCopperId; break;
                case 3: id = BlockCatalog.OxidizedCopperId; break;
                default: id = BlockCatalog.CopperBlockId; break;
            }

            BlockDef def = id > 0 ? BlockCatalog.Of(id) : null;
            if (def == null)
                return;

            var go = new GameObject("lightning_rod");
            go.transform.SetParent(transform, false);
            // 头高 6 像素、头顶在 16，针插进去一半，露出 5 像素
            go.transform.localPosition = new Vector3(0f, 17.5f / GolemModel.PixelsPerBlock, 0f);
            go.transform.localScale = new Vector3(1.5f, 5f, 1.5f) / GolemModel.PixelsPerBlock;
            go.AddComponent<MeshFilter>().sharedMesh = BlockMesh.Build(def);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = BlockMesh.GetMaterial(def);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        /// <summary>各种傀儡用哪几张部件定义。</summary>
        static GolemPartDef[] Parts(GolemKind kind, out int texW, out int texH)
        {
            if (kind == GolemKind.Snow)
            {
                texW = 64; texH = 64;
                return GolemModel.Snow();
            }
            if (kind == GolemKind.Iron)
            {
                texW = 128; texH = 128;
                return GolemModel.Iron();
            }
            texW = 64; texH = 64;
            return GolemModel.Copper();
        }

        /// <summary>贴图文件名。铜傀儡按氧化等级换。</summary>
        static string TextureName(GolemKind kind, int oxidation)
        {
            if (kind == GolemKind.Snow)
                return "snow_golem";
            if (kind == GolemKind.Iron)
                return "iron_golem";

            switch (oxidation)
            {
                case 1: return "copper_golem_exposed";
                case 2: return "copper_golem_weathered";
                case 3: return "copper_golem_oxidized";
                default: return "copper_golem";
            }
        }

        /// <summary>
        /// 每个傀儡一份材质（铜傀儡氧化时要单独换贴图，不能共用）。
        /// shader 用 Unlit/Transparent Cutout：实体贴图是带 alpha 的纸盒，
        /// 不用 lit 是因为场外没有光照，用 lit 会一边黑一边亮。
        /// </summary>
        static Material MakeMaterial(string texName)
        {
            Texture2D tex = LoadTexture(texName);

            if (_matTemplate == null)
            {
                // 用自己写的 shader，放在 Resources 里保证构建时会打包。
                // 内置 shader 没被材质引用的话会被剥离，运行时 Shader.Find 直接给 null。
                Shader sh = Shader.Find("MinecraftTetris/Golem");
                if (sh == null)
                    sh = Shader.Find("Unlit/Transparent Cutout");
                if (sh == null)
                    sh = Shader.Find("Sprites/Default");
                _matTemplate = new Material(sh);
                _matTemplate.name = "GolemMat";
            }

            var mat = new Material(_matTemplate);
            if (tex != null)
            {
                tex.filterMode = FilterMode.Point;
                tex.wrapMode = TextureWrapMode.Clamp;
                mat.mainTexture = tex;
            }
            return mat;
        }

        static Texture2D LoadTexture(string name)
        {
            Texture2D tex;
            if (TexCache.TryGetValue(name, out tex))
                return tex;

            tex = Resources.Load<Texture2D>("Golem/" + name);
            if (tex == null)
                Debug.LogWarning("[golem] 找不到贴图 Resources/Golem/" + name);
            TexCache[name] = tex;
            return tex;
        }

        void Update()
        {
            float dt = Time.deltaTime;

            TickOxidation(dt);

            if (_walking)
                WalkStep(dt);
            else
                IdleStep(dt);
        }

        /// <summary>铜傀儡氧化：隔一段时间升一级，到顶就停。换贴图即可，模型不动。</summary>
        void TickOxidation(float dt)
        {
            if (Kind != GolemKind.Copper || Oxidation >= 3)
                return;

            _oxidizeTimer += dt;
            if (_oxidizeTimer < OxidizeInterval)
                return;

            _oxidizeTimer = 0f;
            Oxidation++;

            Texture2D tex = LoadTexture(TextureName(Kind, Oxidation));
            if (tex == null || _bodyRenderer == null)
                return;

            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            for (int i = 0; i < _renderers.Count; i++)
            {
                if (_renderers[i] != null && _renderers[i].sharedMaterial != null)
                    _renderers[i].sharedMaterial.mainTexture = tex;
            }
        }

        /// <summary>走向目标点，走到就停下歇一会儿。</summary>
        void WalkStep(float dt)
        {
            Vector3 pos = transform.position;
            Vector3 to = _target - pos;
            to.y = 0f;

            if (to.magnitude < 0.06f)
            {
                _walking = false;
                _idleTimer = Random.Range(1.2f, 3.2f);
                _phase = 0f;
                ResetLimbs();
                return;
            }

            Vector3 dir = to.normalized;
            pos += dir * WalkSpeed * dt;
            // 活动范围是个矩形，撞到边就换目标
            pos.x = Mathf.Clamp(pos.x, RangeMin.x, RangeMax.x);
            pos.z = Mathf.Clamp(pos.z, RangeMin.z, RangeMax.z);
            pos.y = _groundY;
            transform.position = pos;

            // 朝向跟着走的方向转，转得慢一点，别像陀螺。
            // 模型的脸做在 -z 面上（MC 的 front），而 Unity 的 forward 是 +z，所以要补 180°，
            // 不然会倒着走 —— 脸朝后、背朝前。
            float want = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg + 180f;
            _yaw = Mathf.MoveTowardsAngle(_yaw, want, 240f * dt);
            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);

            _phase += dt * SwingSpeed;
            float s = Mathf.Sin(_phase) * Swing;
            SetLimb(_legL, s);
            SetLimb(_legR, -s);
            SetLimb(_armL, -s * 0.7f);
            SetLimb(_armR, s * 0.7f);
            if (_head != null)
                _head.localRotation = Quaternion.Euler(Mathf.Sin(_phase * 0.5f) * 3f, 0f, 0f);
        }

        /// <summary>站着不动：歇够了再挑个新目标。</summary>
        void IdleStep(float dt)
        {
            _idleTimer -= dt;
            if (_idleTimer > 0f)
                return;

            PickTarget();
        }

        void PickTarget()
        {
            _target = new Vector3(
                Random.Range(RangeMin.x, RangeMax.x),
                _groundY,
                Random.Range(RangeMin.z, RangeMax.z));
            _walking = true;
        }

        /// <summary>摆一个关节（绕 x 轴，正数往前抬）。</summary>
        static void SetLimb(Transform joint, float radians)
        {
            if (joint != null)
                joint.localRotation = Quaternion.Euler(radians * Mathf.Rad2Deg, 0f, 0f);
        }

        void ResetLimbs()
        {
            SetLimb(_legL, 0f);
            SetLimb(_legR, 0f);
            SetLimb(_armL, 0f);
            SetLimb(_armR, 0f);
            if (_head != null)
                _head.localRotation = Quaternion.identity;
        }

        /// <summary>把活动范围收一收（场地尺寸变了之后由 Bootstrap 调）。</summary>
        public void SetRange(Vector3 min, Vector3 max)
        {
            RangeMin = min;
            RangeMax = max;
        }
    }
}
