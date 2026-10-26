
#ifndef RSPH_UTILS_INCLUDED
#define RSPH_UTILS_INCLUDED

static const float SPH_PI = 3.14159265359;

float Poly6(float distanceSquared, float h)
{
    float q = saturate(1.0 - distanceSquared / (h * h));
    return (315.0 / (64.0 * SPH_PI * h * h * h)) * q * q * q;
}

float SpikyGradientScale(float distance, float h)
{
    float q = max(h - distance, 0.0);
    return -45.0 * q * q / (SPH_PI * h * h * h * h * h * h);
}

float ViscosityLaplacian(float distance, float h)
{
    return 45.0 * max(h - distance, 0.0) / (SPH_PI * h * h * h * h * h * h);
}

#endif

