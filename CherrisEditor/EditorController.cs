using Cherris;
using ImGuiNET;
using ImGuizmoNET;
using System.Numerics;

namespace CherrisEditor;

public class EditorController : Script
{
    public float Speed { get; set; } = 5.0f;
    public float MouseSensitivity { get; set; } = 0.0015f;
    public float ZoomSensitivity { get; set; } = 0.5f;

    private float _yaw;
    private float _pitch;

    public override void Start()
    {
        Quaternion initialRotation = GameObject.Transform.Rotation;
        Vector3 direction = Vector3.Transform(-Vector3.UnitZ, initialRotation);
        
        _yaw = float.Atan2(direction.X, -direction.Z);
        _pitch = float.Asin(direction.Y);
    }

    public override void Update(float deltaTime)
    {
        ImGui.Begin("Viewport");
        bool isViewportHovered = ImGui.IsWindowHovered();
        ImGui.End();

        if (!isViewportHovered || ImGuizmo.IsUsing())
        {
            return;
        }

        HandleInput(deltaTime);
    }

    private void HandleInput(float deltaTime)
    {
        ImGuiIOPtr io = ImGui.GetIO();

        HandleMouseZoom(ref io);
        HandleKeyboardMovement(deltaTime, Speed, io);
        HandleMouseLook(io);
    }

    private void HandleMouseZoom(ref ImGuiIOPtr io)
    {
        if (io.MouseWheel == 0)
        {
            return;
        }

        Vector3 forward = Vector3.Transform(-Vector3.UnitZ, GameObject.Transform.Rotation);
        GameObject.Transform.Position += forward * io.MouseWheel * ZoomSensitivity;
    }

    private void HandleKeyboardMovement(float deltaTime, float currentSpeed, ImGuiIOPtr io)
    {
        if (io.KeyShift)
        {
            currentSpeed *= 3.0f; // Speed boost
        }

        Vector3 localMove = Vector3.Zero;

        if (ImGui.IsKeyDown(ImGuiKey.W))
        {
            localMove.Z -= 1;
        }

        if (ImGui.IsKeyDown(ImGuiKey.S))
        {
            localMove.Z += 1;
        }

        if (ImGui.IsKeyDown(ImGuiKey.A))
        {
            localMove.X -= 1;
        }

        if (ImGui.IsKeyDown(ImGuiKey.D))
        {
            localMove.X += 1;
        }

        float worldVerticalMove = 0f;

        if (ImGui.IsKeyDown(ImGuiKey.E))
        {
            worldVerticalMove += 1; // Up
        }

        if (ImGui.IsKeyDown(ImGuiKey.Q))
        {
            worldVerticalMove -= 1; // Down
        }

        if (localMove == Vector3.Zero && worldVerticalMove == 0)
        {
            return;
        }

        Quaternion yawRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, _yaw);
        Vector3 worldHorizontalMove = Vector3.Transform(localMove, yawRotation);
        Vector3 finalMove = new Vector3(worldHorizontalMove.X, worldVerticalMove, worldHorizontalMove.Z);

        if (finalMove.LengthSquared() > 0)
        {
            GameObject.Transform.Position += Vector3.Normalize(finalMove) * currentSpeed * deltaTime;
        }
    }

    private void HandleMouseLook(ImGuiIOPtr io)
    {
        if (!ImGui.IsMouseDown(ImGuiMouseButton.Right))
        {
            return;
        }

        Vector2 mouseDelta = io.MouseDelta;
        _yaw -= mouseDelta.X * MouseSensitivity;
        _pitch -= mouseDelta.Y * MouseSensitivity;
        _pitch = float.Clamp(_pitch, -MathF.PI / 2.0f + 0.001f, MathF.PI / 2.0f - 0.001f);
        GameObject.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, _yaw) *
                                        Quaternion.CreateFromAxisAngle(Vector3.UnitX, _pitch);
    }
}