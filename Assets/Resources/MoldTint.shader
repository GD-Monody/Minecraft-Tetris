// 建造盒的「兜底面」用的透明色块 shader。
//
// 为什么不用 GridCoreMat：那个材质用的是内置 Unlit/Color，面里没有 _MainTex、也不做混合，
// alpha 会被直接忽略 —— 拿它铺半透明面会变成一整块实心黑（踩过）。
// 这里用标准的 alpha 混合，只吃一个 _Color，不带贴图。
Shader "MinecraftTetris/MoldTint"
{
    Properties
    {
        _Color ("Color", Color) = (0.30, 0.32, 0.36, 0.04)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return _Color;
            }
            ENDCG
        }
    }
    Fallback Off
}
