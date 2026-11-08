Shader "Yachiyo/ClothToon" {
 Properties { _MainTex("Texture",2D)="white"{} _Color("Tint",Color)=(1,1,1,1) _Cutoff("Cutoff",Range(0,1))=0.3 _FaceLighting("Soft face lighting",Range(0,1))=0 _CullMode("Culling",Float)=0 _ClothCollision("Surface contacts",Float)=0 _ClothPadding("Layer spacing",Float)=0 _SleeveContactMargin("Sleeve contact thickness",Float)=0.03 _ClothColliderCount("Collider count",Float)=0 _SleeveTwoSided("Separate lining UV",Float)=0 }
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
   sampler2D _MainTex; float4 _MainTex_ST; fixed4 _Color; float _Cutoff,_FaceLighting,_SleeveTwoSided;
   float4 _FaceOrigin,_FaceForward,_FaceRight,_FaceUp;
   float _ClothCollision,_ClothPadding,_ClothColliderCount,_SleeveContactMargin;
   float4 _ClothCapsuleA[12],_ClothCapsuleB[12];
   float4 _ClothWaist,_ClothUp,_ClothRight,_ClothForward,_SleeveLeftDirection,_SleeveRightDirection;
   struct appdata { float4 vertex:POSITION; float3 normal:NORMAL; float2 uv:TEXCOORD0; float2 liningUV:TEXCOORD1; float2 contact:TEXCOORD2; };
   struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float3 normal:TEXCOORD1; float3 worldPos:TEXCOORD2; float2 liningUV:TEXCOORD3; };
   v2f vert(appdata v) {
    v2f o;float3 world=mul(unity_ObjectToWorld,v.vertex).xyz;float sleeveShift=0;
    bool sleeve=abs(v.contact.x)>.05;
    float free=smoothstep(.001,.03,v.contact.y);
    float height=dot(world-_ClothWaist.xyz,_ClothUp.xyz);
    float3 direction=normalize(v.contact.x<0?_SleeveLeftDirection.xyz:_SleeveRightDirection.xyz);
    if(_ClothCollision>.5 && (height<.02 || (sleeve&&free>0))){
     // A strictly increasing radial map keeps stacked skirt layers apart.
     // Hard clamping every layer onto one radius creates coincident triangles.
     for(int j=0;j<12;j++)if(j<_ClothColliderCount){
      if(_ClothCapsuleB[j].w>1.5)continue;
      bool body=_ClothCapsuleB[j].w>.5;
      if(body&&(!sleeve||free<=0))continue;
      float3 a=_ClothCapsuleA[j].xyz,b=_ClothCapsuleB[j].xyz,axis=b-a;
      float t=saturate(dot(world-a,axis)/max(dot(axis,axis),.000001));
      float3 center=a+axis*t,away=world-center;float distance=length(away);
      float radius=_ClothCapsuleA[j].w+(body?.012:_ClothPadding);
      if(sleeve){
       // Account for the span of broad sleeve triangles around the leg shell.
       if(!body)radius+=_SleeveContactMargin;
       float3 outward=direction;
       float along=dot(away,outward);
       float perpendicular2=max(0,dot(away,away)-along*along);
       if(perpendicular2<radius*radius){
        float limit=sqrt(radius*radius-perpendicular2);
        float delta=limit-along;
        sleeveShift=max(sleeveShift,(max(delta,0)+.012*log(1+exp(-abs(delta)/.012)))*(body?free:1));
       }
      }else if(distance<radius+.20){
       float band=.035;
       float clearance=radius+band*log(1+exp((distance-radius)/band));
       world=center+(distance>.00001?away/distance:float3(0,0,1))*clearance;
      }
     }
     // Only the hanging sleeve is kept outside the dressed silhouette. Fitted
     // bodice, collar and skirt vertices never enter this envelope correction.
     if(sleeve&&free>0){
      float3 relative=world-_ClothWaist.xyz;
      float below=-dot(relative,_ClothUp.xyz);
      if(below>0 && below<1.02){
       float t=saturate(below/.8),rx=lerp(.20,.54,t),rz=lerp(.28,.42,t);
       float2 p=float2(dot(relative,_ClothRight.xyz)/rx,(dot(relative,_ClothForward.xyz)+.025)/rz);
       float2 d=float2(dot(direction,_ClothRight.xyz)/rx,dot(direction,_ClothForward.xyz)/rz);
       float a=dot(d,d),b=dot(p,d),c=dot(p,p)-1;
       if(c<0 && a>.0001){float shift=(-b+sqrt(max(0,b*b-a*c)))/a;sleeveShift=max(sleeveShift,(shift+.015)*free);}
      }
     }
    }
    if(sleeve && _ClothCollision>.5 && sleeveShift>0)world+=direction*sleeveShift*.8;
    o.pos=mul(UNITY_MATRIX_VP,float4(world,1));o.uv=TRANSFORM_TEX(v.uv,_MainTex);o.normal=UnityObjectToWorldNormal(v.normal);o.worldPos=world;o.liningUV=TRANSFORM_TEX(v.liningUV,_MainTex);return o;
   }
   fixed4 frag(v2f i,fixed facing:VFACE):SV_Target {
    float2 uv=(_SleeveTwoSided>.5 && facing<0)?i.liningUV:i.uv;
    if(_SleeveTwoSided>.5 && facing<0)i.normal=-i.normal;
    fixed4 c=tex2D(_MainTex,uv)*_Color; clip(c.a-_Cutoff);
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
