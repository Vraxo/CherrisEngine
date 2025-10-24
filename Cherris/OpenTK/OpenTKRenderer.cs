using Cherris.OpenTK;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Cherris
{
    public class OpenTKRenderer : Rendering.IRenderer, IDisposable
    {
        private readonly OpenGLSceneRenderer _sceneRenderer;
        private readonly OpenGLSkyboxRenderer _skyboxRenderer;
        private readonly OpenGLPostProcessor _postProcessor;
        private readonly ImGuiController _imGuiController;

        private int _width, _height;

        public OpenTKRenderer(ImGuiController imGuiController)
        {
            _sceneRenderer = new OpenGLSceneRenderer();
            _skyboxRenderer = new OpenGLSkyboxRenderer();
            _postProcessor = new OpenGLPostProcessor();
            _imGuiController = imGuiController;

            GL.ClearColor(0.1f, 0.1f, 0.2f, 1.0f);
            GL.FrontFace(FrontFaceDirection.Cw);
        }

        public void RenderFrame(Camera mainCamera, Skybox skybox, IEnumerable<GameObject> gameObjects, GameObject selectedObject, float windowWidth, float windowHeight, float exposure)
        {
            if ((int)windowWidth != _width || (int)windowHeight != _height)
            {
                OnWindowResized((int)windowWidth, (int)windowHeight);
            }
            if (mainCamera is null) return;

            // 1. Scene Pass: Render scene to offscreen MSAA framebuffer
            _postProcessor.BeginFrame();
            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.CullFace);
            GL.CullFace(CullFaceMode.Back);
            GL.Disable(EnableCap.Blend);

            var view = ToOpenTKMatrix(mainCamera.GetViewMatrix());
            var projection = Matrix4.CreatePerspectiveFieldOfView(
                mainCamera.FieldOfView * (float)Math.PI / 180.0f,
                windowWidth / windowHeight,
                mainCamera.NearClipPlane,
                mainCamera.FarClipPlane);

            if (skybox?.CubeMapTexture != null)
            {
                _skyboxRenderer.Render(skybox, view, projection);
            }

            _sceneRenderer.Render(gameObjects, view, projection);

            // Note: selectedObject outline rendering is omitted in this refactor for simplicity.
            // It would require stencil logic within the OpenGLSceneRenderer.

            // 2. Resolve MSAA Framebuffer
            _postProcessor.ResolveMsaa();

            // 3. Bloom / Post-Processing
            _postProcessor.RenderBloom();

            // 4. Final Composite Pass: Render to screen
            _postProcessor.CompositeToScreen(exposure);

            // 5. ImGui Pass
            _imGuiController.Render();
        }

        private void OnWindowResized(int width, int height)
        {
            _width = width;
            _height = height;
            GL.Viewport(0, 0, width, height);
            _postProcessor.OnResize(width, height);
            _imGuiController.WindowResized(width, height);
        }

        // This is the implementation for the IRenderer interface method.
        // It's not called by the OpenTK backend loop, which uses the width/height version.
        public void OnWindowResized() { }

        public void RequestSnapshot(string path) { /* Not implemented for OpenTK */ }
        public void ProcessSnapshot() { /* Not implemented for OpenTK */ }

        public void Dispose()
        {
            _sceneRenderer?.Dispose();
            _skyboxRenderer?.Dispose();
            _postProcessor?.Dispose();
        }

        private static Matrix4 ToOpenTKMatrix(System.Numerics.Matrix4x4 m)
        {
            return new Matrix4(
                m.M11, m.M12, m.M13, m.M14,
                m.M21, m.M22, m.M23, m.M24,
                m.M31, m.M32, m.M33, m.M34,
                m.M41, m.M42, m.M43, m.M44
            );
        }

        [Conditional("DEBUG")]
        private static void CheckGLError(string context)
        {
            var error = GL.GetError();
            while (error != ErrorCode.NoError)
            {
                Console.WriteLine($"[OpenGL Error] After {context}: {error}");
                error = GL.GetError();
            }
        }
    }
}