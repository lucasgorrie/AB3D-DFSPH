

struct Particle
{
    float3 position; uint domainId;
    float3 velocity; uint materialId;
};

RWStructuredBuffer<Particle> Particles : register(u0);

cbuffer SimCB : register(b0)
{
    float3 gravity; float dt;
    float3 boxMin;  float restitution;
    float3 boxMax;  uint  particleCount;
    uint   offset;  uint3 _pad;
};

[numthreads(256, 1, 1)]
void CSIntegrate(uint3 ThreadID : SV_DispatchThreadID)
{
    uint i = ThreadID.x;
    if (i >= particleCount) return;

    uint idx = offset + i;
    Particle p = Particles[idx];

    p.velocity += gravity * dt;
    p.position += p.velocity * dt;

    if (p.position.x < boxMin.x) { p.position.x = boxMin.x; p.velocity.x = -p.velocity.x * restitution; }
    if (p.position.x > boxMax.x) { p.position.x = boxMax.x; p.velocity.x = -p.velocity.x * restitution; }
    if (p.position.y < boxMin.y) { p.position.y = boxMin.y; p.velocity.y = -p.velocity.y * restitution; }
    if (p.position.y > boxMax.y) { p.position.y = boxMax.y; p.velocity.y = -p.velocity.y * restitution; }
    if (p.position.z < boxMin.z) { p.position.z = boxMin.z; p.velocity.z = -p.velocity.z * restitution; }
    if (p.position.z > boxMax.z) { p.position.z = boxMax.z; p.velocity.z = -p.velocity.z * restitution; }

    Particles[idx] = p;
}
