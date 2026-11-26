#version 330 core
// The static depth stage: a vertex's position in the light's orthographic box, and nothing else.
//
// There is no normal, no colour and no texcoord here. A depth pass that carried the scene shader's whole varying set
// would pay for interpolation it throws away, and the one thing that matters — `gl_Position` — never had anything to
// do with them. `scene.vert` is the stage with the lighting; this is the stage with only the geometry.

layout (location = 0) in vec3 aPosition;
layout (location = 3) in vec2 aTexCoord;

// Locations 4 and 5 carry bone indices and weights for a skinned mesh. This shader does not declare them, so a static
// mesh pays nothing for a layout it never reads; shadow_skinned.vert is the stage that does.

// Declared only because a cutout caster's fragment stage samples the diffuse atlas with it. It is two interpolated
// floats for every caster in the scene, which is the price of a fence casting the shadow of its holes rather than of
// its quad.
out vec2 fsin_TexCoord;

uniform mat4 model;

// The material's tiling, the same uniform the colour pass multiplies into the same texcoord. Unused by a caster with
// no cutoff, which is most of them, and cheap enough not to need a third program.
uniform vec2 uTiling;

// The cascade's light view multiplied by its light projection, uploaded per cascade. The eye and clip planes live
// inside it, which is why this stage needs no `view` and no `projection` of its own.
uniform mat4 lightViewProjection;

void main()
{
    gl_Position = lightViewProjection * model * vec4(aPosition, 1.0);
    fsin_TexCoord = aTexCoord * uTiling;
}
