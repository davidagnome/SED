#version 450

layout(location = 0) in vec3 inPos;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in vec3 inColor;
layout(location = 3) in vec2 inUv;

layout(push_constant) uniform Push {
    mat4 mvp;          // 0
    vec4 camPos;       // 64  (xyz)
    vec4 sky;          // 80  (x=ceilZ, y=offX, z=offY)
    vec2 invTexSize;   // 96
    uint mode;         // 104 (0=indexed+lit, 1=flat)
    float alpha;       // 108
    float brightness;  // 112
    uint skyMode;      // 116 (0=normal, 1=ceiling, 2=horizon)
} pc;

layout(location = 0) out vec3 vColor;
layout(location = 1) out vec2 vUv;
layout(location = 2) out vec3 vWorld;

void main()
{
    gl_Position = pc.mvp * vec4(inPos, 1.0);
    vColor = inColor;
    vUv = inUv * pc.invTexSize;
    vWorld = inPos;   // geometry is already world-space
}
