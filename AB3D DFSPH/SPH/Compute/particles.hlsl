#define THREADS 256
#define SCAN_BLOCK (THREADS * 2)
groupshared uint gTemp[SCAN_BLOCK];


struct Particle
{
    float3 position; uint domainId;
    float3 velocity; uint materialId;
    float density; uint groupId;
    float densityAdv;
};

struct DomainBound 
{
    float4 mn; 
    float4 mx; 
};

cbuffer GridCB : register(b0)
{
    float cellSize; uint tableSize;
    uint totalCount; uint numBlocks;

    float mass; float kernelK;
    float restDensity; uint gridPadding;
};

cbuffer SolverCB : register(b1)
{
    float3 gravity; float dt;

    float invH; float invH2;
    float restitution; float solverPad;
};

RWStructuredBuffer<Particle> Particles : register(u0);

StructuredBuffer<DomainBound> DomainBounds : register(t0);

RWStructuredBuffer<uint> CellCount : register(u1);
RWStructuredBuffer<uint> CellStart : register(u2);
RWStructuredBuffer<uint> BlockSums : register(u3);
RWStructuredBuffer<uint> CellIndex : register(u4);
RWStructuredBuffer<uint> LocalOffset : register(u5);
RWStructuredBuffer<uint> SortedIndices : register(u6);
RWStructuredBuffer<float> Factor : register(u7);

#pragma region Helpers

int3 CellOf(float3 pos)
{
    return (int3)floor(pos / cellSize);
}

uint Hash(int3 c)
{
    return (uint)((c.x * 73856093) ^ (c.y * 19349663) ^ (c.z * 83492791));
}

uint Bucket(float3 pos)
{
    return Hash(CellOf(pos)) & (tableSize - 1);
}

float W(float r)
{
    float q = r / cellSize;
    if (q >= 1.0) return 0.0;

    float f = (q <= 0.5) ? (6.0 * (q * q * q - q * q) + 1.0) : (2.0 * (1.0 - q) * (1.0 - q) * (1.0 - q));

    return kernelK * f;
}

float3 gradW(float3 d)
{
    float r = length(d);
    if (r < 1e-9) return float3(0, 0, 0);

    float q = r / cellSize;
    if (q >= 1.0) return float3(0, 0, 0);

    float l = 6.0 * kernelK;
    float g = (q <= 0.5) ? (q * (3.0 * q - 2.0)) : (-(1.0 - q) * (1.0 - q));

    return (l / cellSize) * g * (d / r);
}

#pragma endregion

#pragma region Kernels

[numthreads(THREADS, 1, 1)]
void CSApplyForces(uint3 ThreadID : SV_DispatchThreadID)
{
    uint i = ThreadID.x; if (i >= totalCount) return;
    Particles[i].velocity += gravity * dt;
}

[numthreads(THREADS, 1, 1)]
void CSIntegratePos(uint3 ThreadID : SV_DispatchThreadID)
{
    uint i = ThreadID.x; if (i >= totalCount) return;

    Particle p = Particles[i];
    p.position += p.velocity * dt;

    DomainBound box = DomainBounds[p.domainId];
    float3 mn = box.mn.xyz, mx = box.mx.xyz;
    if (p.position.x < mn.x) { p.position.x = mn.x; p.velocity.x = -p.velocity.x * restitution; }
    if (p.position.x > mx.x) { p.position.x = mx.x; p.velocity.x = -p.velocity.x * restitution; }
    if (p.position.y < mn.y) { p.position.y = mn.y; p.velocity.y = -p.velocity.y * restitution; }
    if (p.position.y > mx.y) { p.position.y = mx.y; p.velocity.y = -p.velocity.y * restitution; }
    if (p.position.z < mn.z) { p.position.z = mn.z; p.velocity.z = -p.velocity.z * restitution; }
    if (p.position.z > mx.z) { p.position.z = mx.z; p.velocity.z = -p.velocity.z * restitution; }

    Particles[i] = p;
}

