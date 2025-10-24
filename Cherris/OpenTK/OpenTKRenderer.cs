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

        private Vector2i _viewportSize = new(1, 1);
        private Vector2i _windowSize;

        public OpenTKRenderer(ImGuiController imGuiController)
        {
            _sceneRenderer = new OpenGLSceneRenderer();
            _skyboxRenderer = new OpenGLSkyboxRenderer();
            _postProcessor = new OpenGLPostProcessor();
            _imGuiController = imGuiController;

            GL.FrontFace(FrontFaceDirection.Cw);
        }

        public IntPtr GetSceneTextureHandle() => (IntPtr)_postProcessor.FinalSceneTexture;

        public void SetViewportSize(System.Numerics.Vector2 size)
        {
            var newSize = new Vector2i((int)Math.Max(size.X, 1), (int)Math.Max(size.Y, 1));
            if (newSize != _viewportSize)
            {
                _viewportSize = newSize;
                _postProcessor.OnResize(_viewportSize.X, _viewportSize.Y);
            }
        }

        public void RenderFrame(Camera mainCamera, Skybox skybox, IEnumerable<GameObject> gameObjects, GameObject selectedObject, float windowWidth, float windowHeight, float exposure)
        {
            _windowSize = new Vector2i((int)windowWidth, (int)windowHeight);

            // 1. Render scene to texture if the viewport is visible
            if (mainCamera is not null && _viewportSize.X > 1 && _viewportSize.Y > 1)
            {
                _postProcessor.BeginFrame();
                GL.Enable(EnableCap.DepthTest);
                GL.Enable(EnableCap.CullFace);
                GL.CullFace(CullFaceMode.Back);
                GL.Disable(EnableCap.Blend);

                var view = ToOpenTKMatrix(mainCamera.GetViewMatrix());
                var projection = Matrix4.CreatePerspectiveFieldOfView(
                    mainCamera.FieldOfView * (float)Math.PI / 180.0f,
                    (float)_viewportSize.X / _viewportSize.Y,
                    mainCamera.NearClipPlane,
                    mainCamera.FarClipPlane);

                if (skybox?.CubeMapTexture is not null)
                {
                    _skyboxRenderer.Render(skybox, view, projection);
                }

                _sceneRenderer.Render(gameObjects, view, projection);

                _postProcessor.ResolveMsaa();
                _postProcessor.RenderBloom();
                _postProcessor.Composite(exposure);
            }

            // 2. Clear main window and render UI
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GL.Viewport(0, 0, _windowSize.X, _windowSize.Y);
            GL.ClearColor(0.1f, 0.105f, 0.11f, 1.00f);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            _imGuiController.Render();
        }

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