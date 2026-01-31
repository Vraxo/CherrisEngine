#version 330 core
layout (location = 0) in vec3 aPosition;

uniform mat4 view;
uniform mat4 projection;
uniform vec3 uCameraPos;

out vec3 vWorldPos;
out vec3 vToCamera;

void main()
{
    vWorldPos = aPosition;
    vToCamera = uCameraPos - vWorldPos;
    gl_Position = projection * view * vec4(aPosition, 1.0);
}