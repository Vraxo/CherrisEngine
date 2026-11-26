#version 330 core
// The depth pass's fragment stage, and it has nothing to do.
//
// The depth buffer is written by the rasteriser before any fragment shader runs, so the output of this stage is thrown
// away — the framebuffer has no colour attachment at all, and `glDrawBuffer(GL_NONE)` says so. The file exists because
// `ShaderProgram` always compiles a vertex and a fragment source pair, and one empty shader is a smaller price than a
// second constructor overload on it.

void main()
{
}
