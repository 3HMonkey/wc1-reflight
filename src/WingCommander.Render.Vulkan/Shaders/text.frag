#version 450

// Output-resolution text (ADR-013), fragment stage. Sampling follows the CPU reference
// Core.Rendering.GlyphRasterizer:
// - distance: bilinear over the glyph's field (texel centres at +0.5, clamped to the field) from
//   the R channel of the RG8 atlas through a LINEAR sampler; byte = 128 + d * 127 / 8, d in
//   field texels, positive inside.
// - coverage: clamp(d / texelsPerPixel + 0.5, 0, 1); texelsPerPixel = field texels per output
//   pixel, the mean of x and y (4:3 pixels are not square).
// - colour: single-colour glyphs take palette[text colour]; multicolour glyphs blend the palette
//   colours of the four nearest source pixels bilinearly at source resolution (G channel, read
//   with texelFetch at the source pixel centres; the glyph's ink index takes the text colour).
// Game text (mode 0) obeys the 320x200 text mask: instance i may colour logical pixel p only when
// mask[p] != 0 and i >= mask[p] - 1, so whatever the game drew over its text stays on top.
// Overlay items (mode 1) carry an RGBA8 colour with straight alpha: rectangles are solid fills,
// glyphs take that single colour. Colours blend in the palette's sRGB space (like classic.frag);
// output is premultiplied alpha (blend ONE, ONE_MINUS_SRC_ALPHA), painter order = instance order.

layout(set = 0, binding = 0) uniform sampler2D uAtlas;     // RG8_UNORM glyph atlas: R distance, G palette index
layout(set = 0, binding = 1) uniform usampler2D uMask;     // R16_UINT 320x200 text mask, texelFetch only
layout(set = 0, binding = 2) uniform sampler2D uPalette;   // RGBA8 256x1 live palette (shared with the classic pass)

layout(push_constant) uniform TextParams
{
    vec2 destOrigin;    // game: letterbox origin; overlay: (0, 0) (framebuffer pixels)
    vec2 destSize;      // game: letterbox size; overlay: target size (framebuffer pixels)
    vec2 space;         // size of the quads' coordinate space (vertex stage)
    uint mode;          // 0 game text, 1 overlay
    uint encodeLinear;  // 1 when the target format is *_SRGB
} pc;

layout(location = 0) in vec2 inCell;            // position inside the glyph cell, source pixels
layout(location = 1) flat in vec4 inField;      // the glyph's field in the atlas: x, y, width, height (texels)
layout(location = 2) flat in uvec4 inData;      // x colour (mode 0 palette index, mode 1 RGBA8), y list index,
                                                // z flags (1 multicolour, 2 rectangle), w the glyph's ink index

layout(location = 0) out vec4 outColour;

const vec2 kScreen = vec2(320.0, 200.0);
const float kFieldScale = 8.0;  // GlyphImage.FieldScale
const float kPadding = 4.0;     // GlyphImage.FieldPadding
const float kSpread = 8.0;      // GlyphImage.FieldSpread
const uint kMulticolour = 1u;
const uint kRectangle = 2u;

// Signed distance in field texels at a field position (texels, (0,0) = field corner).
float SampleDistance(vec2 field)
{
    vec2 p = clamp(field, vec2(0.5), inField.zw - 0.5);
    vec2 uv = (inField.xy + p) / vec2(textureSize(uAtlas, 0));
    float value = textureLod(uAtlas, uv, 0.0).r * 255.0;
    return (value - 128.0) * kSpread / 127.0;
}

float Coverage(vec2 field, float texelsPerPixel)
{
    return clamp(SampleDistance(field) / texelsPerPixel + 0.5, 0.0, 1.0);
}

vec3 PaletteColour(uint index)
{
    return texelFetch(uPalette, ivec2(int(index), 0), 0).rgb;
}

// Colour of source pixel (x, y) of the glyph, -1 <= x <= width: the field texel at its centre
// (clamped to the field); the ink index takes the text colour.
vec3 SourceColour(ivec2 source)
{
    ivec2 texel = clamp(ivec2(kPadding) + source * 8 + 4, ivec2(0), ivec2(inField.zw) - 1);
    uint index = uint(round(texelFetch(uAtlas, ivec2(inField.xy) + texel, 0).g * 255.0));
    return PaletteColour(index == inData.w ? inData.x : index);
}

// Multicolour glyphs: bilinear blend of the four nearest source pixels' colours.
vec3 BlendedColour()
{
    vec2 s = inCell - 0.5;
    vec2 base = floor(s);
    vec2 f = s - base;
    ivec2 p = ivec2(base);
    vec3 top = mix(SourceColour(p), SourceColour(p + ivec2(1, 0)), f.x);
    vec3 bottom = mix(SourceColour(p + ivec2(0, 1)), SourceColour(p + ivec2(1, 1)), f.x);
    return mix(top, bottom, f.y);
}

vec3 SrgbToLinear(vec3 c)
{
    vec3 low = c / 12.92;
    vec3 high = pow((c + 0.055) / 1.055, vec3(2.4));
    return mix(high, low, lessThanEqual(c, vec3(0.04045)));
}

void main()
{
    // Field texels per output pixel (anti-aliasing width); derivatives first, in uniform control flow.
    vec2 field = kPadding + inCell * kFieldScale;
    vec2 footprint = fwidth(field);
    float texelsPerPixel = max(0.5 * (footprint.x + footprint.y), 1e-3);

    vec3 rgb;
    float alpha;
    if (pc.mode == 0u)
    {
        vec2 logical = (gl_FragCoord.xy - pc.destOrigin) * (kScreen / pc.destSize);
        ivec2 pixel = ivec2(floor(logical));
        if (any(lessThan(pixel, ivec2(0))) || any(greaterThanEqual(pixel, ivec2(kScreen))))
            discard;
        uint mask = texelFetch(uMask, pixel, 0).r;
        if (mask == 0u || inData.y + 1u < mask)
            discard;
        alpha = Coverage(field, texelsPerPixel);
        if (alpha <= 0.0)
            discard;
        rgb = (inData.z & kMulticolour) != 0u ? BlendedColour() : PaletteColour(inData.x);
    }
    else
    {
        vec4 colour = unpackUnorm4x8(inData.x); // red in the lowest byte, straight alpha
        rgb = colour.rgb;
        alpha = (inData.z & kRectangle) != 0u ? colour.a : colour.a * Coverage(field, texelsPerPixel);
        if (alpha <= 0.0)
            discard;
    }

    if (pc.encodeLinear != 0u)
        rgb = SrgbToLinear(rgb);
    outColour = vec4(rgb * alpha, alpha);
}
