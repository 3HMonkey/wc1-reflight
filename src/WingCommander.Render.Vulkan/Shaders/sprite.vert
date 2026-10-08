#version 450

// Space sprites (R2), vertex stage: one 4-vertex triangle strip per instance, no vertex buffer
// except the per-instance data. Coordinates are logical 320x200 screen pixels; the viewport is
// the classic letterbox rectangle, so sprites scale and stretch exactly like the classic layer.
//
// The hot-spot pixel (X, Y) covers [X, X+1) x [Y, Y+1); scale, flip and rotation act around its
// centre (X + 0.5, Y + 0.5). Quad corners are relative to that centre:
// left = -originX - 0.5, top = -originY - 0.5, so at scale 1 frame pixel (i, j) covers screen
// pixel (X - originX + i, Y - originY + j) exactly like the software renderer.

layout(location = 0) in vec2 inPosition; // hot-spot pixel, logical screen pixels (sub-pixel allowed)
layout(location = 1) in vec4 inAxes;     // 2x2 matrix columns: image x axis (xy), image y axis (zw) on screen
layout(location = 2) in vec4 inQuad;     // left, top (relative to the hot-spot centre), width, height
layout(location = 3) in vec4 inAtlas;    // atlas x, y (texels), magnification (output px per image px), unused

layout(location = 0) out vec2 outTexel;          // position inside the frame in texels, 0..width / 0..height
layout(location = 1) flat out vec4 outAtlas;     // atlas x, y, frame width, height
layout(location = 2) flat out float outMagnification;

void main()
{
    vec2 corner = vec2(float(gl_VertexIndex & 1), float((gl_VertexIndex >> 1) & 1));
    vec2 local = inQuad.xy + corner * inQuad.zw;
    vec2 screen = inPosition + 0.5 + local.x * inAxes.xy + local.y * inAxes.zw;
    gl_Position = vec4(screen / vec2(320.0, 200.0) * 2.0 - 1.0, 0.0, 1.0);
    outTexel = corner * inQuad.zw;
    outAtlas = vec4(inAtlas.xy, inQuad.zw);
    outMagnification = inAtlas.z;
}
