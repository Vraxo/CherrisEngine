using Cherris;
using System;
using System.Numerics;
using ImGuiNET;
using ImGuizmoNET;

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
        var initialRotation = GameObject.Transform.Rotation;
        var direction = Vector3.Transform(-Vector3.UnitZ, initialRotation);
        _yaw = MathF.Atan2(direction.X, -direction.Z);
        _pitch = MathF.Asin(direction.Y);
    }

    public override void Update(float deltaTime)
    {
        // This pattern of Begin/End is safe to use for getting context on an existing window.
        ImGui.Begin("Viewport");
        bool isViewportHovered = ImGui.IsWindowHovered();
        ImGui.End();

        // Only process controls if the viewport is hovered and the gizmo is not in use.
        if (!isViewportHovered || ImGuizmo.IsUsing())
        {
            return;
        }

        var io = ImGui.GetIO();

        // --- Mouse Wheel Zoom ---
        if (io.MouseWheel != 0)
        {
            var forward = Vector3.Transform(-Vector3.UnitZ, GameObject.Transform.Rotation);
            GameObject.Transform.Position += forward * io.MouseWheel * ZoomSensitivity;
        }

        // --- Keyboard Movement (when viewport is hovered) ---
        float currentSpeed = Speed;
        if (io.KeyShift)
        {
            currentSpeed *= 3.0f; // Speed boost
        }

        var localMove = Vector3.Zero;
        if (ImGui.IsKeyDown(ImGuiKey.W)) localMove.Z -= 1;
        if (ImGui.IsKeyDown(ImGuiKey.S)) localMove.Z += 1;
        if (ImGui.IsKeyDown(ImGuiKey.A)) localMove.X -= 1;
        if (ImGui.IsKeyDown(ImGuiKey.D)) localMove.X += 1;

        var worldVerticalMove = 0f;
        if (ImGui.IsKeyDown(ImGuiKey.E)) worldVerticalMove += 1; // Up
        if (ImGui.IsKeyDown(ImGuiKey.Q)) worldVerticalMove -= 1; // Down

        if (localMove != Vector3.Zero || worldVerticalMove != 0)
        {
            var yawRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, _yaw);
            var worldHorizontalMove = Vector3.Transform(localMove, yawRotation);
            var finalMove = new Vector3(worldHorizontalMove.X, worldVerticalMove, worldHorizontalMove.Z);

            if (finalMove.LengthSquared() > 0)
            {
                GameObject.Transform.Position += Vector3.Normalize(finalMove) * currentSpeed * deltaTime;
            }
        }

        // --- Mouse Look (only when right mouse button is down) ---
        if (ImGui.IsMouseDown(ImGuiMouseButton.Right))
        {
            Vector2 mouseDelta = io.MouseDelta;
            _yaw -= mouseDelta.X * MouseSensitivity;
            _pitch -= mouseDelta.Y * MouseSensitivity;
            _pitch = Math.Clamp(_pitch, -MathF.PI / 2.0f + 0.001f, MathF.PI / 2.0f - 0.001f);
            GameObject.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, _yaw) *
                                              Quaternion.CreateFromAxisAngle(Vector3.UnitX, _pitch);
        }
    }
}