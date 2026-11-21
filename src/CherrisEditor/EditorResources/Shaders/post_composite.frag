#version 330 core
out vec4 FragColor;
in vec2 TexCoords;
uniform sampler2D image;
uniform float exposure;
uniform bool isBloomPass;

vec3 tonemap_reinhard(vec3 color) {
    return color / (color + vec3(1.0));
}

vec3 gamma_correct(vec3 linear) {
    const float gamma = 2.2;
    return pow(linear, vec3(1.0 / gamma));
}

void main()
{
    vec3 color = texture(image, TexCoords).rgb;
    if (!isBloomPass) {
        color *= exposure;
        color = tonemap_reinhard(color);
        color = gamma_correct(color);
    }
    
    FragColor = vec4(color, 1.0);
}