[numthreads(THREADS, 1, 1)]
void CSDensityAdv(uint3 ThreadID : SV_DispatchThreadID)  // constant-density
{
    uint i = ThreadID.x; 
    if (i >= totalCount) return;

    float3 xi = Particles[i].position; 
    float3 vi = Particles[i].velocity;

    uint gi = Particles[i].groupId; 
    int3 ci = CellOf(xi);

    float delta = 0.0;
    for (int dz = -1; dz <= 1; ++dz) for (int dy = -1; dy <= 1; ++dy) for (int dx = -1; dx <= 1; ++dx) {
        int3 cc = ci + int3(dx, dy, dz); uint b = Hash(cc) & (tableSize - 1);
        uint start = CellStart[b], cnt = CellCount[b];

        for (uint k = 0; k < cnt; ++k) {
            uint j = SortedIndices[start + k]; if (j == i) continue;
            float3 xj = Particles[j].position;

            if (any(CellOf(xj) != cc)) continue;
            if (Particles[j].groupId != gi) continue;

            delta += mass * dot(vi - Particles[j].velocity, gradW(xi - xj));
        }
    }
    Particles[i].densityAdv = max((Particles[i].density + dt * delta) / restDensity, 1.0);
}

[numthreads(THREADS, 1, 1)]
void CSPressureCorrect(uint3 ThreadID : SV_DispatchThreadID)
{
    uint i = ThreadID.x;
    if (i >= totalCount) return;

    float3 xi = Particles[i].position; 
    uint gi = Particles[i].groupId; 

    int3 ci = CellOf(xi);
    float ki = (Particles[i].densityAdv - 1.0) * Factor[i] * invH2;

    float3 vsum = float3(0, 0, 0);
    for (int dz = -1; dz <= 1; ++dz) for (int dy = -1; dy <= 1; ++dy) for (int dx = -1; dx <= 1; ++dx) {
        int3 cc = ci + int3(dx, dy, dz); uint b = Hash(cc) & (tableSize - 1);
        uint start = CellStart[b], cnt = CellCount[b];

        for (uint k = 0; k < cnt; ++k) {
            uint j = SortedIndices[start + k];
            if (j == i) continue;

            float3 xj = Particles[j].position;
            if (any(CellOf(xj) != cc)) continue;
            if (Particles[j].groupId != gi) continue;

            float kj = (Particles[j].densityAdv - 1.0) * Factor[j] * invH2;
            vsum += (ki + kj) * mass * gradW(xi - xj);
        }
    }
    Particles[i].velocity -= dt * vsum;
}

[numthreads(THREADS, 1, 1)]
void CSDivergenceAdv(uint3 ThreadID : SV_DispatchThreadID)
{
    uint i = ThreadID.x;
    if (i >= totalCount) return;

    float3 xi = Particles[i].position; 
    float3 vi = Particles[i].velocity;

    uint gi = Particles[i].groupId; 
    int3 ci = CellOf(xi);

    float delta = 0.0;
    for (int dz = -1; dz <= 1; ++dz) for (int dy = -1; dy <= 1; ++dy) for (int dx = -1; dx <= 1; ++dx) {
        int3 cc = ci + int3(dx, dy, dz); uint b = Hash(cc) & (tableSize - 1);
        uint start = CellStart[b], cnt = CellCount[b];
        for (uint k = 0; k < cnt; ++k) {
            uint j = SortedIndices[start + k]; 
            if (j == i) continue;

            float3 xj = Particles[j].position;
            if (any(CellOf(xj) != cc)) continue;
            if (Particles[j].groupId != gi) continue;

            delta += mass * dot(vi - Particles[j].velocity, gradW(xi - xj));
        }
    }

    // Only resolve divergence in compressed regions. No boundary particles
    Particles[i].densityAdv = (Particles[i].density < restDensity) ? 0.0 : (delta / restDensity);
}

