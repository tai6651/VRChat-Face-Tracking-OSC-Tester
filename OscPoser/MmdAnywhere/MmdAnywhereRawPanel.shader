Shader "MMD Anywhere/Native Face Bytes"
{
    Properties
    {
        _MainTex ("Panel labels", 2D) = "white" {}
        _Tracking ("Face tracking available", Float) = 0
        _Local ("Local avatar", Float) = 0
        _Rx0 ("Byte 0", Float) = 0
        _Rx1 ("Byte 1", Float) = 0
        _Rx2 ("Byte 2", Float) = 0
        _Rx3 ("Byte 3", Float) = 0
        _Rx4 ("Byte 4", Float) = 0
        _Rx5 ("Byte 5", Float) = 0
        _Rx6 ("Byte 6", Float) = 0
        _Rx7 ("Byte 7", Float) = 0
        _Rx8 ("Byte 8", Float) = 0
        _Rx9 ("Byte 9", Float) = 0
        _Rx10 ("Byte 10", Float) = 0
        _Rx11 ("Byte 11", Float) = 0
        _Rx12 ("Byte 12", Float) = 0
        _Rx13 ("Byte 13", Float) = 0
        _Rx14 ("Byte 14", Float) = 0
        _Rx15 ("Byte 15", Float) = 0
        _Rx16 ("Byte 16", Float) = 0
        _Rx17 ("Byte 17", Float) = 0
        _Rx18 ("Byte 18", Float) = 0
        _Rx19 ("Byte 19", Float) = 0
        _Rx20 ("Byte 20", Float) = 0
        _Rx21 ("Byte 21", Float) = 0
        _Rx22 ("Byte 22", Float) = 0
        _Rx23 ("Byte 23", Float) = 0
        _Rx24 ("Byte 24", Float) = 0
        _Rx25 ("Byte 25", Float) = 0
        _Rx26 ("Byte 26", Float) = 0
        _Rx27 ("Byte 27", Float) = 0
        _Rx28 ("Byte 28", Float) = 0
        _Rx29 ("Byte 29", Float) = 0
        _Rx30 ("Byte 30", Float) = 0
        _Rx31 ("Byte 31", Float) = 0
        _Rx32 ("Byte 32", Float) = 0
        _Rx33 ("Byte 33", Float) = 0
        _Rx34 ("Byte 34", Float) = 0
        _Rx35 ("Byte 35", Float) = 0
        _Rx36 ("Byte 36", Float) = 0
        _Rx37 ("Byte 37", Float) = 0
        _Rx38 ("Byte 38", Float) = 0
        _Rx39 ("Byte 39", Float) = 0
        _Rx40 ("Byte 40", Float) = 0
        _Rx41 ("Byte 41", Float) = 0
        _Rx42 ("Byte 42", Float) = 0
        _Rx43 ("Byte 43", Float) = 0
        _Rx44 ("Byte 44", Float) = 0
        _Rx45 ("Byte 45", Float) = 0
        _Rx46 ("Byte 46", Float) = 0
        _Rx47 ("Byte 47", Float) = 0
        _Rx48 ("Byte 48", Float) = 0
        _Rx49 ("Byte 49", Float) = 0
        _Rx50 ("Byte 50", Float) = 0
        _Rx51 ("Byte 51", Float) = 0
        _Rx52 ("Byte 52", Float) = 0
        _Rx53 ("Byte 53", Float) = 0
        _Rx54 ("Byte 54", Float) = 0
        _Rx55 ("Byte 55", Float) = 0
        _Rx56 ("Byte 56", Float) = 0
        _Rx57 ("Byte 57", Float) = 0
        _Rx58 ("Byte 58", Float) = 0
        _Rx59 ("Byte 59", Float) = 0
        _Rx60 ("Byte 60", Float) = 0
        _Rx61 ("Byte 61", Float) = 0
        _Rx62 ("Byte 62", Float) = 0
        _Rx63 ("Byte 63", Float) = 0
        _Rx64 ("Byte 64", Float) = 0
        _Rx65 ("Byte 65", Float) = 0
        _Rx66 ("Byte 66", Float) = 0
        _Rx67 ("Byte 67", Float) = 0
        _Rx68 ("Byte 68", Float) = 0
        _Rx69 ("Byte 69", Float) = 0
        _Rx70 ("Byte 70", Float) = 0
        _Rx71 ("Byte 71", Float) = 0
        _Rx72 ("Byte 72", Float) = 0
        _Rx73 ("Byte 73", Float) = 0
        _Rx74 ("Byte 74", Float) = 0
        _Rx75 ("Byte 75", Float) = 0
        _Rx76 ("Byte 76", Float) = 0
        _Rx77 ("Byte 77", Float) = 0
        _Rx78 ("Byte 78", Float) = 0
        _Rx79 ("Byte 79", Float) = 0
        _Rx80 ("Byte 80", Float) = 0
        _Rx81 ("Byte 81", Float) = 0
        _Rx82 ("Byte 82", Float) = 0
        _Rx83 ("Byte 83", Float) = 0
        _Rx84 ("Byte 84", Float) = 0
        _Rx85 ("Byte 85", Float) = 0
        _Rx86 ("Byte 86", Float) = 0
        _Rx87 ("Byte 87", Float) = 0
        _Rx88 ("Byte 88", Float) = 0
        _Rx89 ("Byte 89", Float) = 0
        _Rx90 ("Byte 90", Float) = 0
        _Rx91 ("Byte 91", Float) = 0
        _Rx92 ("Byte 92", Float) = 0
        _Rx93 ("Byte 93", Float) = 0
        _Rx94 ("Byte 94", Float) = 0
        _Rx95 ("Byte 95", Float) = 0
        _Rx96 ("Byte 96", Float) = 0
        _Rx97 ("Byte 97", Float) = 0
        _Rx98 ("Byte 98", Float) = 0
        _Rx99 ("Byte 99", Float) = 0
        _Rx100 ("Byte 100", Float) = 0
        _Rx101 ("Byte 101", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "DisableBatching" = "True" }
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Tracking, _Local;
            float _Rx0, _Rx1, _Rx2, _Rx3, _Rx4, _Rx5, _Rx6, _Rx7;
            float _Rx8, _Rx9, _Rx10, _Rx11, _Rx12, _Rx13, _Rx14, _Rx15;
            float _Rx16, _Rx17, _Rx18, _Rx19, _Rx20, _Rx21, _Rx22, _Rx23;
            float _Rx24, _Rx25, _Rx26, _Rx27, _Rx28, _Rx29, _Rx30, _Rx31;
            float _Rx32, _Rx33, _Rx34, _Rx35, _Rx36, _Rx37, _Rx38, _Rx39;
            float _Rx40, _Rx41, _Rx42, _Rx43, _Rx44, _Rx45, _Rx46, _Rx47;
            float _Rx48, _Rx49, _Rx50, _Rx51, _Rx52, _Rx53, _Rx54, _Rx55;
            float _Rx56, _Rx57, _Rx58, _Rx59, _Rx60, _Rx61, _Rx62, _Rx63;
            float _Rx64, _Rx65, _Rx66, _Rx67, _Rx68, _Rx69, _Rx70, _Rx71;
            float _Rx72, _Rx73, _Rx74, _Rx75, _Rx76, _Rx77, _Rx78, _Rx79;
            float _Rx80, _Rx81, _Rx82, _Rx83, _Rx84, _Rx85, _Rx86, _Rx87;
            float _Rx88, _Rx89, _Rx90, _Rx91, _Rx92, _Rx93, _Rx94, _Rx95;
            float _Rx96, _Rx97, _Rx98, _Rx99, _Rx100, _Rx101;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float2 cell : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                nointerpolation float value : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float received(int index)
            {
                // Select the float in the vertex shader so fragments only draw the digits.
                float4 group = 0;
                switch (index / 4)
                {
                    case 0: group = float4(_Rx0, _Rx1, _Rx2, _Rx3); break;
                    case 1: group = float4(_Rx4, _Rx5, _Rx6, _Rx7); break;
                    case 2: group = float4(_Rx8, _Rx9, _Rx10, _Rx11); break;
                    case 3: group = float4(_Rx12, _Rx13, _Rx14, _Rx15); break;
                    case 4: group = float4(_Rx16, _Rx17, _Rx18, _Rx19); break;
                    case 5: group = float4(_Rx20, _Rx21, _Rx22, _Rx23); break;
                    case 6: group = float4(_Rx24, _Rx25, _Rx26, _Rx27); break;
                    case 7: group = float4(_Rx28, _Rx29, _Rx30, _Rx31); break;
                    case 8: group = float4(_Rx32, _Rx33, _Rx34, _Rx35); break;
                    case 9: group = float4(_Rx36, _Rx37, _Rx38, _Rx39); break;
                    case 10: group = float4(_Rx40, _Rx41, _Rx42, _Rx43); break;
                    case 11: group = float4(_Rx44, _Rx45, _Rx46, _Rx47); break;
                    case 12: group = float4(_Rx48, _Rx49, _Rx50, _Rx51); break;
                    case 13: group = float4(_Rx52, _Rx53, _Rx54, _Rx55); break;
                    case 14: group = float4(_Rx56, _Rx57, _Rx58, _Rx59); break;
                    case 15: group = float4(_Rx60, _Rx61, _Rx62, _Rx63); break;
                    case 16: group = float4(_Rx64, _Rx65, _Rx66, _Rx67); break;
                    case 17: group = float4(_Rx68, _Rx69, _Rx70, _Rx71); break;
                    case 18: group = float4(_Rx72, _Rx73, _Rx74, _Rx75); break;
                    case 19: group = float4(_Rx76, _Rx77, _Rx78, _Rx79); break;
                    case 20: group = float4(_Rx80, _Rx81, _Rx82, _Rx83); break;
                    case 21: group = float4(_Rx84, _Rx85, _Rx86, _Rx87); break;
                    case 22: group = float4(_Rx88, _Rx89, _Rx90, _Rx91); break;
                    case 23: group = float4(_Rx92, _Rx93, _Rx94, _Rx95); break;
                    case 24: group = float4(_Rx96, _Rx97, _Rx98, _Rx99); break;
                    case 25: group = float4(_Rx100, _Rx101, 0, 0); break;
                }
                return group[index % 4];
            }

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float4 origin = mul(UNITY_MATRIX_M, float4(0, 0, 0, 1));
                float2 scale = float2(length(mul((float3x3)UNITY_MATRIX_M, float3(1, 0, 0))),
                    length(mul((float3x3)UNITY_MATRIX_M, float3(0, 1, 0))));
                float4 view = mul(UNITY_MATRIX_V, origin);
                view.xy += v.vertex.xy * scale;
                o.vertex = mul(UNITY_MATRIX_P, view);
                o.uv = v.uv;
                o.value = received((int)v.cell.x);
                return o;
            }

            float box(float2 p, float2 lo, float2 hi)
            {
                float2 a = step(lo, p) * step(p, hi);
                return a.x * a.y;
            }

            float segment(float2 p, uint bits, uint bit, float2 lo, float2 hi)
            {
                return ((bits & bit) != 0) * box(p, lo, hi);
            }

            float digit(float2 p, uint value)
            {
                const uint masks[10] = {63, 6, 91, 79, 102, 109, 125, 7, 127, 111};
                uint bits = masks[value];
                return saturate(
                    segment(p, bits, 1, float2(4, 0), float2(13, 3)) +
                    segment(p, bits, 2, float2(13, 3), float2(16, 12)) +
                    segment(p, bits, 4, float2(13, 15), float2(16, 24)) +
                    segment(p, bits, 8, float2(4, 24), float2(13, 27)) +
                    segment(p, bits, 16, float2(1, 15), float2(4, 24)) +
                    segment(p, bits, 32, float2(1, 3), float2(4, 12)) +
                    segment(p, bits, 64, float2(4, 12), float2(13, 15)));
            }

            float4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 p = float2(i.uv.x, 1 - i.uv.y) * 1024;
                float3 color = tex2D(_MainTex, i.uv).rgb;
                float3 green = float3(.28, .90, .73);
                float3 red = float3(1, .29, .24);
                color = lerp(color, lerp(red, green, step(.5, _Tracking)), box(p, float2(833, 41), float2(847, 55)));
                color = lerp(color, green, step(.5, _Local) * box(p, float2(791, 86), float2(861, 89)));
                color = lerp(color, green, (1 - step(.5, _Local)) * box(p, float2(885, 86), float2(973, 89)));

                if (p.y >= 136 && p.y < 952 && p.x >= 32 && p.x < 992)
                {
                    float2 cell = float2(fmod(p.x - 32, 160), fmod(p.y - 136, 48));
                    uint value = (uint)(saturate(i.value) * 255 + .5);
                    float numbers = digit(cell - float2(72, 9), value / 100) +
                        digit(cell - float2(96, 9), (value / 10) % 10) +
                        digit(cell - float2(120, 9), value % 10);
                    float3 ink = lerp(float3(.51, .62, .72), float3(1, .80, .39), step(1, value));
                    color = lerp(color, ink, saturate(numbers));
                }

                return float4(color, 1);
            }
            ENDCG
        }
    }
}
