#include "shared/hash-functions.hlsl"
#include "shared/noise-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

cbuffer IntParams : register(b0)
{
    int2 TexSize;
    uint BufferLength;
    int FrameIndex;
    int TotalSteps;
}

cbuffer Transforms : register(b1)
{
    float4x4 CameraToClipSpace;
    float4x4 ClipSpaceToCamera;
    float4x4 WorldToCamera;
    float4x4 CameraToWorld;
    float4x4 WorldToClipSpace;
    float4x4 ClipSpaceToWorld;
    float4x4 ObjectToWorld;
    float4x4 WorldToObject;
    float4x4 ObjectToCamera;
    float4x4 ObjectToClipSpace;
};

RWTexture2D<float4> ColorOutput  : register(u0); 
RWStructuredBuffer<uint> IndexBuffer : u1;
StructuredBuffer<Point> SourcePoints : t0;


float c2k(Point c){
    float3 p=c.Position.xyz;
    if(isnan(c.Scale.x)){return -1;}
    // return -mul(float4(c.Position.xyz,1),WorldToCamera).z;
    return length(c.Position.xyz-CameraToWorld[3].xyz);
}
#define linstep(a,b,x) saturate((x-a)/(b-a))

[numthreads(32,32,1)]
void main(uint3 DTid : SV_DispatchThreadID)
{   
    uint width, height;
    ColorOutput.GetDimensions(width, height);
    if(DTid.x >= width || DTid.y >= height)
         return;

    uint idx=DTid.x+DTid.y*TexSize.x;
    idx=idx/((TexSize.x*TexSize.y)/BufferLength);
    float4 c=1;
    float2 uv=float2(DTid.xy)/TexSize;
    c.xy=uv;
    c.z=0.0;
    c.a=1.0;
    float progress=float(FrameIndex)/TotalSteps;
    if(idx<BufferLength){
        
        uint sid=IndexBuffer[idx].x;

        Particle p=SourcePoints[sid];
        // ColorOutput[DTid.xy]=p.Color;
        float smp=0.15;
        smp=c2k(p);
        // smp/=c2k(SourcePoints[IndexBuffer[0]]);
        smp=linstep(c2k(SourcePoints[IndexBuffer[BufferLength-1].x]),c2k(SourcePoints[IndexBuffer[0].x]),smp);
        ColorOutput[DTid.xy]=float4(smp.xxx,1)+0*float4(0,0,(uv.y>.95)*(uv.x<progress),0);  

    }else{
        ColorOutput[DTid.xy]=c;   
    }
}
