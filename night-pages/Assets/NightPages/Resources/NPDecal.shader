// Something on a wall that isn't paint on top of it but in it: writing, a stain, handprints. The picture multiplies
// what's already there (dark marks darken the wallpaper under them; light parts leave it alone), and fades out at
// its edges so it has no border.
Shader "NightPages/Decal"
{
    Properties
    {
        _MainTex ("Marks", 2D) = "white" {}
        _Strength ("Strength", Range(0, 1)) = 1
        _Lift ("Lift (how much of the light background drops out)", Range(0, 1)) = 0.25
    }
    SubShader
    {
        Tags { "Queue" = "Geometry+10" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        ZWrite Off
        Offset -1, -1
        Blend DstColor Zero
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _Strength, _Lift;
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(appdata_base v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = TRANSFORM_TEX(v.texcoord, _MainTex); return o; }
            fixed4 frag(v2f i) : SV_Target
            {
                fixed3 c = tex2D(_MainTex, i.uv).rgb;
                c = saturate((c - _Lift) / max(0.01, 1.0 - _Lift));         // the light background goes to white (no change)
                float2 e = min(i.uv, 1.0 - i.uv);
                float edge = smoothstep(0.0, 0.16, min(e.x, e.y));
                return fixed4(lerp(fixed3(1, 1, 1), c, _Strength * edge), 1);
            }
            ENDCG
        }
    }
}
