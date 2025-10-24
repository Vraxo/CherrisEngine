using Cherris;
using ImGuiNET;
using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace CherrisEditor;

public class EditorAppLogic
{
    private readonly Editor _editor;

    public EditorAppLogic(Editor editor)
    {
        _editor = editor;
    }

    public void DrawUI(float deltaTime)
    {
        // Setup the main dockspace
        SetupDockspace();

        // Draw the individual editor panels. They will be floating on first launch
        // and can be docked manually. ImGui will save the layout for subsequent runs.
        DrawHierarchyPanel();
        DrawInspectorPanel();
        DrawConsolePanel();
    }

    private void SetupDockspace()
    {
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.Pos);
        ImGui.SetNextWindowSize(viewport.Size);
        ImGui.SetNextWindowViewport(viewport.ID);

        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0.0f);

        var windowFlags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
                          ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoNavFocus | ImGuiWindowFlags.MenuBar;

        ImGui.Begin("MainDockspace", windowFlags);
        ImGui.PopStyleVar(2);

        var dockspaceId = ImGui.GetID("MyDockSpace");
        ImGui.DockSpace(dockspaceId, Vector2.Zero, ImGuiDockNodeFlags.PassthruCentralNode);

        DrawMainMenuBar();

        ImGui.End();
    }

    private void DrawMainMenuBar()
    {
        if (ImGui.BeginMenuBar())
        {
            if (ImGui.BeginMenu("File"))
            {
                if (ImGui.MenuItem("Exit"))
                {
                    // This is a placeholder; a real implementation would close the app.
                    Console.WriteLine("Exit clicked!");
                }
                ImGui.EndMenu();
            }
            ImGui.EndMenuBar();
        }
    }

    private void DrawHierarchyPanel()
    {
        ImGui.Begin("Hierarchy");

        foreach (var go in _editor.SceneManager.GameObjects)
        {
            bool isSelected = _editor.GetSelectedGameObject() == go;
            if (ImGui.Selectable(go.Name, isSelected))
            {
                _editor.SetSelectedGameObject(go);
            }
        }

        ImGui.End();
    }

    private void DrawInspectorPanel()
    {
        ImGui.Begin("Inspector");

        var selectedObject = _editor.GetSelectedGameObject();
        if (selectedObject != null)
        {
            ImGui.Text($"Selected: {selectedObject.Name}");
            ImGui.Separator();

            // Transform Component
            if (ImGui.CollapsingHeader("Transform", ImGuiTreeNodeFlags.DefaultOpen))
            {
                var position = selectedObject.Transform.Position;
                if (ImGui.DragFloat3("Position", ref position, 0.1f))
                {
                    selectedObject.Transform.Position = position;
                }

                // Convert quaternion to Euler angles for editing
                var eulerDegrees = ToEulerAngles(selectedObject.Transform.Rotation) * (180.0f / MathF.PI);
                if (ImGui.DragFloat3("Rotation", ref eulerDegrees, 1.0f))
                {
                    var eulerRadians = eulerDegrees * (MathF.PI / 180.0f);
                    selectedObject.Transform.Rotation = Quaternion.CreateFromYawPitchRoll(eulerRadians.Y, eulerRadians.X, eulerRadians.Z);
                }

                var scale = selectedObject.Transform.Scale;
                if (ImGui.DragFloat3("Scale", ref scale, 0.1f))
                {
                    selectedObject.Transform.Scale = scale;
                }
            }
        }
        else
        {
            ImGui.Text("No object selected.");
        }

        ImGui.End();
    }

    private void DrawConsolePanel()
    {
        ImGui.Begin("Console");
        ImGui.Text("Log messages will appear here...");
        ImGui.End();
    }

    public void UpdateEditorLogic(float deltaTime)
    {
        if (Input.WasMouseButtonPressed(MouseButton.Left))
        {
            // Only process scene clicks if the mouse is not over an ImGui window
            if (!ImGui.GetIO().WantCaptureMouse)
            {
                Ray ray = _editor.CreateRayFromMouse();
                GameObject? closestObject = null;
                float closestDistance = float.MaxValue;

                foreach (var go in _editor.SceneManager.GameObjects)
                {
                    if (go.GetComponent<Skybox>() != null || go.GetComponent<Camera>() != null) continue;
                    var aabb = go.GetWorldSpaceAABB();
                    if (ray.Intersects(aabb, out float distance))
                    {
                        if (distance < closestDistance)
                        {
                            closestDistance = distance;
                            closestObject = go;
                        }
                    }
                }
                _editor.SetSelectedGameObject(closestObject);
                if (closestObject is not null) Console.WriteLine($"Selected '{closestObject.Name}'");
            }
        }

        var selectedGameObject = _editor.GetSelectedGameObject();
        if (selectedGameObject is not null)
        {
            const float moveSpeed = 2.0f;
            var moveDirection = Vector3.Zero;
            bool shiftHeld = Input.IsKeyDown(Key.ShiftLeft) || Input.IsKeyDown(Key.ShiftRight);

            if (Input.IsKeyDown(Key.Left)) moveDirection.X -= 1;
            if (Input.IsKeyDown(Key.Right)) moveDirection.X += 1;
            if (shiftHeld)
            {
                if (Input.IsKeyDown(Key.Up)) moveDirection.Y += 1;
                if (Input.IsKeyDown(Key.Down)) moveDirection.Y -= 1;
            }
            else
            {
                if (Input.IsKeyDown(Key.Up)) moveDirection.Z -= 1;
                if (Input.IsKeyDown(Key.Down)) moveDirection.Z += 1;
            }
            if (moveDirection != Vector3.Zero)
                selectedGameObject.Transform.Position += Vector3.Normalize(moveDirection) * moveSpeed * deltaTime;
        }
    }

    // Helper to convert Quaternion to Euler angles (in radians) for display
    private Vector3 ToEulerAngles(Quaternion q)
    {
        Vector3 angles = new();

        // Roll (x-axis rotation)
        float sinr_cosp = 2 * (q.W * q.X + q.Y * q.Z);
        float cosr_cosp = 1 - 2 * (q.X * q.X + q.Y * q.Y);
        angles.X = MathF.Atan2(sinr_cosp, cosr_cosp);

        // Pitch (y-axis rotation)
        float sinp = 2 * (q.W * q.Y - q.Z * q.X);
        if (Math.Abs(sinp) >= 1)
            angles.Y = MathF.CopySign(MathF.PI / 2, sinp); // use 90 degrees if out of range
        else
            angles.Y = MathF.Asin(sinp);

        // Yaw (z-axis rotation)
        float siny_cosp = 2 * (q.W * q.Z + q.X * q.Y);
        float cosy_cosp = 1 - 2 * (q.Y * q.Y + q.Z * q.Z);
        angles.Z = MathF.Atan2(siny_cosp, cosy_cosp);

        return angles;
    }
}