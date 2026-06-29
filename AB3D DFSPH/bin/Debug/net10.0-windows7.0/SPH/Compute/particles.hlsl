RWStructuredBuffer<float> Output : register(u0);


[numthreads(64, 1, 1)]
void CSMain(uint3 id : SV_DispatchThreadID)
{
    Output[id.x] = id.x * 2.0;
}
