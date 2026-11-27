#version 330 core
in vec2 vTexCoord;
in vec4 vColor;

uniform sampler2D uTexture;
uniform int uHasTexture;

out vec4 FragColor;

void main()
{
    vec4 texel;
    if (uHasTexture != 0)
    {
        texel = texture(uTexture, vTexCoord);
    }
    else
    {
        // Procedural soft radial disc so untextured particles draw smooth glowing orbs rather than hard squares.
        vec2 centered = (vTexCoord * 2.0) - vec2(1.0);
        float distSq = dot(centered, centered);
        if (distSq > 1.0)
        {
            discard;
        }
        float soft = max(0.0, 1.0 - distSq);
        texel = vec4(1.0, 1.0, 1.0, soft * soft);
    }

    vec4 color = vColor * texel;
    if (color.a <= 0.001)
    {
        discard;
    }

    FragColor = color;
}
