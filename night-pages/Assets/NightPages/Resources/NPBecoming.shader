// The monster while it becomes something else. One mesh carries the in-between shape; this shader makes it move
// like flesh between the shapes the CPU worked out:
//  - every vertex slides toward where it lies on the next shape (_Sub, 0..1), so the body flows instead of stepping;
//  - the surface writhes (slow travelling swells and a pulse that climbs the body), strongest halfway through;
//  - the new skin spreads outward from the core with the growth, a stain with a wet raw edge, instead of the
//    old material switching all at once. Both skins keep their colour and bump textures the whole way.
Shader "NightPages/Becoming"
{
    Properties
    {
        _MainTex ("Old skin", 2D) = "white" {}
        _BumpMap ("Old bumps", 2D) = "bump" {}
        _Color ("Old colour", Color) = (1, 1, 1, 1)
        _Glossiness ("Old gloss", Range(0, 1)) = 0.2
        _BumpScale ("Old bump strength", Float) = 1
        _MainTex2 ("New skin", 2D) = "white" {}
        _BumpMap2 ("New bumps", 2D) = "bump" {}
        _Color2 ("New colour", Color) = (1, 1, 1, 1)
        _Glossiness2 ("New gloss", Range(0, 1)) = 0.2
        _BumpScale2 ("New bump strength", Float) = 1
        _Blend ("How far the new skin has spread", Range(0, 1)) = 0
        _Sub ("Between this shape and the next", Range(0, 1)) = 0
        _Writhe ("Writhing", Range(0, 1)) = 0
        _Core ("Where it grows from (object space)", Vector) = (0, 1, 0, 0)
        _Radius ("How far it reaches", Float) = 1.5
        _Seed ("Seed", Float) = 0
        _Dissolve ("Dissolved away (1 = gone)", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 300

        CGPROGRAM
        #pragma surface surf Standard vertex:vert fullforwardshadows addshadow
        #pragma target 3.0

        sampler2D _MainTex, _BumpMap, _MainTex2, _BumpMap2;
        float4 _MainTex_ST, _MainTex2_ST;
        fixed4 _Color, _Color2;
        half _Glossiness, _Glossiness2, _BumpScale, _BumpScale2;
        float _Blend, _Sub, _Writhe, _Radius, _Seed, _Dissolve;
        float4 _Core;

        struct Input
        {
            float2 texA;        // (not "uv...": the surface shader compiler takes those names over and fills them itself)
            float2 texB;
            float3 opos;
        };

        float hash3(float3 p)
        {
            return frac(sin(dot(p, float3(12.9898, 78.233, 37.719))) * 43758.5453);
        }

        float vnoise(float3 p)
        {
            float3 i = floor(p), f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            float a = lerp(hash3(i), hash3(i + float3(1, 0, 0)), f.x);
            float b = lerp(hash3(i + float3(0, 1, 0)), hash3(i + float3(1, 1, 0)), f.x);
            float c = lerp(hash3(i + float3(0, 0, 1)), hash3(i + float3(1, 0, 1)), f.x);
            float d = lerp(hash3(i + float3(0, 1, 1)), hash3(i + float3(1, 1, 1)), f.x);
            return lerp(lerp(a, b, f.y), lerp(c, d, f.y), f.z);
        }

        // Slow swells that travel over the body, and a pulse that climbs it, like something moving under the skin.
        float writhe(float3 p, float t)
        {
            float s = sin(p.x * 7.1 + t * 1.9 + _Seed) * sin(p.y * 5.3 - t * 1.4) * sin(p.z * 6.7 + t * 2.2);
            s += 0.5 * sin(p.x * 13.0 - t * 2.6 + p.z * 4.0) * sin(p.y * 11.0 + t * 1.7);
            s += 0.7 * pow(saturate(sin(p.y * 4.0 - t * 3.2 + p.x * 1.5) * 0.5 + 0.5), 4.0);
            return s;
        }

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            float3 p = lerp(v.vertex.xyz, v.texcoord2.xyz, _Sub);
            p += v.normal * writhe(p, _Time.y) * _Writhe * 0.028;
            v.vertex.xyz = p;
            o.texA = v.texcoord.xy * _MainTex_ST.xy + _MainTex_ST.zw;
            o.texB = v.texcoord.xy * _MainTex2_ST.xy + _MainTex2_ST.zw;
            o.opos = p;
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            // Coming and going: it breaks up in fine patches over the real body underneath, so the start and the end of
            // a change are never a cut from one mesh to another.
            clip(vnoise(IN.opos * 34.0 + _Seed) * 0.98 + 0.01 - _Dissolve);
            // The new skin spreads out from the core with the growth, unevenly.
            float dist = length(IN.opos - _Core.xyz);
            float n = vnoise(IN.opos * 5.0 + _Seed) * 0.65 + vnoise(IN.opos * 14.0) * 0.35;
            float front = _Blend * (_Radius + 0.5) - dist + (n - 0.5) * 0.45;
            float m = saturate(front / 0.14 + 0.5) * saturate(_Blend * 10.0);
            m = max(m, saturate(_Blend * 10.0 - 9.0));

            fixed4 a = tex2D(_MainTex, IN.texA) * _Color;
            fixed4 b = tex2D(_MainTex2, IN.texB) * _Color2;
            float3 na = UnpackNormal(tex2D(_BumpMap, IN.texA)); na.xy *= _BumpScale;
            float3 nb = UnpackNormal(tex2D(_BumpMap2, IN.texB)); nb.xy *= _BumpScale2;

            // Where the two meet, it's raw: darker, redder, wet.
            float edge = 1.0 - abs(m * 2.0 - 1.0);
            edge *= edge;
            float3 col = lerp(a.rgb, b.rgb, m);
            col = lerp(col, col * float3(0.55, 0.22, 0.2) + float3(0.04, 0.0, 0.0), edge * 0.75);

            o.Albedo = col;
            o.Normal = normalize(lerp(normalize(na), normalize(nb), m));
            o.Smoothness = saturate(lerp(_Glossiness, _Glossiness2, m) + edge * 0.25);
            o.Metallic = 0;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