[numthreads(THREADS, 1, 1)]
void CSDivergenceCorrect(uint3 ThreadID : SV_DispatchThreadID)
{
    uint i = ThreadID.x;
    if (i >= totalCount) return;

    float3 xi = Particles[i].position; 
    uint gi = Particles[i].groupId;

    int3 ci = CellOf(xi);
    float ki = Particles[i].densityAdv * Factor[i] * invH;  // note invH, not invH2

    float3 vsum = float3(0, 0, 0);
    for (int dz = -1; dz <= 1; ++dz) for (int dy = -1; dy <= 1; ++dy) for (int dx = -1; dx <= 1; ++dx) {
        int3 cc = ci + int3(dx, dy, dz); uint b = Hash(cc) & (tableSize - 1);
        uint start = CellStart[b], cnt = CellCount[b];

        for (uint k = 0; k < cnt; ++k) {
            uint j = SortedIndices[start + k];
            if (j == i) continue;

            float3 xj = Particles[j].position;
            if (any(CellOf(xj) != cc)) continue;
            if (Particles[j].groupId != gi) continue;

            float kj = Particles[j].densityAdv * Factor[j] * invH;
            vsum += (ki + kj) * mass * gradW(xi - xj);
        }
    }
    Particles[i].velocity -= dt * vsum;
}

[numthreads(THREADS, 1, 1)]
void CSComputeFactor(uint3 ThreadID : SV_DispatchThreadID)
{
    uint i = ThreadID.x;
    if (i >= totalCount) return;

    float3 xi = Particles[i].position;
    uint   gi = Particles[i].groupId;
    int3   ci = CellOf(xi);

    float3 sumGrad = float3(0, 0, 0);
    float  sumSq = 0.0;

    for (int dz = -1; dz <= 1; ++dz)
        for (int dy = -1; dy <= 1; ++dy)
            for (int dx = -1; dx <= 1; ++dx)
            {
                int3 cc = ci + int3(dx, dy, dz);
                uint b = Hash(cc) & (tableSize - 1);
                uint start = CellStart[b], cnt = CellCount[b];
                for (uint k = 0; k < cnt; ++k)
                {
                    uint j = SortedIndices[start + k];
                    if (j == i) continue;  // Don't count self

                    float3 xj = Particles[j].position;
                    if (any(CellOf(xj) != cc)) continue;
                    if (Particles[j].groupId != gi) continue;

                    float3 mGrad = mass * gradW(xi - xj);
                    sumGrad += mGrad;
                    sumSq += dot(mGrad, mGrad);
                }
            }

    float denom = dot(sumGrad, sumGrad) + sumSq;
    Factor[i] = (denom > 1e-12) ? (-1.0 / denom) : 0.0;
}

[numthreads(THREADS, 1, 1)]
void CSDensity(uint3 ThreadID : SV_DispatchThreadID)
{
    uint i = ThreadID.x;
    if (i >= totalCount) return;

    float3 xi = Particles[i].position;
    uint   gi = Particles[i].groupId;
    int3   ci = CellOf(xi);

    float rho = 0.0;
    for (int dz = -1; dz <= 1; ++dz)
        for (int dy = -1; dy <= 1; ++dy)
            for (int dx = -1; dx <= 1; ++dx)
            {
                int3 cc = ci + int3(dx, dy, dz);
                uint b = Hash(cc) & (tableSize - 1);
                uint start = CellStart[b];
                uint cnt = CellCount[b];
                for (uint k = 0; k < cnt; ++k)
                {
                    uint   j = SortedIndices[start + k];
                    float3 xj = Particles[j].position;
                    if (any(CellOf(xj) != cc)) continue;       // reject strays
                    if (Particles[j].groupId != gi) continue;  // group mask

                    float r = length(xi - xj);
                    if (r < cellSize) rho += mass * W(r);
                }
            }

    Particles[i].density = rho;
}

[numthreads(THREADS, 1, 1)]
void CSCount(uint3 ThreadID : SV_DispatchThreadID)
{
    uint i = ThreadID.x;
    if (i >= totalCount) return;

    uint b = Bucket(Particles[i].position);

    uint slot;
    InterlockedAdd(CellCount[b], 1, slot);
    CellIndex[i] = b;
    LocalOffset[i] = slot;
}

