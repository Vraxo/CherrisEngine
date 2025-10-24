using Cherris;
using ImGuiNET;
using System;
using System.Numerics;

namespace CherrisEditor;

public class EditorAppLogic
{
    private readonly Editor _editor;
    private float _sliderValue = 50f;

    public EditorAppLogic(Editor editor)
    {
        _editor = editor;
    }

    public void DrawUI(float deltaTime)
    {
        ImGui.Begin("Editor");
        ImGui.Text($"Hello from ImGui.NET on Cherris!");

        ImGui.Separator();

        if (ImGui.Button("Click Me!"))
        {
            Console.WriteLine("Button clicked!");
        }
        ImGui.SameLine();
        ImGui.Text("A UI Label");

        ImGui.SliderFloat("Value", ref _sliderValue, 0, 100);
        ImGui.End();
    }

    public void UpdateEditorLogic(float deltaTime)
    {
        if (Input.WasMouseButtonPressed(MouseButton.Left))
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
}