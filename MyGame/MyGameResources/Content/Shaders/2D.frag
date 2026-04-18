#version 450

layout(location = 0) in vec2 vTexCoord;
layout(location = 1) in vec4 vColor;

layout(location = 0) out vec4 fragColor;

layout(set = 0, binding = 0) uniform sampler2D TexSampler;

void main()
{
    fragColor = vColor;
}