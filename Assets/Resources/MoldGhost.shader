// 创意工坊建造台里的「半透明方块」。
//
// 普通图集材质（MinecraftTetris/Atlas）没有 _Color，做不出「淡一点」的效果；
// AtlasTransparent 的 alpha 又直接取自贴图（钻石块是 1.0，画出来还是实心的）。
// 所以单独写一个带染色的版本：贴图 × 颜色，alpha 由颜色给。
// 幽灵预览和非当前层的方块都用它 —— 一眼能分出「这一层的实心块」和「别的层」。
Shader "MinecraftTetris/MoldGhost"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1, 1, 1, 0.35)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;

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

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return tex2D(_MainTex, i.uv) * _Color;
            }
            ENDCG
        }
    }
    Fallback Off
}
