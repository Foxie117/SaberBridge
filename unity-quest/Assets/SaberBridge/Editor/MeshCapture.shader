// Development-time helper for SaberBridge: recovers meshes that were saved without Read/Write.
// Drawing such a mesh with this shader writes every vertex (by SV_VertexID) and every triangle's
// vertex indices (by SV_PrimitiveID) into buffers that the converter reads back.
Shader "Hidden/SaberBridge/MeshCapture"
{
    SubShader
    {
        Cull Off ZWrite Off ZTest Always ColorMask 0
        Pass
        {
            CGPROGRAM
            #pragma target 5.0
            #pragma vertex vert
            #pragma geometry geom
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct CapturedVertex
            {
                float3 position;
                float3 normal;
                float4 tangent;
                float4 uv0;
                float4 uv1;
                float4 color;
            };

            RWStructuredBuffer<CapturedVertex> _Vertices : register(u1);
            RWStructuredBuffer<uint> _Triangles : register(u2);

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 tangent : TANGENT;
                float4 uv0 : TEXCOORD0;
                float4 uv1 : TEXCOORD1;
                float4 color : COLOR;
                uint id : SV_VertexID;
            };

            struct v2g
            {
                float4 pos : SV_POSITION;
                uint id : TEXCOORD0;
            };

            v2g vert (appdata v)
            {
                CapturedVertex c;
                c.position = v.vertex.xyz;
                c.normal = v.normal;
                c.tangent = v.tangent;
                c.uv0 = v.uv0;
                c.uv1 = v.uv1;
                c.color = v.color;
                _Vertices[v.id] = c;

                v2g o;
                o.pos = float4(0, 0, 0, 1);
                o.id = v.id;
                return o;
            }

            [maxvertexcount(1)]
            void geom (triangle v2g input[3], uint primitive : SV_PrimitiveID, inout PointStream<v2g> stream)
            {
                _Triangles[primitive * 3 + 0] = input[0].id;
                _Triangles[primitive * 3 + 1] = input[1].id;
                _Triangles[primitive * 3 + 2] = input[2].id;
            }

            fixed4 frag (v2g i) : SV_Target
            {
                return 0;
            }
            ENDCG
        }
    }
}
