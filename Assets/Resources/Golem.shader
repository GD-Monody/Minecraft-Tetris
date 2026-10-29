// 傀儡模型用的无光照贴图 shader。
//
// 为什么不用 Shader.Find("Unlit/Transparent Cutout")：内置 shader 只有在被某个材质引用时
// 才会打进 player，运行时用 Shader.Find 找会返回 null，于是掉到 Diffuse 那种受光材质上，
// 场外又没有灯，傀儡就成了一团暗色。这个 shader 放在 Resources 里，构建时一定会被打包。
//
// 和 MC 实体渲染一致：不做光照、alpha 直接裁剪（贴图里 alpha=0 的地方透明），背面剔除。
Shader "MinecraftTetris/Golem"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "Queue" = "AlphaTest" "RenderType" = "TransparentCutout" "IgnoreProjector" = "True" }

        Pass
        {
            Cull Back
            Lighting Off
            ZWrite On

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            sampler2D _MainTex;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv);
                // 实体贴图是"展开的纸盒"，空白处的 alpha 是 0，和 MC 一样直接裁掉
                clip(c.a - 0.5);
                return c;
            }
            ENDCG
        }
    }

    Fallback Off
}
