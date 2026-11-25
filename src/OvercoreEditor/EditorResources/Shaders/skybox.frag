#version 410 core
in vec3 TexCoords;
out vec4 FragColor;

uniform samplerCube skybox;
uniform int uHasSkybox;
uniform vec3 uSkyZenith;
uniform vec3 uSkyHorizon;
uniform vec3 uSkyGround;

// A scene without a skybox still needs a horizon. The colours arrive as uniforms, so the look is tuned beside the
// code that draws it rather than inside a shader string.
vec3 gradient(vec3 direction)
{
    float height = direction.y;

    // The bands are stretched unevenly: the sky reads as a gradient instead of a stripe, and the ground is darker
    // than the horizon so the grid drawn on top of it stays the brightest thing at eye level.
    if (height >= 0.0)
    {
        return mix(uSkyHorizon, uSkyZenith, smoothstep(0.0, 0.55, height));
    }

    return mix(uSkyHorizon, uSkyGround, smoothstep(0.0, 0.30, -height));
}

void main()
{
    if (uHasSkybox != 0)
    {
        FragColor = texture(skybox, TexCoords);
        return;
    }

    FragColor = vec4(gradient(normalize(TexCoords)), 1.0);
}
