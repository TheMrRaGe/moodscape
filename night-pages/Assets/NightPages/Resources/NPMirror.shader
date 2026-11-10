// The hall mirror: shows the reflection camera's picture, sampled where it lands on the screen.
Shader "NightPages/Mirror"
{
    Properties
    {
        _MainTex ("Reflection", 2D) = "black" {}
        _Tint ("Tint", Color) = (0.8, 0.82, 0.86, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            fixed4 _Tint;
            struct v2f { float4 pos : SV_POSITION; float4 sp : TEXCOORD0; };
            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.sp = ComputeNonStereoScreenPos(o.pos);
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                return tex2Dproj(_MainTex, UNITY_PROJ_COORD(i.sp)) * _Tint;
            }
            ENDCG
        }
    }
}
