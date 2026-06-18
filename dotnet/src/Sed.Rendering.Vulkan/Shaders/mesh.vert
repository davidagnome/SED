#version 450

layout(location = 0) in vec3 inPos;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in vec3 inColor;   // mode 0: light intensity (grey); mode 1: flat color
layout(location = 3) in vec2 inUv;

layout(push_constant) uniform Push {
    mat4 mvp;
    vec2 invTexSize;   // 1/texW, 1/texH
    uint mode;         // 0 = indexed+lit, 1 = flat color
    float alpha;
} pc;

layout(location = 0) out vec3 vColor;
layout(location = 1) out vec2 vUv;

void main()
{
    gl_Position = pc.mvp * vec4(inPos, 1.0);
    vColor = inColor;
    vUv = inUv * pc.invTexSize;
}
