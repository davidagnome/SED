#version 450

layout(set = 0, binding = 0) uniform sampler2D matTex;    // R8: palette index
layout(set = 0, binding = 1) uniform sampler2D palette;   // 256x1 RGBA
layout(set = 0, binding = 2) uniform sampler2D lightRamp; // 256x64 R8: shaded index

layout(push_constant) uniform Push {
    mat4 mvp;
    vec2 invTexSize;
    uint mode;
    float alpha;
} pc;

layout(location = 0) in vec3 vColor;
layout(location = 1) in vec2 vUv;
layout(location = 0) out vec4 outColor;

void main()
{
    if (pc.mode == 1u) {            // flat: markers / selection / untextured
        outColor = vec4(vColor, 1.0);
        return;
    }

    int idx = int(texture(matTex, vUv).r * 255.0 + 0.5);
    if (pc.alpha < 0.999 && idx == 0) discard;   // index 0 transparency (translucent only)

    // Shade the index through the colormap light ramp, then resolve to RGB.
    int level = int(clamp(vColor.r, 0.0, 1.0) * 63.0 + 0.5);
    int shaded = int(texture(lightRamp, vec2((float(idx) + 0.5) / 256.0,
                                             (float(level) + 0.5) / 64.0)).r * 255.0 + 0.5);
    vec3 rgb = texture(palette, vec2((float(shaded) + 0.5) / 256.0, 0.5)).rgb;
    outColor = vec4(rgb, pc.alpha);
}
