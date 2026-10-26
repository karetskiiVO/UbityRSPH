Shader "RSPH/Particles"
{
    Properties
    {
        _SlowColor ("Slow color", Color) = (0.015, 0.22, 0.5, 1)
        _FastColor ("Fast color", Color) = (0.12, 0.85, 1, 1)
        _Radius ("Radius", Float) = 0.05
        _ColorSpeed ("Color speed", Float) = 6
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            Name "SPHForward"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull Back
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
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

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                uint instanceID : SV_InstanceID;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewWS : TEXCOORD1;
                half3 color : TEXCOORD2;
            };
            Varyings Vert(Attributes input)
            {
                Particle particle = _Particles[input.instanceID * max(_ParticleStride, 1u)];
                float3 positionWS = mul(_SimulationToWorld,
                    float4(particle.position.xyz + input.positionOS * _Radius, 1)).xyz;
                Varyings output;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = normalize(mul((float3x3)_SimulationToWorld, input.normalOS));
                output.viewWS = GetWorldSpaceViewDir(positionWS);
                output.color = lerp(_SlowColor.rgb, _FastColor.rgb,
                    saturate(length(particle.velocity.xyz) / max(_ColorSpeed, .001)));
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float3 normal = normalize(input.normalWS);
                float3 view = normalize(input.viewWS);
                float3 light = normalize(float3(-.4, .85, -.35));
                float diffuse = .35 + .65 * saturate(dot(normal, light));
                float specular = pow(saturate(dot(normal, normalize(light + view))), 64);
                float fresnel = pow(1 - saturate(dot(normal, view)), 3);
                return half4(input.color * diffuse + specular * .7 + fresnel * half3(.05, .18, .22), 1);
            }
            ENDHLSL
        }
    }
}
