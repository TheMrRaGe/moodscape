// The dream maze's walls. Wallpaper, and over it the rot: black, wet, veined, like the drowned thing's skin, and
// alive. It seeps down from the ceiling and spreads out from blotches, runs in slow drips, glistens, and pulses
// faintly. How far it has got is one number for the whole maze (_NPRot, 0 = clean paper, 1 = almost nothing left),
// which the game raises as the night runs out, so you can watch it creep.
Shader "NightPages/Rot"
{
    Properties
    {
        _MainTex ("Wallpaper", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 300

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0

        sampler2D _MainTex;
        fixed4 _Color;
        float _NPRot, _NPFloorY;

        struct Input
        {
            float2 uv_MainTex;
            float3 worldPos;
        };

        float hash2(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

        float vnoise(float2 p)
        {
            float2 i = floor(p), f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(hash2(i), hash2(i + float2(1, 0)), f.x), lerp(hash2(i + float2(0, 1)), hash2(i + float2(1, 1)), f.x), f.y);
        }

        float fbm(float2 p)
        {
            float s = 0.0, a = 0.5;
            for (int o = 0; o < 4; o++) { s += vnoise(p) * a; p = p * 2.03 + 11.3; a *= 0.5; }
            return s / 0.9375;
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 paper = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            float t = _Time.y;
            // Along the wall and up it (the maze's walls all run along x or along z).
            float2 w = float2(IN.worldPos.x + IN.worldPos.z, IN.worldPos.y - _NPFloorY);
            float height = saturate(w.y / 3.0);

            // Where it is: blotches that drift very slowly, long runs sliding down the wall, and more of it higher up.
            float blot = fbm(w * 0.6 + float2(0.0, t * 0.015));
            float runs = vnoise(float2(w.x * 3.2, w.y * 0.3 + t * 0.11)) * 0.6 + vnoise(float2(w.x * 9.0, w.y * 0.8 + t * 0.2)) * 0.4;
            float field = blot * 0.55 + runs * 0.3 + height * 0.3;
            float pulse = sin(t * 0.7 + blot * 9.0) * 0.012;
            float edge = 1.08 - _NPRot * 1.2 + pulse;
            float m = smoothstep(edge, edge + 0.05, field);
            float halo = smoothstep(edge - 0.14, edge, field);          // the paper darkens and wets ahead of it

            // The ooze itself: black, with veins that show and fade, and a sheen that crawls.
            float vein = abs(sin(fbm(w * 2.4 + float2(t * 0.03, -t * 0.05)) * 22.0));
            vein = smoothstep(0.93, 1.0, 1.0 - vein) * (0.5 + 0.5 * sin(t * 1.3 + blot * 14.0));
            float3 ooze = float3(0.014, 0.017, 0.02) + float3(0.0, 0.07, 0.062) * vein;
            float sheen = vnoise(float2(w.x * 6.0, w.y * 1.5 + t * 0.35));

            float3 col = paper.rgb * lerp(1.0, 0.38, halo);
            col = lerp(col, ooze, m);
            // A thick lip where it's advancing.
            float lip = smoothstep(edge, edge + 0.02, field) * (1.0 - smoothstep(edge + 0.02, edge + 0.07, field));
            o.Albedo = col;
            o.Emission = (float3(0.0, 0.04, 0.036) * vein + float3(0.006, 0.008, 0.01) * sheen * sheen) * m;     // it glistens even in the dark
            o.Smoothness = lerp(lerp(0.06, 0.4, halo), 0.8 + 0.18 * sheen, m);
            o.Normal = normalize(float3((runs - 0.5) * 0.5 * m, lip * 0.9, 1.0));
            o.Metallic = 0;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
