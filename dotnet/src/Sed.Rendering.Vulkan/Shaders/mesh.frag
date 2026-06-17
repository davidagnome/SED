#version 450

layout(location = 0) in vec3 vColor;
layout(location = 1) in vec3 vNormal;
layout(location = 0) out vec4 outColor;

void main()
{
    vec3 n = normalize(vNormal);
    vec3 lightDir = normalize(vec3(0.35, 0.5, 0.8));
    // Two-sided lambert so face winding does not affect shading.
    float diff = abs(dot(n, lightDir));
    float ambient = 0.28;
    vec3 lit = vColor * (ambient + diff * 0.8);
    outColor = vec4(lit, 1.0);
}
