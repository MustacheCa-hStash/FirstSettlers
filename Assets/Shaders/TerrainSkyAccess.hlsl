#ifndef FIRST_SETTLERS_TERRAIN_SKY_ACCESS_INCLUDED
#define FIRST_SETTLERS_TERRAIN_SKY_ACCESS_INCLUDED
// Matches TerrainSkyAccess: 2 m lattice, 16-cell tiles, 128 atlas slots.
TEXTURE2D(_TerrainSkyData); SAMPLER(sampler_TerrainSkyData);
TEXTURE2D(_TerrainSkyVisibility);
TEXTURE2D(_TerrainSkyHeight); SAMPLER(sampler_TerrainSkyHeight);
TEXTURE2D(_TerrainSkyTable);
float _TerrainSkyEnabled;
float4 _TerrainSkyOrigin, _TerrainSkyControls;
float _TerrainSkyTimes[128];

float TerrainSkyExposure(float3 positionWS)
{
    if (_TerrainSkyEnabled < .5) return 1;
    float3 tile=floor(positionWS/32), key=tile-_TerrainSkyOrigin.xyz;
    if(any(key<0)||any(key>=float3(16,8,16)))return 1;
    float index=round(SAMPLE_TEXTURE2D_LOD(_TerrainSkyTable,sampler_TerrainSkyData,(float2(key.x,key.y+8*key.z)+.5)/float2(16,128),0).r*255)-1;
    if(index<0)return 1;
    float2 slot=float2(fmod(index,16),floor(index/16));
    float3 grid=clamp((positionWS-tile*32)/2,0,15.9999);
    float2 heightUV=(slot*17+grid.xz+.5)/float2(272,136);
    float2 encoded=SAMPLE_TEXTURE2D_LOD(_TerrainSkyHeight,sampler_TerrainSkyHeight,heightUV,0).rg;
    float height=dot(encoded,float2(255,65280))/65535*256-128+tile.y*32;
    // Exterior skin remains unchanged. Below the baseline, natural-air probes above
    // an intact roof cannot illuminate the cave through interpolation.
    if(positionWS.y>=height-.025)return 1;
    float3 cell=floor(grid),fraction=grid-cell;
    float blend=_TerrainSkyControls.z<=0?1:saturate((_TerrainSkyControls.w-_TerrainSkyTimes[(int)index])/_TerrainSkyControls.z);
    float total=0,exposure=0;
    [unroll]for(int z=0;z<2;z++)[unroll]for(int y=0;y<2;y++)[unroll]for(int x=0;x<2;x++)
    {
        float3 corner=cell+float3(x,y,z),delta=(grid-corner)*2;
        float2 uv=(slot*float2(17,289)+float2(corner.x,corner.y+17*corner.z)+.5)/float2(272,2312);
        float4 data=SAMPLE_TEXTURE2D_LOD(_TerrainSkyData,sampler_TerrainSkyData,uv,0);
        float4 visible=SAMPLE_TEXTURE2D_LOD(_TerrainSkyVisibility,sampler_TerrainSkyData,uv,0)*4;
        float2 hUV=(slot*17+corner.xz+.5)/float2(272,136);
        float2 h=SAMPLE_TEXTURE2D_LOD(_TerrainSkyHeight,sampler_TerrainSkyHeight,hUV,0).rg;
        float cornerHeight=dot(h,float2(255,65280))/65535*256-128+tile.y*32;
        float3 negative=float3(visible.r,data.b*4,visible.b),positive=float3(visible.g,data.a*4,visible.a);
        // Six cached free-space distances reject a bright probe on the far side of
        // a wall/floor/ceiling. This is a bounded approximation, not a pixel ray march.
        float3 clearance=min(delta+negative,positive-delta);
        float visibleWeight=saturate((min(clearance.x,min(clearance.y,clearance.z))+.035)/.1);
        float3 weights=lerp(1-fraction,fraction,float3(x,y,z));
        float weight=weights.x*weights.y*weights.z*visibleWeight;
        if(dot(negative+positive,1)<.001||tile.y*32+corner.y*2>=cornerHeight-.025)weight=0;
        exposure+=lerp(data.r,data.g,blend)*weight;total+=weight;
    }
    return total>.0001?saturate(exposure/total):0;
}
half TerrainCaveAmbient(float3 positionWS,float3 normalWS)
{
    float access=TerrainSkyExposure(positionWS+normalWS*.15);
    return lerp(saturate(_TerrainSkyControls.x),1,pow(access,max(.25,_TerrainSkyControls.y)));
}
#endif
