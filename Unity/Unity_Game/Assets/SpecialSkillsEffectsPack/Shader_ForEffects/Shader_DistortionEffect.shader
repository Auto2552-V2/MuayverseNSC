Shader "GAPH Custom Shader/Distortion Effect" {
	Properties {
		_TintColor ("Tint Color", Color) = (1,1,1,1)
		_Mask ("Mask", 2D) = "black" {}
		_NormalMap ("Normalmap", 2D) = "bump" {}
		_DistortFactor ("Distortion", Float) = 10
		_InvFade ("Soft Particles Factor", Range(0,10)) = 1.0
	}

	SubShader {
		Tags {
			"Queue" = "Transparent"
			"IgnoreProjector" = "True"
			"RenderType" = "Transparent"
			"RenderPipeline" = "UniversalPipeline"
		}

		Blend SrcAlpha OneMinusSrcAlpha
		Cull Off
		ZWrite Off

		Pass {
			Name "Distortion"
			Tags { "LightMode" = "UniversalForward" }

			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 2.0

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

			struct Attributes {
				float4 positionOS : POSITION;
				float2 texcoord : TEXCOORD0;
				half4 color : COLOR;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings {
				float4 positionCS : SV_POSITION;
				float2 uvnormal : TEXCOORD0;
				float2 uvmask : TEXCOORD1;
				half4 color : COLOR;
				UNITY_VERTEX_OUTPUT_STEREO
			};

			TEXTURE2D(_Mask);
			SAMPLER(sampler_Mask);
			TEXTURE2D(_NormalMap);
			SAMPLER(sampler_NormalMap);

			CBUFFER_START(UnityPerMaterial)
				half4 _TintColor;
				float4 _Mask_ST;
				float4 _NormalMap_ST;
				half _DistortFactor;
				half _InvFade;
			CBUFFER_END

			Varyings vert(Attributes input) {
				Varyings output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
				output.uvnormal = input.texcoord * _NormalMap_ST.xy + _NormalMap_ST.zw;
				output.uvmask = input.texcoord * _Mask_ST.xy + _Mask_ST.zw;
				output.color = input.color;
				return output;
			}

			half4 frag(Varyings input) : SV_Target {
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

				float2 screenUV = input.positionCS.xy / _ScaledScreenParams.xy;
				half2 normal = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, input.uvnormal)).rg;
				float2 distortUV = normal * _DistortFactor * _CameraOpaqueTexture_TexelSize.xy;

				#if defined(SHADER_API_D3D11) || defined(SHADER_API_METAL)
					distortUV *= 10.0;
				#endif

				half4 mask = SAMPLE_TEXTURE2D(_Mask, sampler_Mask, input.uvmask);
				half4 color = half4(SampleSceneColor(screenUV + distortUV), 1.0);
				color.a = _TintColor.a * input.color.a * mask.a;
				return color;
			}
			ENDHLSL
		}
	}
}
