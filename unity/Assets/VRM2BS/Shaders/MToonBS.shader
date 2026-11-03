// MToon (VRM 1.0) look-alike for Beat Saber Custom Avatars.
// Uses the same property names as "VRM10/MToon10", so a material can be switched to it without copying values.
// Differences from MToon: lighting comes from a fixed key light relative to the viewer (Beat Saber has no Unity lights),
// and the alpha channel is written as "no glow" so the avatar does not bloom.
Shader "VRM2BS/MToon"
{
    Properties
    {
        _AlphaMode ("alphaMode", Int) = 0
        _TransparentWithZWrite ("mtoon.transparentWithZWrite", Int) = 0
        _Cutoff ("alphaCutoff", Range(0, 1)) = 0.5
        _RenderQueueOffset ("mtoon.renderQueueOffsetNumber", Int) = 0
        _DoubleSided ("doubleSided", Int) = 0

        _Color ("pbrMetallicRoughness.baseColorFactor", Color) = (1, 1, 1, 1)
        _MainTex ("pbrMetallicRoughness.baseColorTexture", 2D) = "white" {}
        _ShadeColor ("mtoon.shadeColorFactor", Color) = (1, 1, 1, 1)
        _ShadeTex ("mtoon.shadeMultiplyTexture", 2D) = "white" {}
        [Normal] _BumpMap ("normalTexture", 2D) = "bump" {}
        _BumpScale ("normalTexture.scale", Float) = 1.0
        _ShadingShiftFactor ("mtoon.shadingShiftFactor", Range(-1, 1)) = -0.05
        _ShadingShiftTex ("mtoon.shadingShiftTexture", 2D) = "black" {}
        _ShadingShiftTexScale ("mtoon.shadingShiftTexture.scale", Float) = 1
        _ShadingToonyFactor ("mtoon.shadingToonyFactor", Range(0, 1)) = 0.95

        [HDR] _EmissionColor ("emissiveFactor", Color) = (0, 0, 0, 1)
        _EmissionMap ("emissiveTexture", 2D) = "white" {}

        _MatcapColor ("mtoon.matcapFactor", Color) = (0, 0, 0, 1)
        _MatcapTex ("mtoon.matcapTexture", 2D) = "black" {}
        _RimColor ("mtoon.parametricRimColorFactor", Color) = (0, 0, 0, 1)
        _RimFresnelPower ("mtoon.parametricRimFresnelPowerFactor", Range(0, 100)) = 5.0
        _RimLift ("mtoon.parametricRimLiftFactor", Range(0, 1)) = 0
        _RimTex ("mtoon.rimMultiplyTexture", 2D) = "white" {}

        _OutlineWidthMode ("mtoon.outlineWidthMode", Int) = 0
        _OutlineWidth ("mtoon.outlineWidthFactor", Range(0, 0.05)) = 0
        _OutlineWidthTex ("mtoon.outlineWidthMultiplyTexture", 2D) = "white" {}
        _OutlineColor ("mtoon.outlineColorFactor", Color) = (0, 0, 0, 1)
        _OutlineLightingMix ("mtoon.outlineLightingMixFactor", Range(0, 1)) = 1

        _UvAnimMaskTex ("mtoon.uvAnimationMaskTexture", 2D) = "white" {}
        _UvAnimScrollXSpeed ("mtoon.uvAnimationScrollXSpeedFactor", Float) = 0
        _UvAnimScrollYSpeed ("mtoon.uvAnimationScrollYSpeedFactor", Float) = 0
        _UvAnimRotationSpeed ("mtoon.uvAnimationRotationSpeedFactor", Float) = 0

        _M_CullMode ("_CullMode", Float) = 2.0
        _M_SrcBlend ("_SrcBlend", Float) = 1.0
        _M_DstBlend ("_DstBlend", Float) = 0.0
        _M_ZWrite ("_ZWrite", Float) = 1.0

        // Beat Saber specific
        _BS_Ambient ("Beat Saber: minimum brightness", Range(0, 1)) = 0.35
        _BS_KeyLightUp ("Beat Saber: key light height", Range(-1, 2)) = 0.6
        _BS_KeyLightSide ("Beat Saber: key light side", Range(-1, 1)) = 0.3
    }

    CGINCLUDE
    #include "UnityCG.cginc"

    sampler2D _MainTex; float4 _MainTex_ST;
    sampler2D _ShadeTex;
    sampler2D _BumpMap; float _BumpScale;
    sampler2D _ShadingShiftTex; float _ShadingShiftTexScale;
    sampler2D _EmissionMap;
    sampler2D _MatcapTex;
    sampler2D _RimTex;
    sampler2D _OutlineWidthTex;
    sampler2D _UvAnimMaskTex;

    fixed4 _Color, _ShadeColor, _EmissionColor, _MatcapColor, _RimColor, _OutlineColor;
    float _Cutoff, _ShadingShiftFactor, _ShadingToonyFactor, _RimFresnelPower, _RimLift;
    float _OutlineWidth, _OutlineLightingMix;
    float _UvAnimScrollXSpeed, _UvAnimScrollYSpeed, _UvAnimRotationSpeed;
    float _BS_Ambient, _BS_KeyLightUp, _BS_KeyLightSide;

    struct appdata
    {
        float4 vertex : POSITION;
        float3 normal : NORMAL;
        float4 tangent : TANGENT;
        float2 uv : TEXCOORD0;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct v2f
    {
        float4 pos : SV_POSITION;
        float2 uv : TEXCOORD0;
        float3 worldPos : TEXCOORD1;
        float3 worldNormal : TEXCOORD2;
        float4 worldTangent : TEXCOORD3;
        UNITY_VERTEX_OUTPUT_STEREO
    };

    float2 AnimatedUv(float2 uv)
    {
        float mask = tex2Dlod(_UvAnimMaskTex, float4(uv, 0, 0)).b;
        float t = _Time.y * mask;
        float r = frac(_UvAnimRotationSpeed * t) * UNITY_TWO_PI;
        float s, c; sincos(r, s, c);
        uv = mul(float2x2(c, -s, s, c), uv - 0.5) + 0.5;
        return uv + float2(_UvAnimScrollXSpeed, -_UvAnimScrollYSpeed) * t;
    }

    v2f vertBase(appdata v, float outlineWidth)
    {
        v2f o;
        UNITY_SETUP_INSTANCE_ID(v);
        UNITY_INITIALIZE_OUTPUT(v2f, o);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

        float3 worldNormal = UnityObjectToWorldNormal(v.normal);
        float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
        worldPos += worldNormal * outlineWidth;

        o.pos = mul(UNITY_MATRIX_VP, float4(worldPos, 1));
        o.uv = TRANSFORM_TEX(v.uv, _MainTex);
        o.worldPos = worldPos;
        o.worldNormal = worldNormal;
        o.worldTangent = float4(UnityObjectToWorldDir(v.tangent.xyz), v.tangent.w * unity_WorldTransformParams.w);
        return o;
    }

    v2f vert(appdata v)
    {
        return vertBase(v, 0);
    }

    v2f vertOutline(appdata v)
    {
        float width = 0;
    #if defined(_MTOON_OUTLINE_WORLD) || defined(_MTOON_OUTLINE_SCREEN)
        width = _OutlineWidth * tex2Dlod(_OutlineWidthTex, float4(TRANSFORM_TEX(v.uv, _MainTex), 0, 0)).g;
    #endif
    #if defined(_MTOON_OUTLINE_SCREEN)
        // screen-relative width: scale by distance so it stays the same size on screen
        float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
        width *= distance(wp, _WorldSpaceCameraPos) * 0.5;
    #endif
        return vertBase(v, width);
    }

    float4 SampleBase(float2 uv)
    {
        float4 col = _Color * tex2D(_MainTex, uv);
    #if defined(_ALPHATEST_ON)
        clip(col.a - _Cutoff);
        col.a = 1;
    #elif !defined(_ALPHABLEND_ON)
        col.a = 1;
    #endif
        return col;
    }

    float3 KeyLightDir(float3 viewDir)
    {
        // light from the viewer's side, slightly above and to the side, so every camera sees a lit front
        float3 right = normalize(cross(float3(0, 1, 0), viewDir) + 1e-5);
        return normalize(viewDir + float3(0, _BS_KeyLightUp, 0) + right * _BS_KeyLightSide);
    }

    float4 frag(v2f i, fixed facing : VFACE) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

        float2 uv = AnimatedUv(i.uv);
        float4 baseCol = SampleBase(uv);

        float3 n = normalize(i.worldNormal) * (facing > 0 ? 1 : -1);
    #if defined(_NORMALMAP)
        float3 t = normalize(i.worldTangent.xyz);
        float3 b = cross(n, t) * i.worldTangent.w;
        float3 tn = UnpackNormal(tex2D(_BumpMap, uv));
        tn.xy *= _BumpScale;
        tn = normalize(tn);
        n = normalize(tn.x * t + tn.y * b + tn.z * n);
    #endif

        float3 viewDir = normalize(_WorldSpaceCameraPos - i.worldPos);
        float3 lightDir = KeyLightDir(viewDir);

        float shading = dot(n, lightDir) + _ShadingShiftFactor + tex2D(_ShadingShiftTex, uv).r * _ShadingShiftTexScale;
        shading = saturate((shading + 1 - _ShadingToonyFactor) / max(2 - 2 * _ShadingToonyFactor, 1e-4));

        float3 shadeCol = _ShadeColor.rgb * tex2D(_ShadeTex, uv).rgb;
        shadeCol = max(shadeCol, baseCol.rgb * _BS_Ambient);
        float3 col = lerp(shadeCol, baseCol.rgb, shading);

        // matcap
        float3 viewNormal = mul((float3x3)UNITY_MATRIX_V, n);
        col += tex2D(_MatcapTex, viewNormal.xy * 0.5 + 0.5).rgb * _MatcapColor.rgb;

        // parametric rim
        float rim = pow(saturate(1 - dot(n, viewDir) + _RimLift), max(_RimFresnelPower, 1e-4));
        col += rim * _RimColor.rgb * tex2D(_RimTex, uv).rgb;

        // emission
        col += _EmissionColor.rgb * tex2D(_EmissionMap, uv).rgb;

        // alpha = coverage; the blend state turns it into "erase glow behind the avatar"
        return float4(col, baseCol.a);
    }

    float4 fragOutline(v2f i) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
    #if !defined(_MTOON_OUTLINE_WORLD) && !defined(_MTOON_OUTLINE_SCREEN)
        clip(-1);
    #endif
        float2 uv = AnimatedUv(i.uv);
        float4 baseCol = SampleBase(uv);
        float3 col = _OutlineColor.rgb * lerp(1, baseCol.rgb, _OutlineLightingMix * 0.5);
        return float4(col, baseCol.a);
    }
    ENDCG

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }

        Pass
        {
            Name "FORWARD_BASE"
            Tags { "LightMode" = "ForwardBase" }
            Cull [_M_CullMode]
            Blend [_M_SrcBlend] [_M_DstBlend], Zero OneMinusSrcAlpha
            ZWrite [_M_ZWrite]

            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile __ _ALPHATEST_ON _ALPHABLEND_ON
            #pragma multi_compile __ _NORMALMAP
            ENDCG
        }

        Pass
        {
            Name "FORWARD_BASE_OUTLINE"
            Tags { "LightMode" = "ForwardBase" }
            Cull Front
            Blend [_M_SrcBlend] [_M_DstBlend], Zero OneMinusSrcAlpha
            ZWrite [_M_ZWrite]

            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vertOutline
            #pragma fragment fragOutline
            #pragma multi_compile_instancing
            #pragma multi_compile __ _ALPHATEST_ON _ALPHABLEND_ON
            #pragma multi_compile __ _MTOON_OUTLINE_WORLD _MTOON_OUTLINE_SCREEN
            ENDCG
        }
    }
    Fallback Off
}
