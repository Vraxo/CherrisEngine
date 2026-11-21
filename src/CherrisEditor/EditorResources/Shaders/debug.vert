#version 410 core
layout (location = 0) in vec3 aPosition;
layout (location = 1) in vec3 aColor;

uniform mat4 view;
uniform mat4 projection;

out vec3 f_color;

void main()
{
    gl_Position = projection * view * vec4(aPosition, 1.0);
    f_color = aColor;
}