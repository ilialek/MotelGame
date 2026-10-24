Shader "Motel/World Service Text"
{
    Properties { _MainTex ("Font Atlas", 2D) = "white" {} }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            struct Attributes { float4 positionOS: POSITION; float2 uv: TEXCOORD0; half4 color: COLOR; };
            struct Varyings { float4 positionCS: SV_POSITION; float2 uv: TEXCOORD0; half4 color: COLOR; };
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv; output.color = input.color;
                return output;
            }
            half4 frag(Varyings input): SV_Target
            {
                return half4(input.color.rgb, input.color.a * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a);
            }
            ENDHLSL
        }
    }
}
