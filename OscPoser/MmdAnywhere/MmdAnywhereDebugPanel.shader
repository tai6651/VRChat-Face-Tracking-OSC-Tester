Shader "MMD Anywhere/Root Telemetry"
{
    Properties
    {
        _MainTex ("Panel labels", 2D) = "white" {}
        _Rx0 ("Received 0", Float) = 0
        _Rx1 ("Received 1", Float) = 0
        _Rx2 ("Received 2", Float) = 0
        _Rx3 ("Received 3", Float) = 0
        _Rx4 ("Received 4", Float) = 0
        _Rx5 ("Received 5", Float) = 0
        _Rx6 ("Received 6", Float) = 0
        _Playback ("Playback enabled", Float) = 0
        _Tracking ("Face tracking available", Float) = 0
        _Local ("Local avatar", Float) = 0
        _Overlay ("Action overlay", Float) = 0
        _Row ("Overlay row", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "DisableBatching" = "True" }
        Cull Off
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Rx0, _Rx1, _Rx2, _Rx3, _Rx4, _Rx5, _Rx6;
            float _Playback, _Tracking, _Local, _Overlay, _Row;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 readout : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                // Face the viewing camera while keeping the mesh at the avatar's playback origin.
                float3 origin = mul(UNITY_MATRIX_M, float4(0, 0, 0, 1)).xyz;
                float3 scale = float3(length(mul((float3x3)UNITY_MATRIX_M, float3(1,0,0))),
                    length(mul((float3x3)UNITY_MATRIX_M, float3(0,1,0))),
                    length(mul((float3x3)UNITY_MATRIX_M, float3(0,0,1))));
                float4 view = mul(UNITY_MATRIX_V, float4(origin, 1));
                // The Action renderer uses scale ratios to carry its values across playable layers.
                float uniformScale = max(.0001, scale.z);
                o.readout = scale.xy / uniformScale;
                view.xy += v.vertex.xy * (_Overlay > .5 ? float2(uniformScale,uniformScale) : scale.xy);
                o.vertex = mul(UNITY_MATRIX_P, view);
                o.uv = v.uv;
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
                    segment(p,bits,1,float2(5,0),float2(20,4)) +
                    segment(p,bits,2,float2(20,4),float2(24,16)) +
                    segment(p,bits,4,float2(20,20),float2(24,32)) +
                    segment(p,bits,8,float2(5,32),float2(20,36)) +
                    segment(p,bits,16,float2(1,20),float2(5,32)) +
                    segment(p,bits,32,float2(1,4),float2(5,16)) +
                    segment(p,bits,64,float2(5,16),float2(20,20)));
            }

            float received(int row)
            {
                if (row == 0) return _Rx0;
                if (row == 1) return _Rx1;
                if (row == 2) return _Rx2;
                if (row == 3) return _Rx3;
                if (row == 4) return _Rx4;
                if (row == 5) return _Rx5;
                return _Rx6;
            }

            float4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 p = float2(i.uv.x, 1 - i.uv.y) * float2(960, 630);
                float3 color = tex2D(_MainTex, i.uv).rgb;
                float3 ok = float3(.28, .90, .73);
                float3 off = float3(1, .29, .24);
                color = lerp(color, lerp(off, ok, step(.5, _Playback)), box(p,float2(185,104),float2(207,127)));
                color = lerp(color, lerp(off, ok, step(.5, _Tracking)), box(p,float2(507,104),float2(529,127)));
                color = lerp(color, ok, step(.5, _Local) * box(p,float2(769,134),float2(820,138)));
                color = lerp(color, ok, (1 - step(.5, _Local)) * box(p,float2(843,134),float2(916,138)));

                int row = (int)floor((p.y - 212) / 53);
                if (row >= 0 && row < 7)
                {
                    float value = saturate(received(row));
                    uint byteValue = (uint)(value * 255 + .5);
                    float y = p.y - (219 + row * 53);
                    float numbers = digit(float2(p.x - 256, y), byteValue / 100) +
                        digit(float2(p.x - 288, y), (byteValue / 10) % 10) +
                        digit(float2(p.x - 320, y), byteValue % 10);
                    float3 rxColor = lerp(float3(1,.80,.39), off, step(byteValue, 15));
                    color = lerp(color, rxColor, saturate(numbers));
                    color = lerp(color, rxColor, box(p,float2(375,237+row*53),float2(375+140*value,242+row*53)));
                }
                if (_Overlay > .5)
                {
                    float status = (1 - step(.5,_Row)) * box(p,float2(667,104),float2(689,127));
                    float driving = step(1.5,i.readout.x);
                    float marker = 0;
                    if (row == (int)_Row && driving > .5)
                    {
                        float byteDelta = (i.readout.x - 2) * 255 - 128;
                        float steps = clamp(abs(byteDelta) - (_Row == 1 ? 1 : 0), 0, 111);
                        float fineRange = _Row == 1 ? .35 : 1.25;
                        float distance = min(steps, 80) / 80 * fineRange +
                            max(steps - 80, 0) / 31 * (5 - fineRange);
                        float position = sign(byteDelta) * distance / 5;
                        float x = 732.5 + position * 187.5;
                        marker = box(p,float2(x-5,226+row*53),float2(x+5,250+row*53));
                    }
                    return float4(lerp(off, ok, max(marker,driving)), max(status,marker));
                }
                return float4(color, 1);
            }
            ENDCG
        }
    }
}
