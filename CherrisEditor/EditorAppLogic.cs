using Cherris;
using ImGuiNET;
using System.Numerics;

namespace CherrisEditor;

public class EditorAppLogic
{
    private readonly Editor _editor;

    private readonly InspectorPanel _inspectorPanel;

    public EditorAppLogic(Editor editor)
    {
        _editor = editor;
        _inspectorPanel = new(_editor);
    }

    public Action<float> DrawUI()
    {
        return (deltaTime) =>
        {
            SetupDockspace();
            DrawHierarchyPanel();
            DrawConsolePanel();
            _inspectorPanel.DrawInspectorPanel();
        };
    }

    private static void SetupDockspace()
    {
        ImGuiViewportPtr viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.Pos);
        ImGui.SetNextWindowSize(viewport.Size);
        ImGui.SetNextWindowViewport(viewport.ID);

        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0.0f);

        ImGuiWindowFlags windowFlags = 
              ImGuiWindowFlags.NoTitleBar
            | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.NoResize
            | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoBringToFrontOnFocus
            | ImGuiWindowFlags.NoNavFocus
            | ImGuiWindowFlags.MenuBar;

        ImGui.Begin("MainDockspace", windowFlags);
        ImGui.PopStyleVar(2);

        uint dockspaceId = ImGui.GetID("MyDockSpace");
        ImGui.DockSpace(dockspaceId, Vector2.Zero, ImGuiDockNodeFlags.PassthruCentralNode);

        DrawMainMenuBar();

        ImGui.End();
    }

    private static void DrawMainMenuBar()
    {
        if (!ImGui.BeginMenuBar())
        {
            return;
        }

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

    private void DrawHierarchyPanel()
    {
        ImGui.Begin("Hierarchy");

        foreach (GameObject go in _editor.SceneManager.GameObjects)
        {
            bool isSelected = _editor.GetSelectedGameObject() == go;
            
            if (ImGui.Selectable(go.Name, isSelected))
            {
                _editor.SetSelectedGameObject(go);
            }
        }

        ImGui.End();
    }

    private static void DrawConsolePanel()
    {
        ImGui.Begin("Console");
        ImGui.Text("Log messages will appear here...");
        ImGui.End();
    }

    public void UpdateEditorLogic(float deltaTime)
    {
        HandleObjectSelection();
        HandleMovement(deltaTime);
    }

    private void HandleObjectSelection()
    {
        if (!Input.WasMouseButtonPressed(MouseButton.Left) || ImGui.GetIO().WantCaptureMouse)
        {
            return;
        }

        Ray ray = _editor.CreateRayFromMouse();
        GameObject? closestObject = null;
        float closestDistance = float.MaxValue;

        foreach (GameObject gameObject in _editor.SceneManager.GameObjects)
        {
            if (gameObject.GetComponent<Skybox>() is not null || gameObject.GetComponent<Camera>() is not null)
            {
                continue;
            }

            BoundingBox aabb = gameObject.GetWorldSpaceAABB();

            if (!ray.Intersects(aabb, out float distance))
            {
                continue;
            }

            if (distance >= closestDistance)
            {
                continue;
            }

            closestDistance = distance;
            closestObject = gameObject;
        }

        _editor.SetSelectedGameObject(closestObject);

        if (closestObject is null)
        {
            return;
        }

        Console.WriteLine($"Selected '{closestObject.Name}'");
    }

    private void HandleMovement(float deltaTime)
    {
        GameObject? selectedGameObject = _editor.GetSelectedGameObject();

        if (selectedGameObject is null)
        {
            return;
        }

        const float moveSpeed = 2.0f;
        Vector3 moveDirection = Vector3.Zero;
        bool shiftHeld = Input.IsKeyDown(Key.ShiftLeft) || Input.IsKeyDown(Key.ShiftRight);

        if (Input.IsKeyDown(Key.Left))
        {
            moveDirection.X -= 1;
        }

        if (Input.IsKeyDown(Key.Right))
        {
            moveDirection.X += 1;
        }

        if (shiftHeld)
        {
            if (Input.IsKeyDown(Key.Up))
            {
                moveDirection.Y += 1;
            }

            if (Input.IsKeyDown(Key.Down))
            {
                moveDirection.Y -= 1;
            }
        }
        else
        {
            if (Input.IsKeyDown(Key.Up))
            {
                moveDirection.Z -= 1;
            }

            if (Input.IsKeyDown(Key.Down))
            {
                moveDirection.Z += 1;
            }
        }

        if (moveDirection != Vector3.Zero)
        {
            selectedGameObject.Transform.Position += Vector3.Normalize(moveDirection) * moveSpeed * deltaTime;
        }
    }
}