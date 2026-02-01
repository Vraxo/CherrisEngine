using Cherris.Components;
using Cherris.Core;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace Cherris.Rendering.OpenTK;

public class OpenTKRenderer : IRenderer, IDisposable
{
    private readonly ImGuiController _imGuiController;

    // Sub-renderers
    private readonly OpenGLSceneRenderer _sceneRenderer;
    private readonly OpenGLSkyboxRenderer _skyboxRenderer;
    private readonly OpenGLPostProcessor _postProcessor;
    private readonly OpenGLDebugRenderer _debugRenderer;
    private readonly OpenGLGizmoRenderer _gizmoRenderer;
    private readonly OpenGLGridRenderer _gridRenderer;

    private Vector2i _viewportSize = new(1, 1);

    public bool ShowGrid { get; set; } = true;
    public bool ShowPhysicsColliders { get; set; } = true;

    // When true, the final scene is blitted to the default framebuffer (0).
    // Set this to true for Runtime, false for Editor (where scene is an ImGui image).
    public bool PresentToScreen { get; set; } = false;

    public OpenTKRenderer(ImGuiController imGuiController)
    {
        _imGuiController = imGuiController;

        _sceneRenderer = new OpenGLSceneRenderer();
        _skyboxRenderer = new OpenGLSkyboxRenderer();
        _postProcessor = new OpenGLPostProcessor();
        _debugRenderer = new OpenGLDebugRenderer();
        _gizmoRenderer = new OpenGLGizmoRenderer(_debugRenderer);
        _gridRenderer = new OpenGLGridRenderer();

        GL.FrontFace(FrontFaceDirection.Cw);
    }

    public IntPtr GetSceneTextureHandle()
    {
        return _postProcessor.FinalSceneTexture;
    }

    public void SetViewportSize(System.Numerics.Vector2 size)
    {
        var newSize = new Vector2i((int)Math.Max(size.X, 1), (int)Math.Max(size.Y, 1));
        if (newSize != _viewportSize)
        {
            _viewportSize = newSize;
            _postProcessor.OnResize(_viewportSize.X, _viewportSize.Y);
        }
    }

    [Obsolete]
    public void RenderFrame(Camera mainCamera, Skybox skybox, IEnumerable<GameObject> gameObjects, IEnumerable<Light> lights, GameObject selectedObject, float windowWidth, float windowHeight, float exposure)
    {
        // In Runtime mode (PresentToScreen), we must ensure the viewport matches the window
        if (PresentToScreen)
        {
            SetViewportSize(new System.Numerics.Vector2(windowWidth, windowHeight));
        }

        bool isViewportValid = _viewportSize.X > 1 && _viewportSize.Y > 1;

        if (mainCamera is not null && isViewportValid)
        {
            RenderScenePass(mainCamera, skybox, gameObjects, lights, selectedObject, exposure);
        }
        else
        {
            // Clear to black if no camera
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GL.ClearColor(0, 0, 0, 1);
            GL.Clear(ClearBufferMask.ColorBufferBit);
        }

        // If we are in Runtime, blit the FBO result to the backbuffer
        if (PresentToScreen && isViewportValid)
        {
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _postProcessor.FinalSceneTextureFboHandle);
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, 0);
            GL.BlitFramebuffer(0, 0, _viewportSize.X, _viewportSize.Y,
                               0, 0, (int)windowWidth, (int)windowHeight,
                               ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Linear);
        }

        RenderUIPass((int)windowWidth, (int)windowHeight);
    }

    [Obsolete]
    private void RenderScenePass(Camera mainCamera, Skybox skybox, IEnumerable<GameObject> gameObjects, IEnumerable<Light> lights, GameObject selectedObject, float exposure)
    {
        // 1. Prepare Framebuffer & State
        GL.Enable(EnableCap.FramebufferSrgb);
        _postProcessor.BeginFrame();

        GL.Enable(EnableCap.DepthTest);
        GL.Enable(EnableCap.CullFace);
        GL.CullFace(CullFaceMode.Back);
        GL.Disable(EnableCap.Blend);

        // 2. Calculate Matrices
        var view = ToOpenTKMatrix(mainCamera.GetViewMatrix());
        var projection = Matrix4.CreatePerspectiveFieldOfView(
            mainCamera.FieldOfView * (float)Math.PI / 180.0f,
            (float)_viewportSize.X / _viewportSize.Y,
            mainCamera.NearClipPlane,
            mainCamera.FarClipPlane);

        // 3. Render Passes
        if (skybox?.CubeMapTexture is not null)
        {
            _skyboxRenderer.Render(skybox, view, projection);
        }

        if (ShowGrid)
        {
            var cameraPos = view.Inverted().Row3.Xyz;
            _gridRenderer.Render(view, projection, cameraPos);
        }

        _sceneRenderer.Render(gameObjects, lights, view, projection);

        _gizmoRenderer.Render(gameObjects, selectedObject, ShowPhysicsColliders);
        _debugRenderer.Render(view, projection);

        // 4. Post Processing
        _postProcessor.ResolveMsaa();
        _postProcessor.RenderBloom();
        _postProcessor.Composite(exposure);

        GL.Disable(EnableCap.FramebufferSrgb);
    }

    private void RenderUIPass(int width, int height)
    {
        // Reset state for UI rendering
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        GL.Viewport(0, 0, width, height);

        // In Runtime (PresentToScreen), we don't clear because we just blitted the game view.
        // In Editor, we clear background for ImGui docking area.
        if (!PresentToScreen)
        {
            GL.ClearColor(0.1f, 0.105f, 0.11f, 1.00f);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        }

        GL.Disable(EnableCap.DepthTest);
        GL.Disable(EnableCap.CullFace);

        _imGuiController.Render();
    }

    public void OnWindowResized()
    {
        // Handled in RenderFrame for runtime scaling
    }

    public void RequestSnapshot(string path) { /* Not implemented */ }
    public void ProcessSnapshot() { /* Not implemented */ }

    public void Dispose()
    {
        _sceneRenderer.Dispose();
        _skyboxRenderer.Dispose();
        _postProcessor.Dispose();
        _debugRenderer.Dispose();
        _gridRenderer.Dispose();
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
}