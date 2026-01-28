using Cherris.Components;
using Cherris.Core;
using Cherris.OpenTK;
using Cherris.Rendering;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System.Diagnostics;

namespace Cherris;

public class OpenTKRenderer : IRenderer, IDisposable
{
    private readonly OpenGLSceneRenderer _sceneRenderer;
    private readonly OpenGLSkyboxRenderer _skyboxRenderer;
    private readonly OpenGLPostProcessor _postProcessor;
    private readonly ImGuiController _imGuiController;
    private readonly OpenGLDebugRenderer _debugRenderer;
    private readonly OpenGLGridRenderer _gridRenderer;

    private Vector2i _viewportSize = new(1, 1);
    private Vector2i _windowSize;
    public bool ShowGrid { get; set; } = true;
    public bool ShowPhysicsColliders { get; set; } = true;

    public OpenTKRenderer(ImGuiController imGuiController)
    {
        _sceneRenderer = new OpenGLSceneRenderer();
        _skyboxRenderer = new OpenGLSkyboxRenderer();
        _postProcessor = new OpenGLPostProcessor();
        _imGuiController = imGuiController;
        _debugRenderer = new OpenGLDebugRenderer();
        _gridRenderer = new OpenGLGridRenderer();

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

    public void RenderFrame(Camera mainCamera, Skybox skybox, IEnumerable<GameObject> gameObjects, IEnumerable<Light> lights, GameObject selectedObject, float windowWidth, float windowHeight, float exposure)
    {
        _windowSize = new Vector2i((int)windowWidth, (int)windowHeight);

        // 1. Render scene to texture if the viewport is visible
        if (mainCamera is not null && _viewportSize.X > 1 && _viewportSize.Y > 1)
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

            _debugRenderer.Clear();
            if (selectedObject?.GetComponent<Light>() is Light light)
            {
                if (light.Type == LightType.Directional)
                {
                    DrawDirectionalLightGizmo(light);
                }
                else if (light.Type == LightType.Spot)
                {
                    DrawSpotlightGizmo(light);
                }
            }

            if (ShowPhysicsColliders)
            {
                foreach (var go in gameObjects)
                {
                    DrawRigidBodyGizmo(go);
                }
            }

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
            _debugRenderer.Render(view, projection);

            _postProcessor.ResolveMsaa();
            _postProcessor.RenderBloom();
            _postProcessor.Composite(exposure);

            GL.Disable(EnableCap.FramebufferSrgb);
        }

        // 2. Clear main window and render UI
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        GL.Viewport(0, 0, _windowSize.X, _windowSize.Y);
        GL.ClearColor(0.1f, 0.105f, 0.11f, 1.00f);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        _imGuiController.Render();
    }

    private void DrawDirectionalLightGizmo(Light light)
    {
        var transform = light.GameObject.Transform;
        var color = new System.Numerics.Vector3(1.0f, 0.9f, 0.2f); // Yellow
        float gizmoSize = 2.0f;
        float arrowHeadSize = 0.25f;
        float arrowLength = 1.0f * gizmoSize;

        var direction = System.Numerics.Vector3.Transform(-System.Numerics.Vector3.UnitZ, transform.Rotation);
        var up = System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitY, transform.Rotation);
        var right = System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitX, transform.Rotation);

        var start = transform.Position;
        var end = start + direction * arrowLength;
        _debugRenderer.AddLine(start, end, color);

        _debugRenderer.AddLine(end, end - direction * arrowHeadSize + right * arrowHeadSize, color);
        _debugRenderer.AddLine(end, end - direction * arrowHeadSize - right * arrowHeadSize, color);
        _debugRenderer.AddLine(end, end - direction * arrowHeadSize + up * arrowHeadSize, color);
        _debugRenderer.AddLine(end, end - direction * arrowHeadSize - up * arrowHeadSize, color);

        const int circleSegments = 16;
        float circleRadius = 0.5f * gizmoSize;
        for (int i = 0; i < circleSegments; i++)
        {
            float angle1 = (i / (float)circleSegments) * 2.0f * MathF.PI;
            float angle2 = ((i + 1) / (float)circleSegments) * 2.0f * MathF.PI;

            var p1 = transform.Position + (right * MathF.Cos(angle1) + up * MathF.Sin(angle1)) * circleRadius;
            var p2 = transform.Position + (right * MathF.Cos(angle2) + up * MathF.Sin(angle2)) * circleRadius;
            _debugRenderer.AddLine(p1, p2, color);
        }
    }

    private void DrawSpotlightGizmo(Light light)
    {
        var transform = light.GameObject.Transform;
        var color = new System.Numerics.Vector3(1.0f, 0.9f, 0.2f); // Yellow

        var origin = transform.Position;
        var direction = transform.Forward;
        var up = System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitY, transform.Rotation);
        var right = System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitX, transform.Rotation);

        float range = light.Range;
        float outerAngleRad = light.OuterConeAngle * MathF.PI / 180.0f;
        float outerRadius = range * MathF.Tan(outerAngleRad);

        var circleCenter = origin + direction * range;

        // Draw lines from origin to the edge of the cone circle
        _debugRenderer.AddLine(origin, circleCenter + right * outerRadius, color);
        _debugRenderer.AddLine(origin, circleCenter - right * outerRadius, color);
        _debugRenderer.AddLine(origin, circleCenter + up * outerRadius, color);
        _debugRenderer.AddLine(origin, circleCenter - up * outerRadius, color);

        // Draw the circle at the end of the range
        const int circleSegments = 16;
        for (int i = 0; i < circleSegments; i++)
        {
            float angle1 = (i / (float)circleSegments) * 2.0f * MathF.PI;
            float angle2 = ((i + 1) / (float)circleSegments) * 2.0f * MathF.PI;

            var p1 = circleCenter + (right * MathF.Cos(angle1) + up * MathF.Sin(angle1)) * outerRadius;
            var p2 = circleCenter + (right * MathF.Cos(angle2) + up * MathF.Sin(angle2)) * outerRadius;
            _debugRenderer.AddLine(p1, p2, color);
        }
    }

    private void DrawRigidBodyGizmo(GameObject go)
    {
        var rb = go.GetComponent<RigidBody>();
        if (rb?.JitterBody is null) return;

        var color = rb.JitterBody.IsStatic ? new System.Numerics.Vector3(0.2f, 0.8f, 0.2f) : new System.Numerics.Vector3(0.8f, 0.2f, 0.8f); // Green for static, purple for dynamic

        var position = rb.JitterBody.Position.ToNumerics();
        var orientation = rb.JitterBody.Orientation.ToNumerics();

        if (rb.JitterBody.Shape is Jitter.Collision.Shapes.BoxShape box)
        {
            _debugRenderer.AddBox(position, orientation, box.Size.ToNumerics(), color);
        }
        else if (rb.JitterBody.Shape is Jitter.Collision.Shapes.SphereShape sphere)
        {
            _debugRenderer.AddSphere(position, sphere.Radius, color);
        }
        else if (rb.JitterBody.Shape is Jitter.Collision.Shapes.CapsuleShape capsule)
        {
            _debugRenderer.AddCapsule(position, orientation, capsule.Length, capsule.Radius, color);
        }
    }


    public void OnWindowResized() { }

    public void RequestSnapshot(string path) { /* Not implemented for OpenTK */ }
    public void ProcessSnapshot() { /* Not implemented for OpenTK */ }

    public void Dispose()
    {
        _sceneRenderer?.Dispose();
        _skyboxRenderer?.Dispose();
        _postProcessor?.Dispose();
        _debugRenderer?.Dispose();
        _gridRenderer?.Dispose();
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