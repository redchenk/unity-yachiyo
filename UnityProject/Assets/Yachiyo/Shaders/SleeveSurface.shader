Shader "Yachiyo/ContinuousSleeve" {
 Properties { _Outer("Outer",Color)=(.24,.215,.38,1) _Lining("Lining",Color)=(.78,.065,.17,1) _Motif("Motif",Color)=(.01,.61,.68,1) }
 SubShader {
  Tags { "RenderType"="Opaque" }
  Cull Off
  Pass {
   Tags { "LightMode"="ForwardBase" }
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "UnityCG.cginc"
   #include "Lighting.cginc"
   float4 _Outer,_Lining,_Motif;
   struct appdata {float4 vertex:POSITION;float3 normal:NORMAL;float2 uv:TEXCOORD0;float2 uv2:TEXCOORD1;};
   struct v2f {float4 position:SV_POSITION;float3 normal:TEXCOORD0;float3 uv:TEXCOORD1;};
   v2f vert(appdata v){v2f o;o.position=UnityObjectToClipPos(v.vertex);o.normal=UnityObjectToWorldNormal(v.normal);o.uv=float3(v.uv,v.uv2.x);return o;}
   float diamond(float2 p){p=abs(p);return 1-smoothstep(.029,.032,p.x+p.y);}
   float emblem(float2 p){
    float s=0;
    for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2){float2 q=p-float2(x,y)*.026;s=max(s,diamond(q)*(smoothstep(.004,.006,length(q))));}
    return s;
   }
   fixed4 frag(v2f i,fixed facing:VFACE):SV_Target {
    float3 n=normalize(i.normal)*(facing>0?1:-1);
   float2 sleeveUV=float2(frac(atan2(i.uv.y,i.uv.x)/6.2831853+1),i.uv.z);
    float light=smoothstep(-.35,.55,dot(n,normalize(_WorldSpaceLightPos0.xyz)));
    float3 base=(facing>0||sleeveUV.y<.65)?_Outer.rgb:_Lining.rgb;
    // Red folded binding is part of this same surface, with no overlapping shell.
    if(sleeveUV.y>.984)base=_Lining.rgb;
    if(facing>0){
     float u=sleeveUV.x;float mark=max(emblem(float2(u-.25,(sleeveUV.y-.67)*.55)),emblem(float2(u-.75,(sleeveUV.y-.67)*.55)));
     base=lerp(base,_Motif.rgb,mark);
     float stitch=(1-smoothstep(.002,.004,abs(sleeveUV.y-.957)))*step(.4,frac(u*48));
     base=lerp(base,float3(.64,.29,.46),stitch);
    }
    base*=lerp(float3(.79,.80,.90),float3(1,.99,.98),light);
    return float4(base,1);
   }
   ENDCG
  }
 }
}
