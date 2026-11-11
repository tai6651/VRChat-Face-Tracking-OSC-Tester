Shader "OscPoser/Desktop Camera"
{
    Properties
    {
        _MainTex ("Camera feed", 2D) = "black" {}
        [HideInInspector] _CaptureNear ("Capture camera near plane", Float) = 0.01337
    }
    SubShader
    {
        Tags { "Queue"="Overlay-2" "RenderType"="Transparent" "IgnoreProjector"="True" "DisableBatching"="True" "VRCFallback"="Hidden" }
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Always
            Blend Off
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _CaptureNear;
            float _VRChatCameraMode, _VRChatMirrorMode, _VRChatFaceMirrorMode;
            float3 _VRChatScreenCameraPos;
            float4 _VRChatScreenCameraRot;

            struct Attributes { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.uv = input.uv;
                float2 clipPosition = input.uv * 2 - 1;
                clipPosition.y *= _ProjectionParams.x;
                output.vertex = float4(clipPosition, UNITY_NEAR_CLIP_VALUE, 1);

                // Identify our capture camera by its near plane. VRChat can remap avatar layers.
                bool hidden = abs(_ProjectionParams.y - _CaptureNear) < 0.000002
                    || _VRChatCameraMode > 0.5
                    || _VRChatMirrorMode > 0.5 || _VRChatFaceMirrorMode > 0.5;
                if (dot(_VRChatScreenCameraRot, _VRChatScreenCameraRot) > 0.5)
                    hidden = hidden || unity_OrthoParams.w > 0.5
                        || distance(_WorldSpaceCameraPos, _VRChatScreenCameraPos) > 0.15;
                if (hidden)
                    output.vertex = float4(0, 0, 0, 0);
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                // Center-crop if the desktop window and the RenderTexture have different aspect ratios.
                float targetAspect = _ScreenParams.x / _ScreenParams.y;
                float sourceAspect = _MainTex_TexelSize.z / _MainTex_TexelSize.w;
                float2 scale = min(float2(targetAspect / sourceAspect, sourceAspect / targetAspect), 1);
                float2 uv = (input.uv - 0.5) * scale + 0.5;
                #if UNITY_UV_STARTS_AT_TOP
                    if (_MainTex_TexelSize.y < 0) uv.y = 1 - uv.y;
                #endif
                return float4(tex2D(_MainTex, uv).rgb, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
