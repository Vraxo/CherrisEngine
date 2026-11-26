#version 330 core
// The skinned depth stage: a character's deformed position in the light's orthographic box.
//
// `BoneMatrix` and `SkinMatrix` are copied from skin.vert, character for character, including the `bone * 3` stride,
// the column reconstruction and the identity fallback for an unweighted vertex. Copied rather than shared because
// GLSL 330 has no include, and because a shader assembled by string substitution is a shader nobody can read in the
// repository. ShadowShaderTests reads both files and asserts the constants that tie the palette to the shader still
// agree, which is what keeps this copy from drifting away from skin.vert one edit at a time.
//
// A caster that is not skinned here would stand in the light waving its arms while its shadow stood still, so this
// stage is not an optimisation to skip when shadows are turned on.

layout (location = 0) in vec3 aPosition;
layout (location = 1) in vec3 aNormal;
layout (location = 2) in vec4 aColor;
layout (location = 3) in vec2 aTexCoord;
layout (location = 4) in vec4 aBones;
layout (location = 5) in vec4 aWeights;

uniform mat4 model;
uniform mat4 lightViewProjection;

// Three texels per bone, one per column of the joint matrix. RGBA32F so a column is exact.
uniform sampler2D uBoneTexture;

// One bone's joint matrix, read from the palette.
//
// A texel holds a column. The fourth column of an affine matrix is always (0, 0, 0, 1), so only three are uploaded
// and the fourth is reconstructed — GLSL's mat4 constructor takes columns, so this is the matrix itself.
mat4 BoneMatrix(int bone)
{
    int texel = bone * 3;

    vec4 column0 = texelFetch(uBoneTexture, ivec2(texel + 0, 0), 0);
    vec4 column1 = texelFetch(uBoneTexture, ivec2(texel + 1, 0), 0);
    vec4 column2 = texelFetch(uBoneTexture, ivec2(texel + 2, 0), 0);

    return mat4(column0, column1, column2, vec4(0.0, 0.0, 0.0, 1.0));
}

// The vertex's four influences blended by weight. A vertex no bone moves would be annihilated by a zero matrix, so
// an unweighted vertex takes the identity and stays where it was modelled.
mat4 SkinMatrix()
{
    float total = aWeights.x + aWeights.y + aWeights.z + aWeights.w;

    if (total <= 0.0)
    {
        return mat4(1.0);
    }

    mat4 skin = aWeights.x * BoneMatrix(int(aBones.x))
              + aWeights.y * BoneMatrix(int(aBones.y))
              + aWeights.z * BoneMatrix(int(aBones.z))
              + aWeights.w * BoneMatrix(int(aBones.w));

    // Normalized here rather than trusted: an importer normalizes weights, but a mesh built by hand need not, and a
    // palette scaled by 1.02 is a shadow that slowly drifts off its caster.
    return skin / total;
}

void main()
{
    vec3 skinnedPosition = vec3(vec4(aPosition, 1.0) * SkinMatrix());

    gl_Position = lightViewProjection * model * vec4(skinnedPosition, 1.0);
}
