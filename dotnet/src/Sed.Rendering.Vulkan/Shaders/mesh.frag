#version 450

layout(set = 0, binding = 0) uniform sampler2D tex;

layout(location = 0) in vec3 vColor;
layout(location = 1) in vec2 vUv;
layout(location = 0) out vec4 outColor;

void main()
{
    vec4 t = texture(tex, vUv);
    if (t.a < 0.5) discard;                 // palette-index-0 transparency cutout
    outColor = vec4(t.rgb * vColor, 1.0);   // modulate texture by per-vertex light
}
