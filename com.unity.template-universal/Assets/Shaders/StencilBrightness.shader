Shader "Hidden/Template/StencilBrightness"
{
    Properties
    {
        _StencilRef ("Stencil Reference", Float) = 1
    }

    HLSLINCLUDE
    #pragma target 4.5
    #pragma multi_compile_fragment _ _LINEAR_TO_SRGB_CONVERSION
    #pragma multi_compile _ _USE_DRAW_PROCEDURAL
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureXR.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/Shaders/Utils/Fullscreen.hlsl"

    TEXTURE2D(_StencilBrightnessSource);
    SAMPLER(sampler_LinearClamp);

    float4 _StencilBrightnessParams; // x: brighten, y: darken, z: stencil, w: unused

    half4 BrightenFrag(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        half4 color = SAMPLE_TEXTURE2D(_StencilBrightnessSource, sampler_LinearClamp, input.uv);
        color.rgb *= _StencilBrightnessParams.x;
        return color;
    }

    half4 DarkenFrag(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        half4 color = SAMPLE_TEXTURE2D(_StencilBrightnessSource, sampler_LinearClamp, input.uv);
        color.rgb *= _StencilBrightnessParams.y;
        return color;
    }

    half4 CopyFrag(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        return SAMPLE_TEXTURE2D(_StencilBrightnessSource, sampler_LinearClamp, input.uv);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalRenderPipeline" }
        LOD 0

        Pass
        {
            Name "Brighten"
            Tags { "LightMode" = "UniversalFullscreen" }
            Cull Off
            ZWrite Off
            ZTest Always
            Blend One Zero
            ColorMask RGBA
            Stencil
            {
                Ref [_StencilRef]
                Comp Equal
                Pass Keep
                Fail Keep
                ZFail Keep
            }

            HLSLPROGRAM
            #pragma vertex FullscreenVert
            #pragma fragment BrightenFrag
            ENDHLSL
        }

        Pass
        {
            Name "Darken"
            Tags { "LightMode" = "UniversalFullscreen" }
            Cull Off
            ZWrite Off
            ZTest Always
            Blend One Zero
            ColorMask RGBA
            Stencil
            {
                Ref [_StencilRef]
                Comp NotEqual
                Pass Keep
                Fail Keep
                ZFail Keep
            }

            HLSLPROGRAM
            #pragma vertex FullscreenVert
            #pragma fragment DarkenFrag
            ENDHLSL
        }

        Pass
        {
            Name "Copy"
            Tags { "LightMode" = "UniversalFullscreen" }
            Cull Off
            ZWrite Off
            ZTest Always
            Blend One Zero
            ColorMask RGBA
            Stencil
            {
                Ref 0
                Comp Always
                Pass Keep
                Fail Keep
                ZFail Keep
            }

            HLSLPROGRAM
            #pragma vertex FullscreenVert
            #pragma fragment CopyFrag
            ENDHLSL
        }
    }

    FallBack Off
}
