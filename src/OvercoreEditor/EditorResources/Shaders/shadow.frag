#version 330 core
// The depth pass's fragment stage: empty for an opaque caster, a discard for a cutout one.
//
// The depth buffer is written by the rasteriser before any fragment shader runs, so for an opaque caster this stage has
// nothing to do — the framebuffer has no colour attachment at all, and `glDrawBuffer(GL_NONE)` says so. It exists as a
// file because `ShaderProgram` always compiles a vertex and a fragment source pair, and one shader is a smaller price
// than a second constructor overload on it.
//
// A cutout caster is the case that earns the stage. A chain-link fence, a leaf card or a grille is a quad with holes
// cut in its texture, and without the test below the depth map records the whole quad: the shadow is a solid rectangle
// where the fence stands, which is not a coarser version of the right answer but a different object.

in vec2 fsin_TexCoord;

// The material's diffuse, on unit 0 — the unit the colour pass already binds it to, so a caster needs no second unit
// and none of the three the engine has is spent on this.
uniform sampler2D uDiffuse;

// Below this alpha a fragment is thrown away. 0 turns the test off.
uniform float uAlphaCutoff;

void main()
{
    // A fragment with no alpha is not there: the rasteriser has already written its depth, so the only way to remove it
    // from the map is to discard, which also suppresses the depth write for this fragment.
    //
    // The comparison is written so the common case costs one instruction: a material with no cutoff sends 0, and
    // `alpha < 0.0` is false for every alpha a texture can hold. Not `uAlphaCutoff > 0.0 && ...`, because a branch
    // here is a branch in the inner loop of a pass that runs four times a frame, and the shader compiler folds the
    // comparison against the uniform anyway.
    if (texture(uDiffuse, fsin_TexCoord).a < uAlphaCutoff)
    {
        discard;
    }
}
