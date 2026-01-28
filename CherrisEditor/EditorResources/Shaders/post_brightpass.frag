#version 330 core
out vec4 FragColor;
in vec2 TexCoords;
uniform sampler2D image;
const float threshold = 1.1;
void main()
{
    vec3 color = texture(image, TexCoords).rgb;
    vec3 finalColor = max(vec3(0.0), color - threshold);
    FragColor = vec4(finalColor, 1.0);
}