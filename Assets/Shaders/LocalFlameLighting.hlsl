#ifndef FIRST_SETTLERS_LOCAL_FLAME_LIGHTING
#define FIRST_SETTLERS_LOCAL_FLAME_LIGHTING
#define FS_FLAME_CAPACITY 64
int _FSFlameCount;
float4 _FSFlamePositions[FS_FLAME_CAPACITY];
half4 _FSFlameColors[FS_FLAME_CAPACITY];
half4 _FSFlameAverageColors[FS_FLAME_CAPACITY];
float4x4 _FSFlameShadowVP[FS_FLAME_CAPACITY*6];
TEXTURE2D_ARRAY(_FSFlameStaticDepth);
TEXTURE2D_ARRAY(_FSFlameDynamicDepth);
StructuredBuffer<int4> _FSFlameCells;
StructuredBuffer<int2> _FSFlameLinks;
StructuredBuffer<int4> _FSSurfacePages;
TEXTURE2D_ARRAY(_FSSurfaceNormals);
TEXTURE2D_ARRAY(_FSSurfaceIrradiance);
int _FSSurfaceEnabled;
int _FSSurfaceDebug;
float _FSFlameDynamicReceiver;
int _FSSurfaceAdaptive,_FSFlameAverageOnlyCount;
uint FSFlameHash(int3 cell) {return (uint)cell.x*73856093u^(uint)cell.y*19349663u^(uint)cell.z*83492791u;}
uint FSFlameFace(float3 v)
{
    float3 a=abs(v);return a.x>=a.y && a.x>=a.z?(v.x>0?0:1):a.y>=a.z?(v.y>0?2:3):(v.z>0?4:5);
}
float3 FSFlameAxis(uint face)
{
    return face==0?float3(1,0,0):face==1?float3(-1,0,0):face==2?float3(0,1,0):face==3?float3(0,-1,0):face==4?float3(0,0,1):float3(0,0,-1);
}
float3 FSFlameUp(uint face){return face==2?float3(0,0,1):face==3?float3(0,0,-1):float3(0,-1,0);}
int FSFlameHead(float3 p)
{
    int3 cell=(int3)floor(p/8);uint bucket=FSFlameHash(cell)&8191;
    [loop]for(uint probe=0;probe<8192;probe++)
    {int4 entry=_FSFlameCells[bucket];if(entry.w==0)return 0;if(all(entry.xyz==cell))return entry.w;bucket=(bucket+1)&8191;}
    return 0;
}
// Receiver-plane comparison evaluates each sample on the receiver plane, avoiding
// the concentric self-shadow bands caused by comparing all samples to one distance.
half FSFlameVisibility(uint index,float3 positionWS,float3 delta,half3 normalWS,bool moving)
{
    uint face=FSFlameFace(delta),slice=index*6+face;
    float4 clip=mul(_FSFlameShadowVP[slice],float4(positionWS,1));
    float2 uv=clip.xy/clip.w*.5+.5;
    #if UNITY_UV_STARTS_AT_TOP
        uv.y=1-uv.y;
    #endif
    float3 forward=FSFlameAxis(face),up=FSFlameUp(face),right=cross(up,forward);
    float size=moving?64:256;
    half visibility=0;
    [unroll]for(uint tap=0;tap<4;tap++)
    {
        float2 sampleUV=uv+(float2(tap&1,tap>>1)-.5)/size;
        int2 pixel=clamp((int2)(sampleUV*size),0,(int)size-1);
        sampleUV=(pixel+.5)/size;
        float3 ray=normalize(forward+right*(sampleUV.x*2-1)+up*(sampleUV.y*2-1));
        float denominator=dot(normalWS,ray);
        float distance=abs(denominator)>.0001?dot(normalWS,delta)/denominator:length(delta);
        float compare=(max(0,distance)-.006)/_FSFlamePositions[index].w;
        float depth;
        if(moving)depth=LOAD_TEXTURE2D_ARRAY_LOD(_FSFlameDynamicDepth,pixel,slice,0).r;
        else depth=LOAD_TEXTURE2D_ARRAY_LOD(_FSFlameStaticDepth,pixel,slice,0).r;
        visibility+=compare<=depth?.25h:0;
    }
    return visibility;
}
float FSFlameAttenuation(float distanceSquared,float rangeSquared)
{
    float ratio=distanceSquared/rangeSquared;
    float edge=saturate(1-ratio*ratio);
    return edge*edge/(1+distanceSquared*.2);
}
half3 FSFlameUnit(uint i,float3 positionWS,half3 normalWS,half3 planeNormal)
{
    float3 delta=positionWS-_FSFlamePositions[i].xyz;float d2=dot(delta,delta),r=_FSFlamePositions[i].w;
    if(d2>=r*r || _FSFlameColors[i].a<.5h)return 0;
    half ndotl=saturate(dot(normalWS,-delta*rsqrt(max(d2,.0001))));
    if(ndotl<=0)return 0;
    return (ndotl*FSFlameAttenuation(d2,r*r)*FSFlameVisibility(i,positionWS,delta,planeNormal,false)).xxx;
}
half3 FSFlameDirect(float3 positionWS,half3 normalWS,bool average,half3 planeNormal)
{
    half3 result=0;int head=FSFlameHead(positionWS);
    [loop]for(uint visit=0;head!=0 && visit<FS_FLAME_CAPACITY;visit++)
    {
        int2 link=_FSFlameLinks[head-1];uint i=link.x;head=link.y;
        if(average && _FSFlameAverageColors[i].a<.5h)continue;
        half3 unit=FSFlameUnit(i,positionWS,normalWS,planeNormal);
        if(average)result+=unit*_FSFlameAverageColors[i].rgb;
        else if(any(unit>0))result+=unit*_FSFlameColors[i].rgb*FSFlameVisibility(i,positionWS,positionWS-_FSFlamePositions[i].xyz,planeNormal,true);
    }
    return result;
}
bool FSFlameCacheWorthwhile(float3 p,half3 n)
{
    if(_FSSurfaceAdaptive==0)return true;
    if(_FSFlameAverageOnlyCount<2)return false;
    int head=FSFlameHead(p),averaged=0;
    [loop]for(uint visit=0;head!=0 && visit<FS_FLAME_CAPACITY;visit++)
    {
        int2 link=_FSFlameLinks[head-1];uint i=link.x;head=link.y;
        float3 delta=_FSFlamePositions[i].xyz-p;float r=_FSFlamePositions[i].w;
        if(dot(delta,delta)>=r*r || dot(delta,n)<=0)continue;
        if(_FSFlameColors[i].a>2.5h)return false;
        if(_FSFlameColors[i].a<1.5h)averaged++;
    }
    return averaged>=2;
}
bool FSFlameSurface(float3 p,half3 n,out half3 irradiance,out int usedMip)
{
    float footprint=max(length(ddx(p)),length(ddy(p)));
    irradiance=0;usedMip=0;if(_FSSurfaceEnabled==0 || _FSFlameDynamicReceiver>.5)return false;
    if(!FSFlameCacheWorthwhile(p,n))return false;
    int3 cell=(int3)floor(p/8);uint bucket=FSFlameHash(cell)&1023;int page=-1;
    [loop]for(uint probe=0;probe<1024;probe++)
    {int4 entry=_FSSurfacePages[bucket];if(entry.w==0)return false;if(all(entry.xyz==cell)){page=entry.w-1;break;}bucket=(bucket+1)&1023;}
    if(page<0)return false;
    uint face=FSFlameFace(n);float3 direction=FSFlameAxis(face),up=FSFlameUp(face),right=cross(up,-direction);
    float3 center=(float3(cell)+.5)*8;
    float2 uv=float2(dot(p-center,right),dot(p-center,up))/8+.5;
    int desiredMip=(int)clamp(floor(log2(max(1,footprint*4))),0,3);
    [loop]for(int mip=desiredMip;mip>=0;mip--)
    {
    float resolution=32>>mip;irradiance=0;
    float2 texel=uv*resolution-.5,fracUV=frac(texel);int2 origin=(int2)floor(texel);
    float weight=0;half3 low=65504,high=0;
    [unroll]for(uint tap=0;tap<4;tap++)
    {
        int2 offset=int2(tap&1,tap>>1),pixel=origin+offset;
        if(any(pixel<0)||any(pixel>=resolution))continue;
        float4 sample=LOAD_TEXTURE2D_ARRAY_LOD(_FSSurfaceNormals,pixel,page*6+face,mip);
        if(sample.w<.5 || dot(sample.xyz,n)<.98)continue;
        float2 sampleUV=(pixel+.5)/resolution;
        float3 sampleWS=center+right*((sampleUV.x-.5)*8)+up*((sampleUV.y-.5)*8)+direction*(sample.w-5);
        if(abs(dot(sample.xyz,p-sampleWS))>.012)continue;
        float2 w=lerp(1-fracUV,fracUV,(float2)offset);float amount=w.x*w.y;
        half3 value=LOAD_TEXTURE2D_ARRAY_LOD(_FSSurfaceIrradiance,pixel,page*6+face,mip).rgb;
        irradiance+=value*amount;weight+=amount;low=min(low,value);high=max(high,value);
    }
    if(weight<.1)continue;
    irradiance/=weight;
    if(mip>0 && any(high-low>max(irradiance*.5,.002)))continue;
    usedMip=mip;return true;
    }
    return false;
}
half3 FSLocalFlameDiffuse(float3 positionWS,half3 normalWS)
{
    if(_FSFlameCount==0)return 0;
    half3 planeNormal=normalize(cross(ddx(positionWS),ddy(positionWS)));
    planeNormal*=dot(planeNormal,normalWS)<0?-1:1;
    half3 result;
    int usedMip;bool cached=FSFlameSurface(positionWS,normalWS,result,usedMip);
    if(_FSSurfaceDebug==1)return cached?half3(0,1,0):half3(1,0,0);
    if(_FSSurfaceDebug==2)return cached?half3(usedMip/3.0,1,0):half3(1,0,0);
    if(!cached)return FSFlameDirect(positionWS,normalWS,false,planeNormal);
    // Cache contains average stationary illumination. Apply only the difference
    // for nearby flicker and moving blockers; carried lights add their full result.
    int head=FSFlameHead(positionWS);
    [loop]for(uint visit=0;head!=0 && visit<FS_FLAME_CAPACITY;visit++)
    {
        int2 link=_FSFlameLinks[head-1];uint i=link.x;head=link.y;
        if(_FSFlameColors[i].a<1.5h)continue;
        half3 unit=FSFlameUnit(i,positionWS,normalWS,planeNormal);
        if(!any(unit>0))continue;
        // A moving blocker or carried source makes this receiver use exact live
        // evaluation. Subtracting an interpolated aggregate would leave residual
        // light when a door blocks every contributor. The stored cache stays valid.
        if(_FSFlameColors[i].a>2.5h)return FSFlameDirect(positionWS,normalWS,false,planeNormal);
        half dynamicVisibility=FSFlameVisibility(i,positionWS,positionWS-_FSFlamePositions[i].xyz,planeNormal,true);
        half3 average=_FSFlameAverageColors[i].a>.5h?_FSFlameAverageColors[i].rgb:0;
        result+=unit*(_FSFlameColors[i].rgb*dynamicVisibility-average);
    }
    return max(result,0);
}
#endif
