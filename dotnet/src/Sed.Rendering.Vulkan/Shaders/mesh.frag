#version 450

layout(set = 0, binding = 0) uniform sampler2D matTex;    // R8 palette index
layout(set = 0, binding = 1) uniform sampler2D palette;   // 256x1 RGBA
layout(set = 0, binding = 2) uniform sampler2D lightRamp; // 256x64 R8

layout(push_constant) uniform Push {
    mat4 mvp;
    vec4 camPos;
    vec4 sky;
    vec4 camAngles;
    vec4 horizScreen;
    vec2 invTexSize;
    uint mode;
    float alpha;
    float brightness;
    uint skyMode;
} pc;

layout(location = 0) in vec3 vColor;
layout(location = 1) in vec2 vUv;
layout(location = 2) in vec3 vWorld;
layout(location = 0) out vec4 outColor;

const float PI = 3.14159265359;

void main()
{
    if (pc.mode == 1u) { outColor = vec4(vColor, 1.0); return; }   // flat: markers/selection

    vec2 uv = vUv;
    float light = clamp(vColor.r, 0.0, 1.0);

    if (pc.skyMode == 1u) {              // ceiling sky: project view ray onto ceiling plane
        vec3 dir = normalize(vWorld - pc.camPos.xyz);
        float t = dir.z != 0.0 ? (pc.sky.x - pc.camPos.z) / dir.z : 0.0;
        vec3 hit = pc.camPos.xyz + dir * max(t, 0.0);
        uv = vec2((hit.x + pc.sky.y) * 16.0, (hit.y + pc.sky.z) * 16.0) * pc.invTexSize;
        light = 1.0;
    } else if (pc.skyMode == 2u) {      // horizon sky: screen-space, scrolls with view
        vec2 c = gl_FragCoord.xy - pc.horizScreen.zw * 0.5;
        float nx = c.x / pc.horizScreen.z;
        float ny = -c.y / pc.horizScreen.w;
        float roll = radians(pc.camAngles.z);
        float rc = cos(roll), rs = sin(roll);
        uv = vec2(nx * rc - ny * rs + pc.camAngles.x / 360.0 + pc.horizScreen.x,
                  ny * rc + nx * rs + pc.camAngles.y / 360.0 + pc.horizScreen.y);
        light = 1.0;
    }

    int idx = int(texture(matTex, uv).r * 255.0 + 0.5);
    if (pc.alpha < 0.999 && idx == 0) discard;

    float li = mix(light, 1.0, pc.brightness);
    int level = int(li * 63.0 + 0.5);
    int shaded = int(texture(lightRamp, vec2((float(idx) + 0.5) / 256.0,
                                             (float(level) + 0.5) / 64.0)).r * 255.0 + 0.5);
    vec3 rgb = texture(palette, vec2((float(shaded) + 0.5) / 256.0, 0.5)).rgb;
    outColor = vec4(rgb, pc.alpha);
}
