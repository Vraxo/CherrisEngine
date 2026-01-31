using Cherris.Components;
using Cherris.Core;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace Cherris.Rendering.OpenTK;

public class OpenTKRenderer : IRenderer, IDisposable
{
    private readonly OpenGLSceneRenderer _sceneRenderer;
    private readonly OpenGLSkyboxRenderer _skyboxRenderer;
    private readonly OpenGLPostProcessor _postProcessor;
    private readonly ImGuiController _imGuiController;
    private readonly OpenGLDebugRenderer _debugRenderer;
    private readonly OpenGLGizmoRenderer _gizmoRenderer;
    private readonly OpenGLGridRenderer _gridRenderer;

    private Vector2i _viewportSize = new(1, 1);
    public bool ShowGrid { get; set; } = true;
    public bool ShowPhysicsColliders { get; set; } = true;

    public OpenTKRenderer(ImGuiController imGuiController)
    {
        _imGuiController = imGuiController;

        // Sub-renderers
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
        // 1. Scene Pass (if viewport visible)
        if (mainCamera is not null && _viewportSize.X > 1 && _viewportSize.Y > 1)
        {
            RenderScenePass(mainCamera, skybox, gameObjects, lights, selectedObject, exposure);
        }

        // 2. UI Pass (Screen)
        RenderUIPass((int)windowWidth, (int)windowHeight);
    }

    [Obsolete]
    private void RenderScenePass(Camera mainCamera, Skybox skybox, IEnumerable<GameObject> gameObjects, IEnumerable<Light> lights, GameObject selectedObject, float exposure)
    {
        GL.Enable(EnableCap.FramebufferSrgb);
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

        // Render Skybox
        if (skybox?.CubeMapTexture is not null)
        {
            _skyboxRenderer.Render(skybox, view, projection);
        }

        // Render Grid
        if (ShowGrid)
        {
            var cameraPos = view.Inverted().Row3.Xyz;
            _gridRenderer.Render(view, projection, cameraPos);
        }

        // Render Scene Objects
        _sceneRenderer.Render(gameObjects, lights, view, projection);

        // Render Gizmos & Debug Lines
        _gizmoRenderer.Render(gameObjects, selectedObject, ShowPhysicsColliders);
        _debugRenderer.Render(view, projection);

        // Post Processing
        _postProcessor.ResolveMsaa();
        _postProcessor.RenderBloom();
        _postProcessor.Composite(exposure);

        GL.Disable(EnableCap.FramebufferSrgb);
    }

    private void RenderUIPass(int width, int height)
    {
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        GL.Viewport(0, 0, width, height);
        GL.ClearColor(0.1f, 0.105f, 0.11f, 1.00f);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        _imGuiController.Render();
    }

    public void OnWindowResized() { }
    public void RequestSnapshot(string path) { /* Not implemented for OpenTK */ }
    public void ProcessSnapshot() { /* Not implemented for OpenTK */ }

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