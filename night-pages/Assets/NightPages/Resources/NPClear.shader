// See-through and lit: glass, water, a wet shine, dust, a cobweb, grime that fades as it's cleaned.
// (Unity's Standard shader can be switched to "Fade" at runtime, but a build strips that variant when no material in
// the scenes uses it, and the thing then draws solid. This one is always see-through.)
Shader "NightPages/Clear"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _MainTex ("Albedo (alpha is how solid)", 2D) = "white" {}
        _Glossiness ("Smoothness", Range(0, 1)) = 0.5
        _Metallic ("Metallic", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        LOD 200
        ZWrite Off
        CGPROGRAM
        #pragma surface surf Standard alpha:fade
        #pragma target 3.0
        sampler2D _MainTex;
        fixed4 _Color;
        half _Glossiness, _Metallic;
        struct Input { float2 uv_MainTex; };
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            o.Albedo = c.rgb;
            o.Alpha = c.a;
            o.Smoothness = _Glossiness;
            o.Metallic = _Metallic;
        }
        ENDCG
    }
    FallBack "Transparent/VertexLit"
}
