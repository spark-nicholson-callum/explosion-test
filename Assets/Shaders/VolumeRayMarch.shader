Shader "Custom/VolumeRayMarch"
{
    Properties
    {
        _VolumeTex ("Volume Texture (3D)", 3D) = "" {}
        _StepSize ("Step Size", Range(0.01, 0.1)) = 0.02

        [Header(fBM Noise Settings)]
        _NoiseScale ("Noise Scale", Float) = 10.0
        _NoiseStrength ("Noise Strength", Range(0.0, 2.0)) = 1.0
        _NoiseSpeed ("Noise Animation Speed", Float) = 0.5
    }
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalRenderPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
        }

        Blend One OneMinusSrcAlpha
        ZWrite Off
        Cull Back

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 localPos : TEXCOORD0;
                float3 localCamPos : TEXCOORD1;
                float3 worldPos : TEXCOORD2;
            };

            TEXTURE3D(_VolumeTex);
            SAMPLER(sampler_VolumeTex);

            CBUFFER_START(UnityperMaterial)
                float _StepSize;
                float _NoiseScale;
                float _NoiseStrength;
                float _NoiseSpeed;
            CBUFFER_END

            float noise3D(float3 x)
            {
                float3 p = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);

                float n = p.x + p.y * 157.0 + 113.0 * p.z;
                float a = frac(sin(n) * 43758.5453123);
                float b = frac(sin(n + 1.0) * 43758.5453123);
                float c = frac(sin(n + 157.0) * 43758.5453123);
                float d = frac(sin(n + 158.0) * 43758.5453123);
                float e = frac(sin(n + 113.0) * 43758.5453123);
                float f_n = frac(sin(n + 114.0) * 43758.5453123);
                float g = frac(sin(n + 270.0) * 43758.5453123);
                float h = frac(sin(n + 271.0) * 43758.5453123);

                float res = lerp(lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y),
                                 lerp(lerp(e, f_n, f.x), lerp(g, h, f.x), f.y), f.z);
                return res;
            }

            float fbm(float3 p)
            {
                float f = 0.0;
                float amp = 0.5;
                for(int i = 0; i < 3; i++)
                {
                    f += amp * noise3D(p);
                    p *= 2.0;
                    amp *= 0.5;
                }
                return f;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.localPos = IN.positionOS.xyz;
                OUT.worldPos = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.localCamPos = TransformWorldToObject(GetCameraPositionWS());

                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float3 rayDir = normalize(IN.localPos - IN.localCamPos);
                float3 rayPos = IN.localPos + float3(0.5, 0.5, 0.5);

                float4 finalColor = float4(0,0,0,0);

                // _Time is a float4 in URP, .y is unscaled time
                float3 noiseOffset = float3(0, -_Time.y * _NoiseSpeed, 0);

                for (int step = 0; step < 64; step++)
                {
                    if (any(rayPos < 0) || any(rayPos > 1)) break;

                    // URP texture sampling macro
                    float4 volumeData = SAMPLE_TEXTURE3D(_VolumeTex, sampler_VolumeTex, rayPos);

                    float heat = volumeData.r;
                    float baseDensity = volumeData.g;

                    if (baseDensity > 0.01)
                    {
                        float3 noiseSamplePos = (IN.worldPos * _NoiseScale) + noiseOffset;
                        float noiseVal = fbm(noiseSamplePos);

                        float erodedDensity = saturate(baseDensity - (noiseVal * _NoiseStrength * (1.0 - baseDensity)));

                        if (erodedDensity > 0.01)
                        {
                            float3 fireColor = float3(1.0, 0.4, 0.1) * heat * 3.0;
                            float3 smokeColor = float3(0.5, 0.5, 0.5);
                            float3 color = lerp(smokeColor, fireColor, heat);

                            float alpha = saturate(erodedDensity * _StepSize * 5.0);

                            finalColor.rgb += color * alpha * (1.0 - finalColor.a);
                            finalColor.a += alpha * (1.0 - finalColor.a);
                        }
                    }

                    rayPos += rayDir * _StepSize;
                    IN.worldPos += normalize(IN.worldPos - GetCameraPositionWS()) * _StepSize;
                }

                return half4(finalColor);
            }
            ENDHLSL
        }
    }
}
