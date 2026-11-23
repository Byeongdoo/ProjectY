// 피격 플래시('캐릭터 조작 및 전투' 2-10): 스프라이트 모양 그대로 _Color 단색으로 그린다.
// CreatureController가 캐릭터 위에 겹친 자식 SpriteRenderer에 쓴다. Resources에 두어 빌드에 포함된다.
Shader "ProjectY/SpriteFlash"
{
	Properties
	{
		[PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
		_Color ("Flash Color", Color) = (1, 1, 1, 1)
	}

	SubShader
	{
		Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" "CanUseSpriteAtlas" = "True" }
		Cull Off
		Lighting Off
		ZWrite Off
		Blend SrcAlpha OneMinusSrcAlpha

		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"

			sampler2D _MainTex;
			fixed4 _Color;

			struct appdata
			{
				float4 vertex : POSITION;
				float2 uv : TEXCOORD0;
				fixed4 color : COLOR;
			};

			struct v2f
			{
				float4 pos : SV_POSITION;
				float2 uv : TEXCOORD0;
				fixed4 color : COLOR;
			};

			v2f vert(appdata v)
			{
				v2f o;
				o.pos = UnityObjectToClipPos(v.vertex);
				o.uv = v.uv;
				o.color = v.color;
				return o;
			}

			fixed4 frag(v2f i) : SV_Target
			{
				fixed a = tex2D(_MainTex, i.uv).a * i.color.a * _Color.a;
				return fixed4(_Color.rgb, a);
			}
			ENDCG
		}
	}
}
