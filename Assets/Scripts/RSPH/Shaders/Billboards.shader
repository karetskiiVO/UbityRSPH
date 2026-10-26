Shader "RSPH/Particle Billboards"
{
    Properties
    {
        _SlowColor ("Slow color", Color) = (0.015, 0.22, 0.5, 1)
        _FastColor ("Fast color", Color) = (0.12, 0.85, 1, 1)
        _Radius ("Radius", Float) = 0.01
        _ColorSpeed ("Color speed", Float) = 6
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            Name "SPHBillboards"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "types.hlsl"
            StructuredBuffer<Particle> _Particles;
            float4x4 _SimulationToWorld;
            CBUFFER_START(UnityPerMaterial)
                float4 _SlowColor;
                float4 _FastColor;
                float _Radius;
                float _ColorSpeed;
                uint _ParticleStride;
            CBUFFER_END

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 viewWS : TEXCOORD1;
                half3 color : TEXCOORD2;
            };
            Varyings Vert(uint vertex : SV_VertexID, uint instance : SV_InstanceID)
            {
                const float2 corners[6] = {
                    float2(-1, -1), float2(-1, 1), float2(1, 1),
                    float2(-1, -1), float2(1, 1), float2(1, -1)
                };
                Particle p = _Particles[instance * max(_ParticleStride, 1u)];
                float3 center = mul(_SimulationToWorld, float4(p.position.xyz, 1)).xyz;
                float2 uv = corners[vertex];
                float scale = max(length(_SimulationToWorld._m00_m10_m20),
                    max(length(_SimulationToWorld._m01_m11_m21), length(_SimulationToWorld._m02_m12_m22)));
                float3 offset = mul((float3x3)UNITY_MATRIX_I_V, float3(uv * (_Radius * scale), 0));
                Varyings output;
                output.positionCS = TransformWorldToHClip(center + offset);
                output.uv = uv;
                output.viewWS = GetWorldSpaceViewDir(center);
                output.color = lerp(_SlowColor.rgb, _FastColor.rgb,
                    saturate(length(p.velocity.xyz) / max(_ColorSpeed, .001)));
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float radial = dot(input.uv, input.uv);
                clip(1 - radial);
                float3 normal = normalize(mul((float3x3)UNITY_MATRIX_I_V,
                    float3(input.uv, sqrt(saturate(1 - radial)))));
                float3 light = normalize(float3(-.4, .85, -.35));
                float3 view = normalize(input.viewWS);
                float diffuse = .35 + .65 * saturate(dot(normal, light));
                float specular = pow(saturate(dot(normal, normalize(light + view))), 64);
                float fresnel = pow(1 - saturate(dot(normal, view)), 3);
                return half4(input.color * diffuse + specular * .7 + fresnel * half3(.05, .18, .22), 1);
            }
            ENDHLSL
        }
    }
}
