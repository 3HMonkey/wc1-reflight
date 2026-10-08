#version 450

// Classic layer, fragment stage: 320x200 palette indices + 256-entry palette -> colour.
//
// The lookup happens per source texel, so filtering blends palette colours (never indices).
// Colours are blended in the palette's own (sRGB-encoded) space, like the SDL_Renderer path.
//
// Filters (RendererSettings.Filter):
//   0 Nearest        one texel, crisp, uneven at non-integer scales
//   1 SharpBilinear  analytic "integer nearest prescale + bilinear": crisp and even pixels;
//                    identical to Nearest at integer scales
//   2 Linear         plain bilinear

layout(set = 0, binding = 0) uniform usampler2D uIndices;  // R8_UINT 320x200, texelFetch only
layout(set = 0, binding = 1) uniform sampler2D uPalette;   // R8G8B8A8_UNORM 256x1, texelFetch only

layout(push_constant) uniform ClassicParams
{
    vec2 destOrigin;    // letterbox rectangle origin in framebuffer pixels
    vec2 destSize;      // letterbox rectangle size in framebuffer pixels
    vec2 prescale;      // sharp-bilinear integer prescale per axis, >= 1
    uint filterMode;    // 0 nearest, 1 sharp bilinear, 2 linear
    uint encodeLinear;  // 1 when the target format is *_SRGB (the hardware re-encodes on write)
} pc;

layout(location = 0) out vec4 outColour;

const ivec2 kSourceSize = ivec2(320, 200);

vec3 Fetch(ivec2 p)
{
    p = clamp(p, ivec2(0), kSourceSize - 1);
    uint index = texelFetch(uIndices, p, 0).r;
    return texelFetch(uPalette, ivec2(int(index), 0), 0).rgb;
}

// Bilinear blend of the four palette colours around 'texel' (source pixel units,
// texel centres at +0.5), clamped to the edge like CLAMP_TO_EDGE.
vec3 Bilinear(vec2 texel)
{
    vec2 pos = texel - 0.5;
    vec2 cell = floor(pos);
    vec2 t = pos - cell;
    ivec2 p = ivec2(cell);
    vec3 top = mix(Fetch(p), Fetch(p + ivec2(1, 0)), t.x);
    vec3 bottom = mix(Fetch(p + ivec2(0, 1)), Fetch(p + ivec2(1, 1)), t.x);
    return mix(top, bottom, t.y);
}

vec3 SrgbToLinear(vec3 c)
{
    vec3 low = c / 12.92;
    vec3 high = pow((c + 0.055) / 1.055, vec3(2.4));
    return mix(high, low, lessThanEqual(c, vec3(0.04045)));
}

void main()
{
    // Position inside the 320x200 picture, in source pixels (top-left origin like the VGA).
    vec2 texel = (gl_FragCoord.xy - pc.destOrigin) * (vec2(kSourceSize) / pc.destSize);

    vec3 colour;
    if (pc.filterMode == 0u)
    {
        colour = Fetch(ivec2(floor(texel)));
    }
    else if (pc.filterMode == 1u)
    {
        // Sample point of a nearest prescale by 'prescale' followed by bilinear: inside the
        // central part of a texel the colour is flat, only the outer 1/prescale band blends.
        vec2 cell = floor(texel);
        vec2 offset = texel - cell - 0.5;
        vec2 range = 0.5 - 0.5 / pc.prescale;
        vec2 f = (offset - clamp(offset, -range, range)) * pc.prescale + 0.5;
        colour = Bilinear(cell + f);
    }
    else
    {
        colour = Bilinear(texel);
    }

    if (pc.encodeLinear != 0u)
        colour = SrgbToLinear(colour);

    outColour = vec4(colour, 1.0);
}
