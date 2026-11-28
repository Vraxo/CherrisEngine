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

// Wind, for the terrain's detail: a draw that wants it names a strength, everything else gets zero
// and stands still. uTime is the frame's clock in seconds.
uniform float uTime;
uniform float uWindStrength;

out vec3 FragPos;
out vec3 Normal;
out vec4 fsin_Color;
out vec2 fsin_TexCoord;

void main()
{
    FragPos = vec3(model * vec4(aPosition, 1.0));

    // A vertex bends by the square of its height up its card — the v coordinate detail tufts carry
    // root (0) to tip (1) — phased by where it stands, so the field ripples instead of marching.
    float bendWeight = aTexCoord.y * aTexCoord.y;
    if (uWindStrength > 0.0 && bendWeight > 0.0)
    {
        float phase = FragPos.x * 0.35 + FragPos.z * 0.27;
        float sway = sin(uTime * 1.6 + phase) + (0.5 * sin(uTime * 2.7 + (phase * 1.7)));
        FragPos.x += sway * uWindStrength * bendWeight;
        FragPos.z += sway * uWindStrength * bendWeight * 0.6;
    }

    Normal = mat3(transpose(inverse(model))) * aNormal;

    gl_Position = projection * view * vec4(FragPos, 1.0);
    fsin_Color = aColor;
    fsin_TexCoord = aTexCoord * uTiling;
}
