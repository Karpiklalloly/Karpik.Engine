#version 450

layout(location = 0) in vec2 Position;
layout(location = 1) in vec2 TexCoord;
layout(location = 2) in vec4 Color;

layout(location = 0) out vec2 vTexCoord;
layout(location = 1) out vec4 vColor;

void main()
{
    vTexCoord = TexCoord;
    vColor = Color;
    gl_Position = vec4(Position, 0.0, 1.0);
}