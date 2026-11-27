#version 330 core
layout (location = 0) in vec2 aCorner;
layout (location = 1) in vec2 aTexCoord;
layout (location = 2) in vec3 iPosition;
layout (location = 3) in vec4 iColor;
layout (location = 4) in vec3 iRight;
layout (location = 5) in vec3 iUp;

uniform mat4 view;
uniform mat4 projection;

out vec2 vTexCoord;
out vec4 vColor;

void main()
{
    vec3 worldPos = iPosition + (iRight * aCorner.x) + (iUp * aCorner.y);
    gl_Position = projection * view * vec4(worldPos, 1.0);
    vTexCoord = aTexCoord;
    vColor = iColor;
}
