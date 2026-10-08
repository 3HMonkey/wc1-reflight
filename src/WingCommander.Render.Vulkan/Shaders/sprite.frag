#version 450

// Space sprites (R2), fragment stage.
//
// 1. Occlusion (the reference's gl_renderer.c rule): the logical screen pixel under the fragment
//    must lie inside the clip rectangle (space buffer area) and the cockpit window mask, and the
//    classic layer must still show the space background index there (0xBF). Every other classic
//    pixel - cockpit art, HUD, text, cursor, software-drawn sprites - stays on top.
// 2. Colour: sprite texels are palette indices (255 = transparent) looked up in the same live
//    palette as the classic layer, so palette flashes and fades apply. Output is premultiplied
//    alpha (blend ONE, ONE_MINUS_SRC_ALPHA); painter order is the instance order.
//
// Filters (RendererSettings.Filter): 0 nearest texel; 1 sharp (the reference's band filter,
// anti-aliased texel edges only); 2 bilinear. Transparent and outside taps count as (0,0,0,0).

layout(set = 0, binding = 0) uniform usampler2D uAtlas;    // R8_UINT sprite atlas, texelFetch only
layout(set = 0, binding = 1) uniform sampler2D uPalette;   // RGBA8 256x1 (shared with the classic pass)
layout(set = 0, binding = 2) uniform usampler2D uIndices;  // R8_UINT 320x200 classic layer
layout(set = 0, binding = 3) uniform usampler2D uMask;     // R8_UINT 320x200 window mask (non-zero = space)

layout(push_constant) uniform SpriteParams
{
    vec2 destOrigin;       // letterbox rectangle origin, framebuffer pixels
    vec2 destSize;         // letterbox rectangle size, framebuffer pixels
    vec4 clip;             // logical clip rectangle x0, y0, x1, y1 (inside: x0 <= x < x1)
    uint filterMode;       // 0 nearest, 1 sharp, 2 bilinear
    uint encodeLinear;     // 1 when the target format is *_SRGB
    uint backgroundIndex;  // classic index sprites may replace (0xBF)
    uint useMask;          // 0: no window mask (whole screen)
} pc;

layout(location = 0) in vec2 inTexel;
layout(location = 1) flat in vec4 inAtlas;
layout(location = 2) flat in float inMagnification;

layout(location = 0) out vec4 outColour;

const uint kTransparent = 255u;

vec4 Tap(ivec2 t)
{
    if (any(lessThan(t, ivec2(0))) || any(greaterThanEqual(t, ivec2(inAtlas.zw))))
        return vec4(0.0);
    uint index = texelFetch(uAtlas, ivec2(inAtlas.xy) + t, 0).r;
    if (index == kTransparent)
        return vec4(0.0);
    return vec4(texelFetch(uPalette, ivec2(int(index), 0), 0).rgb, 1.0);
}

vec3 SrgbToLinear(vec3 c)
{
    vec3 low = c / 12.92;
    vec3 high = pow((c + 0.055) / 1.055, vec3(2.4));
    return mix(high, low, lessThanEqual(c, vec3(0.04045)));
}

void main()
{
    vec2 logical = (gl_FragCoord.xy - pc.destOrigin) * (vec2(320.0, 200.0) / pc.destSize);
    ivec2 cell = ivec2(floor(logical));
    if (any(lessThan(cell, ivec2(0))) || any(greaterThanEqual(cell, ivec2(320, 200))))
        discard;
    vec2 c = vec2(cell);
    if (any(lessThan(c, pc.clip.xy)) || any(greaterThanEqual(c, pc.clip.zw)))
        discard;
    if (pc.useMask != 0u && texelFetch(uMask, cell, 0).r == 0u)
        discard;
    if (texelFetch(uIndices, cell, 0).r != pc.backgroundIndex)
        discard;

    vec4 colour;
    if (pc.filterMode == 0u)
    {
        colour = Tap(ivec2(floor(inTexel)));
    }
    else
    {
        vec2 source = inTexel - 0.5;
        vec2 base = floor(source);
        vec2 f = source - base;
        if (pc.filterMode == 1u)
        {
            // Blend only a band of 1/magnification texels around each texel edge.
            float band = min(1.0, 1.0 / max(inMagnification, 1e-4));
            f = clamp((f - 0.5) / band + 0.5, 0.0, 1.0);
        }
        ivec2 p = ivec2(base);
        vec4 top = mix(Tap(p), Tap(p + ivec2(1, 0)), f.x);
        vec4 bottom = mix(Tap(p + ivec2(0, 1)), Tap(p + ivec2(1, 1)), f.x);
        colour = mix(top, bottom, f.y);
    }
    if (colour.a <= 0.0)
        discard;

    if (pc.encodeLinear != 0u)
        colour.rgb = SrgbToLinear(colour.rgb / colour.a) * colour.a;
    outColour = colour;
}
