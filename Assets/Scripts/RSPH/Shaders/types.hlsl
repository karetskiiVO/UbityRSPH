#ifndef RSPH_TYPES_INCLUDED
#define RSPH_TYPES_INCLUDED

// Two 16-byte fields, shared by compute, rendering and C# diagnostics.
struct Particle
{
    float4 position;
    float4 velocity;
};

#endif
