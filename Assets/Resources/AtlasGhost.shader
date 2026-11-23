Shader "MinecraftTetris/AtlasGhost"
{
    // 「只亮 z 面」时其它层用的「影子」材质。
    //
    // 第一版是半透明的原图集（alpha 0.42），实际玩起来整个场景糊成一片：
    // 三层方块叠在一起，每层都在透出下一层的花纹，根本看不出哪一层是哪一层。
    // 现在改成**统一的淡色 + 边缘轮廓**：面几乎是空的，只有靠近轮廓的一圈才亮起来，
    // 于是「这一格有东西，但它不在你正在看的层」这件事一眼就能读出来，又不会挡视线。
    Properties
    {
        _Color ("Tint", Color) = (0.62, 0.76, 0.95, 0.06)
        _Rim ("Rim", Range(0, 1)) = 0.85
        _RimPower ("Rim Power", Range(0.5, 6)) = 2.2
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" }
        Cull Back
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _Rim;
            float _RimPower;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldNormal : TEXCOORD0;
                float3 viewDir : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.viewDir = WorldSpaceViewDir(v.vertex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 n = normalize(i.worldNormal);
                float3 v = normalize(i.viewDir);

                // 正对镜头的面几乎不留色，只留一点点；越靠边缘越亮 —— 看上去就是一条轮廓线
                float rim = pow(1.0 - saturate(dot(n, v)), _RimPower);
                float a = _Color.a + rim * _Rim * 0.55;

                fixed4 col = _Color;
                col.a = saturate(a);
                clip(col.a - 0.01);
                return col;
            }
            ENDCG
        }
    }
}
