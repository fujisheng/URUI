// Runs the production .cginc as C++ scalar math, with only the GPU fetch boundary
// supplied by this fixture. This is a CPU contract check, not a shader/GPU test.
#include <algorithm>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <vector>

struct float2 { float x, y; float2(float x_, float y_) : x(x_), y(y_) {} };
struct float4
{
    union
    {
        struct { float r, g, b, a; };
        struct { float x, y, z, w; };
    };
    float4(float r_ = 0, float g_ = 0, float b_ = 0, float a_ = 0) : r(r_), g(g_), b(b_), a(a_) {}
};
struct int3 { int x, y, z; int3(int x_, int y_, int z_) : x(x_), y(y_), z(z_) {} };
struct SamplerState {};
float saturate(float v) { return std::clamp(v, 0.0f, 1.0f); }
int clamp(int v, int lo, int hi) { return std::clamp(v, lo, hi); }
float4 lerp(float4 a, float4 b, float t)
{
    return float4(a.r + (b.r-a.r)*t, a.g + (b.g-a.g)*t,
                  a.b + (b.b-a.b)*t, a.a + (b.a-a.a)*t);
}
float referenceDecode(float v)
{
    return v <= 0.04045f ? v / 12.92f : std::pow((v + 0.055f) / 1.055f, 2.4f);
}
struct Texture2D
{
    int width = 1, height = 1, loads = 0, samples = 0;
    bool srgb = false;
    std::vector<float4> pixels {float4(1,1,1,1)};
    float4 Load(int3 p)
    {
        if (p.x < 0 || p.x >= width || p.y < 0 || p.y >= height || p.z != 0)
        {
            std::fprintf(stderr, "FAIL: fetch must clamp coordinates and use mip zero\n");
            std::exit(1);
        }
        ++loads;
        auto value = pixels[p.y * width + p.x];
#ifndef UNITY_COLORSPACE_GAMMA
        if (srgb)
        {
            value.r = referenceDecode(value.r);
            value.g = referenceDecode(value.g);
            value.b = referenceDecode(value.b);
        }
#endif
        return value;
    }
    float4 Sample(SamplerState, float2)
    {
        ++samples;
        return float4(0.2f,0.3f,0.4f,0.5f);
    }
};

#include "../../../Packages/com.fujisheng.urui/Runtime/Resources/URUI/BorrowedTextureSampling.cginc"

int checks = 0;
void near(float actual, float expected, const char* reason, float tolerance = 0.00001f)
{
    ++checks;
    if (std::abs(actual - expected) > tolerance)
    {
        std::fprintf(stderr, "FAIL: %s: got %.9g, expected %.9g\n", reason, actual, expected);
        std::exit(1);
    }
}
void color(float4 actual, float4 expected, const char* reason)
{
    near(actual.r,expected.r,reason); near(actual.g,expected.g,reason);
    near(actual.b,expected.b,reason); near(actual.a,expected.a,reason);
}
void source(int width, int height, bool srgb, std::vector<float4> pixels)
{
    _UruiBorrowedTexture = 1;
    _UruiSourceSrgb = srgb ? 1.0f : 0.0f;
    _UruiSourceSize = {float(width),float(height),1.0f/width,1.0f/height};
    _MainTex.width = width; _MainTex.height = height; _MainTex.srgb = srgb;
    _MainTex.pixels = pixels; _MainTex.loads = _MainTex.samples = 0;
}
int main()
{
    source(1,1,false,{float4(1,0,0,0.5f)});
    _UruiBorrowedTexture = 0;
    color(UruiSampleMainTexture(float2(0,0)),float4(0.2f,0.3f,0.4f,0.5f),"owned path unchanged");
    near(float(_MainTex.loads),0,"owned path must not load borrowed texels");
    near(float(_MainTex.samples),1,"owned path uses caller-bound sampler");

    // Hidden green in a fully transparent texel must not bleed into red.
    // Post-filter premultiplication would return (0.25,0.25,0,0.5).
    source(2,1,false,{float4(1,0,0,1),float4(0,1,0,0)});
    color(UruiSampleMainTexture(float2(0.5f,0.5f)),float4(0.5f,0,0,0.5f),"premultiply before interpolation");
    color(UruiSampleMainTexture(float2(-20,0.5f)),float4(1,0,0,1),"left clamp ignores repeat");
    color(UruiSampleMainTexture(float2(20,0.5f)),float4(0,0,0,0),"right clamp ignores repeat");

    // Texture2D data has its bottom row first; RmlUi UV zero names top row.
    source(2,2,false,{float4(0,0,1,1),float4(1,1,1,1),float4(1,0,0,1),float4(0,1,0,1)});
    color(UruiSampleMainTexture(float2(0.25f,0.25f)),float4(1,0,0,1),"top-left row flip");
    color(UruiSampleMainTexture(float2(0.75f,0.75f)),float4(1,1,1,1),"bottom-right row flip");
    color(UruiSampleMainTexture(float2(0.375f,0.375f)),float4(0.625f,0.25f,0.25f,1),"four-tap bilinear weights");
    color(UruiSampleMainTexture(float2(-3,-3)),float4(1,0,0,1),"top clamp");
    color(UruiSampleMainTexture(float2(-3,3)),float4(0,0,1,1),"bottom clamp");

    // Compare every 8-bit channel with native's integer byte premultiply.
    for (int alpha : {0,1,32,127,128,254,255})
    for (int channel = 0; channel <= 255; ++channel)
    {
        source(1,1,true,{float4(channel/255.0f,channel/255.0f,channel/255.0f,alpha/255.0f)});
        auto value = UruiSampleMainTexture(float2(0.5f,0.5f));
        float expected = ((channel * alpha + 127) / 255) / 255.0f;
#ifndef UNITY_COLORSPACE_GAMMA
        expected = referenceDecode(expected);
#endif
        near(value.r,expected,"sRGB native-byte-premultiply equivalence");
        near(value.a,alpha/255.0f,"alpha remains linear");
    }

    source(2,1,true,{float4(1,0,0,128.0f/255),float4(0,1,0,0)});
#ifdef UNITY_COLORSPACE_GAMMA
    const float red = 0.250980392f;
#else
    const float red = 0.10793025f;
#endif
    color(UruiSampleMainTexture(float2(0.5f,0.5f)),float4(red,0,0,64.0f/255),"sRGB transparent edge");

    // Linear/HDR source values must not be gamma-decoded or quantized to bytes.
    source(1,1,false,{float4(2,0.2f,0.12345f,0.5f)});
    color(UruiSampleMainTexture(float2(0.5f,0.5f)),float4(1,0.1f,0.061725f,0.5f),"linear source premultiplies in linear space");
#ifdef UNITY_COLORSPACE_GAMMA
    std::printf("PASS: Gamma shader CPU math contract (%d checks)\n",checks);
#else
    std::printf("PASS: Linear shader CPU math contract (%d checks)\n",checks);
#endif
}
