// Stand-in for a PC-only saber shader, used only by the demo saber (SaberBridge swaps it for its Quest shader).
Shader "SaberBridgeDemo/Glow"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _Glow ("Glow", Range(0,1)) = 1
        _CustomColors ("Custom Colors", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Color; half _Glow;
            float4 vert (float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            fixed4 frag () : SV_Target { return fixed4(_Color.rgb, _Glow); }
            ENDCG
        }
    }
}
