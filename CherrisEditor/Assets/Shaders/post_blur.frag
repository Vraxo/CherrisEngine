#version 330 core
out vec4 FragColor;
in vec2 TexCoords;
uniform sampler2D image;
uniform bool horizontal;

// 5-tap Gaussian blur (Veldrid equivalent)
const float weights[3] = float[](0.227027, 0.316216, 0.070270);
const float offsets[3] = float[](0.0, 1.384615, 3.230769);

void main()
{
    vec2 texelSize = 1.0 / textureSize(image, 0);
    vec3 result = texture(image, TexCoords).rgb * weights[0];
    vec2 dir = horizontal ? vec2(texelSize.x, 0.0) : vec2(0.0, texelSize.y);

    for (int i = 1; i < 3; i++) {
        result += texture(image, TexCoords + offsets[i] * dir).rgb * weights[i];
        result += texture(image, TexCoords - offsets[i] * dir).rgb * weights[i];
    }
    FragColor = vec4(result, 1.0);
}