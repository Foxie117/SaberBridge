// Quest-safe replacement for PC saber shaders: texture x colour, optional emission, alpha carries the glow amount.
// Materials with _CustomColors = 1 get the player's saber colour in _Color from Qosmetics, like on PC.
Shader "SaberBridge/QuestSaber"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Texture", 2D) = "white" {}
        _EmissionColor ("Emission", Color) = (0,0,0,0)
        _EmissionMap ("Emission Map", 2D) = "white" {}
        _Glow ("Glow", Range(0,1)) = 0
        _Bloom ("Bloom", Range(0,1)) = 0
        _CustomColors ("Custom Colors", Float) = 0
        _Shade ("Fake Lighting", Range(0,1)) = 0.35
        _MatCap ("MatCap: part tinted by _Color (baked from the PC shader)", 2D) = "white" {}
        _MatCapBase ("MatCap: part not tinted", 2D) = "black" {}
        _UseMatCap ("Use MatCap", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Cull [_Cull]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            sampler2D _MainTex; float4 _MainTex_ST;
            sampler2D _EmissionMap;
            sampler2D _MatCap, _MatCapBase;
            half _UseMatCap;
            fixed4 _Color, _EmissionColor;
            half _Glow, _Shade;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float3 normal : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; half shade : TEXCOORD1; float2 cap : TEXCOORD2; UNITY_VERTEX_OUTPUT_STEREO };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                // view-facing fake light so unlit sabers still show their shape
                float3 n = normalize(UnityObjectToWorldNormal(v.normal));
                float3 viewDir = normalize(WorldSpaceViewDir(v.vertex));
                o.shade = lerp(1, saturate(dot(n, viewDir)) * 0.6 + 0.4, _Shade);
                // view-space normal picks the matching spot of the baked sphere
                float3 vn = normalize(mul((float3x3)UNITY_MATRIX_IT_MV, v.normal));
                o.cap = vn.xy * 0.5 + 0.5;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 t = tex2D(_MainTex, i.uv);
                fixed3 e = tex2D(_EmissionMap, i.uv).rgb * _EmissionColor.rgb;
                // PC look: what the original shader drew with a black and with a white _Color, so any colour in between
                // (the player's note or saber colour) comes out the way the PC shader would draw it
                fixed3 baked = tex2D(_MatCap, i.cap).rgb * _Color.rgb + tex2D(_MatCapBase, i.cap).rgb;
                fixed3 simple = _Color.rgb * i.shade;
                return fixed4(t.rgb * lerp(simple, baked, _UseMatCap) + e, _Glow);
            }
            ENDCG
        }
    }
}
