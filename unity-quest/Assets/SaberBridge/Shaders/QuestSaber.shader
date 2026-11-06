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
            fixed4 _Color, _EmissionColor;
            half _Glow, _Shade;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float3 normal : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; half shade : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };

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
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv) * _Color;
                fixed3 e = tex2D(_EmissionMap, i.uv).rgb * _EmissionColor.rgb;
                return fixed4(c.rgb * i.shade + e, _Glow);
            }
            ENDCG
        }
    }
}
