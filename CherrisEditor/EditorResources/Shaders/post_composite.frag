#version 330 core
out vec4 FragColor;
in vec2 TexCoords;
uniform sampler2D image;
uniform float exposure;
uniform bool isBloomPass;

vec3 tonemap_reinhard(vec3 color) {
    return color / (color + vec3(1.0));
}

void main()
{
    vec3 color = texture(image, TexCoords).rgb;
    if (!isBloomPass) { // This is the main scene pass
        color *= exposure;
        color = tonemap_reinhard(color);
    }
    // else, this is the bloom pass, so we output the raw color for additive blending.
    
    FragColor = vec4(color, 1.0);
}