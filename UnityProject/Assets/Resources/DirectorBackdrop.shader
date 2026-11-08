Shader "Yachiyo/StageBackdrop" {
 Properties { _MainTex ("Stage image", 2D) = "white" {} }
 SubShader { Tags { "Queue"="Background" "RenderType"="Opaque" } Cull Off ZWrite On
  Pass {
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "UnityCG.cginc"
   sampler2D _MainTex;
   struct Input {float4 vertex:POSITION;float2 uv:TEXCOORD0;};
   struct Output {float4 vertex:SV_POSITION;float2 uv:TEXCOORD0;};
   Output vert(Input v){Output o;o.vertex=UnityObjectToClipPos(v.vertex);o.uv=v.uv;return o;}
   fixed4 frag(Output i):SV_Target{return tex2D(_MainTex,i.uv);}
   ENDCG
  }
 }
}
