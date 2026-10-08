#version 450

// Output-resolution text (ADR-013), vertex stage: one 4-vertex triangle strip per instance, no
// vertex buffer except the per-instance data. Two modes share the shaders (push constant 'mode'):
//   0 game text: quads in logical 320x200 screen pixels; the viewport is the classic letterbox
//     rectangle, so glyph cells stretch exactly like the classic layer (4:3 included).
//   1 overlay (key help): quads in render-target pixels; the viewport is the whole target.
// A glyph quad covers the glyph cell (source pixels (0,0)..(width,height) of the GlyphImage);
// the fragment stage receives the position inside the cell. Rectangles ignore the cell.

layout(push_constant) uniform TextParams
{
    vec2 destOrigin;    // game: letterbox origin; overlay: (0, 0) (framebuffer pixels)
    vec2 destSize;      // game: letterbox size; overlay: target size (framebuffer pixels)
    vec2 space;         // size of the quads' coordinate space: (320, 200) or the target size
    uint mode;          // 0 game text, 1 overlay
    uint encodeLinear;  // 1 when the target format is *_SRGB
} pc;

layout(location = 0) in vec4 inQuad;    // x, y, width, height in pc.space units
layout(location = 1) in vec4 inField;   // the glyph's distance field in the atlas: x, y, width, height (texels)
layout(location = 2) in uvec4 inData;   // colour, list index, flags, ink index (see text.frag)

layout(location = 0) out vec2 outCell;          // position inside the glyph cell, source pixels
layout(location = 1) flat out vec4 outField;
layout(location = 2) flat out uvec4 outData;

const float kFieldScale = 8.0;  // GlyphImage.FieldScale: texels per source pixel
const float kPadding = 4.0;     // GlyphImage.FieldPadding: texels around the cell

void main()
{
    vec2 corner = vec2(float(gl_VertexIndex & 1), float((gl_VertexIndex >> 1) & 1));
    vec2 position = inQuad.xy + corner * inQuad.zw;
    gl_Position = vec4(position / pc.space * 2.0 - 1.0, 0.0, 1.0);
    outCell = corner * ((inField.zw - 2.0 * kPadding) / kFieldScale);
    outField = inField;
    outData = inData;
}