[numthreads(THREADS, 1, 1)]
void CSScanBlocks(uint3 Gid : SV_GroupID, uint3 GTid : SV_GroupThreadID)
{
    uint id = GTid.x;

    uint block = Gid.x;
    uint n = SCAN_BLOCK;
    uint offset = 1;

    uint g0 = block * n + 2 * id;
    uint g1 = block * n + 2 * id + 1;

    gTemp[2 * id] = (g0 < tableSize) ? CellCount[g0] : 0;
    gTemp[2 * id + 1] = (g1 < tableSize) ? CellCount[g1] : 0;

    for (uint d = n >> 1; d > 0; d >>= 1)  // up-sweep
    {
        GroupMemoryBarrierWithGroupSync();
        if (id < d)
        {
            uint ai = offset * (2 * id + 1) - 1;
            uint bi = offset * (2 * id + 2) - 1;
            gTemp[bi] += gTemp[ai];
        }
        offset *= 2;
    }

    if (id == 0)
    { 
        BlockSums[block] = gTemp[n - 1]; 
        gTemp[n - 1] = 0; 
    }

    for (uint d = 1; d < n; d *= 2)  // down-sweep
    {
        offset >>= 1;
        GroupMemoryBarrierWithGroupSync();
        if (id < d)
        {
            uint ai = offset * (2 * id + 1) - 1;
            uint bi = offset * (2 * id + 2) - 1;
            uint t = gTemp[ai];
            gTemp[ai] = gTemp[bi];
            gTemp[bi] += t;
        }
    }
    GroupMemoryBarrierWithGroupSync();

    if (g0 < tableSize) CellStart[g0] = gTemp[2 * id];
    if (g1 < tableSize) CellStart[g1] = gTemp[2 * id + 1];
}

[numthreads(THREADS, 1, 1)]
void CSScanBlockSums(uint3 GTid : SV_GroupThreadID)
{
    uint id = GTid.x, n = SCAN_BLOCK, offset = 1;

    gTemp[2 * id] = (2 * id < numBlocks) ? BlockSums[2 * id] : 0;
    gTemp[2 * id + 1] = (2 * id + 1 < numBlocks) ? BlockSums[2 * id + 1] : 0;

    for (uint d = n >> 1; d > 0; d >>= 1)
    {
        GroupMemoryBarrierWithGroupSync();
        if (id < d) { uint ai = offset * (2 * id + 1) - 1, bi = offset * (2 * id + 2) - 1; gTemp[bi] += gTemp[ai]; }
        offset *= 2;
    }

    if (id == 0) gTemp[n - 1] = 0;

    for (uint d = 1; d < n; d *= 2)
    {
        offset >>= 1;
        GroupMemoryBarrierWithGroupSync();
        if (id < d) { uint ai = offset * (2 * id + 1) - 1, bi = offset * (2 * id + 2) - 1; uint t = gTemp[ai]; gTemp[ai] = gTemp[bi]; gTemp[bi] += t; }
    }

    GroupMemoryBarrierWithGroupSync();

    if (2 * id < numBlocks) BlockSums[2 * id] = gTemp[2 * id];
    if (2 * id + 1 < numBlocks) BlockSums[2 * id + 1] = gTemp[2 * id + 1];
}

[numthreads(THREADS, 1, 1)]
void CSAddOffsets(uint3 Gid : SV_GroupID, uint3 GTid : SV_GroupThreadID)
{
    uint id = GTid.x, block = Gid.x, off = BlockSums[block];

    uint g0 = block * SCAN_BLOCK + 2 * id;
    uint g1 = block * SCAN_BLOCK + 2 * id + 1;

    if (g0 < tableSize) CellStart[g0] += off;
    if (g1 < tableSize) CellStart[g1] += off;
}

[numthreads(THREADS, 1, 1)]
void CSScatter(uint3 ThreadID : SV_DispatchThreadID)
{
    uint i = ThreadID.x;
    if (i >= totalCount) return;

    uint b = CellIndex[i];
    uint slot = CellStart[b] + LocalOffset[i];
    SortedIndices[slot] = i;
}

#pragma endregion
