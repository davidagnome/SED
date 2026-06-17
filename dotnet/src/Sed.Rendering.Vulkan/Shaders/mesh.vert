#version 450

layout(location = 0) in vec3 inPos;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in vec3 inColor;
layout(location = 3) in vec2 inUv;

layout(push_constant) uniform Push {
    mat4 mvp;
} pc;

layout(location = 0) out vec3 vColor;
layout(location = 1) out vec2 vUv;

void main()
{
    gl_Position = pc.mvp * vec4(inPos, 1.0);
    vColor = inColor;
    vUv = inUv;
}
