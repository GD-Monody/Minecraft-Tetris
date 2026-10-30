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
        /// <summary>铜傀儡的眼睛（单独一层，贴 eyes 贴图叠在头上，不受光照按原样发光）。</summary>
        Renderer _eyeRenderer;
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
            // 模型是照贴图像素 1:1 建的（1 格 = 16 像素），所以不用再缩放 ——
            // 这样三种傀儡的相对身高就是原版的：雪 30.5 像素（1.9 格，连南瓜）、
            // 铁 43 像素（2.69 格）、铜 24 像素（1.5 格，连天线；原版碰撞箱只算到头顶 16 像素 = 1 格）。
            transform.localScale = Vector3.one;

            string tex = TextureName(Kind, Oxidation);
            int texW, texH;
            GolemPartDef[] parts = Parts(Kind, out texW, out texH);

            for (int i = 0; i < parts.Length; i++)
            {
                GolemPartDef def = parts[i];

                // 三层：关节（摆在哪、朝哪）→ 动画节点（走路时绕关节转）→ 网格。
                // 关节自己带一个初始旋转：原版雪傀儡的手臂是斜着往外伸的，那个 57° 得留在关节上，
                // 放到动画层会被走路摆动覆盖掉。
                var joint = new GameObject("joint_" + def.Part);
                joint.transform.SetParent(transform, false);
                joint.transform.localPosition = def.Pivot / GolemModel.PixelsPerBlock;
                joint.transform.localRotation = Quaternion.Euler(def.Rot);

                var anim = new GameObject("anim_" + def.Part);
                anim.transform.SetParent(joint.transform, false);

                var go = new GameObject("part_" + def.Part);
                go.transform.SetParent(anim.transform, false);

                // 一个部件可能由好几个立方体拼成（铁傀儡的头 + 鼻子、铜傀儡的头 + 天线），
                // BuildPart 已经把它们按 Center 摆好、合成一个 mesh
                go.AddComponent<MeshFilter>().sharedMesh = GolemModel.BuildPart(def, texW, texH);
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = MakeMaterial(tex);
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                _renderers.Add(mr);

                switch (def.Part)
                {
                    case GolemPart.Head: _head = anim.transform; break;
                    case GolemPart.Body: _bodyRenderer = mr; break;
                    case GolemPart.ArmLeft: _armL = anim.transform; break;
                    case GolemPart.ArmRight: _armR = anim.transform; break;
                    case GolemPart.LegLeft: _legL = anim.transform; break;
                    case GolemPart.LegRight: _legR = anim.transform; break;
                }

                // 雪傀儡的南瓜头是原版单独一层（SnowGolemHeadLayer），走的是方块模型不是实体贴图，
                // 所以在这里补一个雕刻南瓜：10×10×10 像素（原版 scale 0.625）、
                // 中心比雪头的中心高 1.5 像素，罩在头顶上。跟着头一起转。
                if (Kind == GolemKind.Snow && def.Part == GolemPart.Head)
                    AddPumpkinHat(anim.transform, def.Pivot);

                // 铜傀儡的橙色眼睛在 26.2 也是单独一层：LivingEntityEmissiveLayer 拿同一套模型
                // 再刷一遍，贴图换成 copper_golem_eyes*.png，只有眼睛那几个像素不透明。
                if (Kind == GolemKind.Copper && def.Part == GolemPart.Head)
                    AddCopperEyes(anim.transform, def, texW, texH);
            }

            PickTarget();
        }

        /// <summary>
        /// 雪傀儡头顶的南瓜。原版 SnowGolemHeadLayer 的做法：先移到 head 关节，
        /// 再 translate(0, -0.34375, 0)（相当于往上 5.5 像素）、scale(0.625)，
        /// 然后画一个整格方块。这里照抄成「10 像素见方、中心在头顶上方」。
        ///
        /// 没雕刻南瓜就退回普通南瓜；两个都找不到就干脆不戴，雪头本身已经有脸了，不至于空着。
        /// </summary>
        void AddPumpkinHat(Transform headAnim, Vector3 headPivot)
        {
            int pid = BlockCatalog.CarvedPumpkinId > 0 ? BlockCatalog.CarvedPumpkinId : BlockCatalog.PumpkinId;
            BlockDef pd = pid > 0 ? BlockCatalog.Of(pid) : null;
            if (pd == null)
                return;

            var go = new GameObject("pumpkin_hat");
            go.transform.SetParent(headAnim, false);
            // 头 8³ 的中心在关节上方 4 像素，南瓜中心再高 1.5 像素
            go.transform.localPosition = new Vector3(0f, 5.5f / GolemModel.PixelsPerBlock, 0f);
            go.transform.localScale = Vector3.one * (10f / GolemModel.PixelsPerBlock);
            go.AddComponent<MeshFilter>().sharedMesh = BlockMesh.Build(pd);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = PumpkinMaterial();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        /// <summary>
        /// 铜傀儡那双橙色的眼睛。26.2 的 CopperGolemRenderer 里挂的是
        /// `LivingEntityEmissiveLayer`：拿**同一套模型**再刷一遍，只是贴图换成
        /// `copper_golem_eyes*.png` —— 那张图 64×64 里只有 (11,12)~(16,13) 六个像素
        /// 不透明，正好落在头正面 UV 的中段，其余全透明被 clip 掉。
        ///
        /// 所以这里也是「同一个头网格 + 眼睛贴图」再叠一层，不上光照就自带发光感
        /// （同样是 Unlit，贴图本身是 255,168,81 的亮橙到 255,247,210 的亮黄）。
        /// 网格稍微放大 2%，让眼睛压在头皮外侧一点点，否则两层完全重合会 z-fighting，
        /// 眼睛会一块一块地闪。
        /// </summary>
        void AddCopperEyes(Transform headAnim, GolemPartDef headDef, int texW, int texH)
        {
            var go = new GameObject("eyes");
            go.transform.SetParent(headAnim, false);
            go.transform.localScale = Vector3.one * 1.02f;

            go.AddComponent<MeshFilter>().sharedMesh = GolemModel.BuildPart(headDef, texW, texH);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = MakeMaterial(EyeTextureName(Oxidation));
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            _eyeRenderer = mr;
        }

        /// <summary>眼睛贴图文件名，跟着氧化等级走（原版四个阶段各有一张）。</summary>
        static string EyeTextureName(int oxidation)
        {
            switch (oxidation)
            {
                case 1: return "copper_golem_eyes_exposed";
                case 2: return "copper_golem_eyes_weathered";
                case 3: return "copper_golem_eyes_oxidized";
                default: return "copper_golem_eyes";
            }
        }

        static Material _pumpkinMat;


        /// <summary>
        /// 南瓜用的材质：方块图集 + 和傀儡同一个不受光的 shader。
        ///
        /// 不能直接用 BlockMesh.GetMaterial() —— 那是受光的方块材质，傀儡站在场外空地，
        /// 正对镜头的那一面没有光，南瓜就成了一块黑饼（只有被照到的侧面还是橙的）。
        /// MC 渲染实体本来就不吃光照，南瓜头作为雪傀儡的「头饰」也照这个来。
        /// </summary>
        static Material PumpkinMaterial()
        {
            if (_pumpkinMat != null)
                return _pumpkinMat;

            Shader sh = Shader.Find("MinecraftTetris/Golem");
            if (sh == null)
                sh = Shader.Find("Unlit/Texture");
            if (sh == null)
                sh = Shader.Find("Sprites/Default");

            _pumpkinMat = new Material(sh);
            _pumpkinMat.name = "GolemPumpkin";

            Texture2D atlas = Resources.Load<Texture2D>("atlas");
            if (atlas != null)
            {
                atlas.filterMode = FilterMode.Point;
                _pumpkinMat.mainTexture = atlas;
            }
            return _pumpkinMat;
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

            // 眼睛是独立一张图，不能跟着身体换，否则整张脸都会被眼睛贴图盖住（非眼睛处全透明 → 脸消失）
            if (_eyeRenderer != null && _eyeRenderer.sharedMaterial != null)
            {
                Texture2D eyes = LoadTexture(EyeTextureName(Oxidation));
                if (eyes != null)
                {
                    eyes.filterMode = FilterMode.Point;
                    eyes.wrapMode = TextureWrapMode.Clamp;
                    _eyeRenderer.sharedMaterial.mainTexture = eyes;
                }
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
