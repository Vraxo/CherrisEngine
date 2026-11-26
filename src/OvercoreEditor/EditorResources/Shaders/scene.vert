#version 330 core
layout (location = 0) in vec3 aPosition;
layout (location = 1) in vec3 aNormal;
layout (location = 2) in vec4 aColor;
layout (location = 3) in vec2 aTexCoord;

// Locations 4 and 5 carry bone indices and weights for a skinned mesh. This shader does not declare them, so the
// static pipeline pays nothing for a layout it never reads; skin.vert is the stage that does.

uniform mat4 model;
uniform mat4 view;
uniform mat4 projection;
uniform vec2 uTiling;

out vec3 FragPos;
out vec3 Normal;
out vec4 fsin_Color;
out vec2 fsin_TexCoord;

void main()
{
    FragPos = vec3(model * vec4(aPosition, 1.0));
    Normal = mat3(transpose(inverse(model))) * aNormal;
    
    gl_Position = projection * view * vec4(FragPos, 1.0);
    fsin_Color = aColor;
    fsin_TexCoord = aTexCoord * uTiling;
}
