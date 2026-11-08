Shader "Yachiyo/SoftToon" {
 Properties { _MainTex("Texture",2D)="white"{} _Color("Tint",Color)=(1,1,1,1) _Cutoff("Cutoff",Range(0,1))=0.3 _FaceLighting("Soft face lighting",Range(0,1))=0 _CullMode("Culling",Float)=0 }
 SubShader {
  Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
  Cull [_CullMode]
  Pass {
   Tags { "LightMode"="ForwardBase" }
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "UnityCG.cginc"
   #include "Lighting.cginc"
   sampler2D _MainTex; float4 _MainTex_ST; fixed4 _Color; float _Cutoff,_FaceLighting;
   float4 _FaceOrigin,_FaceForward,_FaceRight,_FaceUp;
   struct appdata { float4 vertex:POSITION; float3 normal:NORMAL; float2 uv:TEXCOORD0; };
   struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float3 normal:TEXCOORD1; float3 worldPos:TEXCOORD2; };
   v2f vert(appdata v) {
    v2f o;float3 world=mul(unity_ObjectToWorld,v.vertex).xyz;
    o.pos=mul(UNITY_MATRIX_VP,float4(world,1));o.uv=TRANSFORM_TEX(v.uv,_MainTex);o.normal=UnityObjectToWorldNormal(v.normal);o.worldPos=world;return o;
   }
   fixed4 frag(v2f i):SV_Target {
    fixed4 c=tex2D(_MainTex,i.uv)*_Color; clip(c.a-_Cutoff);
    float l=smoothstep(-0.35,0.55,dot(normalize(i.normal),normalize(_WorldSpaceLightPos0.xyz)));
    float3 shade=lerp(float3(0.79,0.80,0.90),float3(1,0.99,0.98),l);
    if(_FaceLighting>.5){
     // Smooth head-space curvature avoids lighting islands on mouth/nose topology.
     float3 relative=i.worldPos-_FaceOrigin.xyz;
     float side=clamp(dot(relative,_FaceRight.xyz)/.105,-1,1);
     float height=clamp(dot(relative,_FaceUp.xyz)/.13,-1,1);
     float3 faceNormal=normalize(_FaceForward.xyz+_FaceRight.xyz*side*.4+_FaceUp.xyz*height*.2);
     float soft=smoothstep(-.6,.8,dot(faceNormal,normalize(_WorldSpaceLightPos0.xyz)));
     shade=lerp(float3(.94,.925,.95),float3(1,1,1),soft);
    }
    c.rgb*=shade;c.a=1;return c;
   }
   ENDCG
  }
 }
 Fallback "Unlit/Texture"
}
