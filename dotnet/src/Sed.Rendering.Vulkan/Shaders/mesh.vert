#version 450

layout(location = 0) in vec3 inPos;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in vec3 inColor;
layout(location = 3) in vec2 inUv;

layout(push_constant) uniform Push {
    mat4 mvp;          // 0
    vec4 camPos;       // 64
    vec4 sky;          // 80  ceiling: (ceilZ, offX, offY, _)
    vec4 camAngles;    // 96  (yawDeg, pitchDeg, rollDeg, _)
    vec4 horizScreen;  // 112 (horizOffX, horizOffY, width, height)
    vec2 invTexSize;   // 128
    uint mode;         // 136
    float alpha;       // 140
    float brightness;  // 144
    uint skyMode;      // 148
} pc;

layout(location = 0) out vec3 vColor;
layout(location = 1) out vec2 vUv;
layout(location = 2) out vec3 vWorld;

void main()
{
    gl_Position = pc.mvp * vec4(inPos, 1.0);
    vColor = inColor;
    vUv = inUv * pc.invTexSize;
    vWorld = inPos;
}
