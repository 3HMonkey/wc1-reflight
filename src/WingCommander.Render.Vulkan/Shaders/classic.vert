#version 450

// Classic layer, vertex stage: one oversized triangle that covers the whole viewport.
// The viewport is the letterbox rectangle from PresentationLayout.Compute, so the
// rasterised area is exactly the 320x200 picture; no vertex buffer is bound.
//
// Vertex 0 -> (-1,-1), 1 -> (3,-1), 2 -> (-1,3).

void main()
{
    vec2 corner = vec2(float((gl_VertexIndex << 1) & 2), float(gl_VertexIndex & 2));
    gl_Position = vec4(corner * 2.0 - 1.0, 0.0, 1.0);
}
