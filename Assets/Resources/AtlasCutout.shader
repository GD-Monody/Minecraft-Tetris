Shader "MinecraftTetris/AtlasCutout"
{
    // 和 MinecraftAtlas 一样是图集方块，但贴图里有整块透明的像素（铜格栅的网格、
    // 玻璃中间的空洞），透明的地方要真的透过去。
    // 走 alpha test（原版 MC 里这类方块就是 cutout_mipped），不走半透明混合：
    // 半透明不写深度，一堆镂空方块叠在一起会互相穿透、排序还会抖。
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
    }
    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" }
        Cull Off
        ZWrite On
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _Cutoff;

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
                fixed4 c = tex2D(_MainTex, i.uv);
                if (c.a < _Cutoff)
                    discard;
                return c;
            }
            ENDCG
        }
    }
}
