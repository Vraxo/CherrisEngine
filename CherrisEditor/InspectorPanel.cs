using Cherris;
using ImGuiNET;
using System.Numerics;
using System.Reflection; // Required for reflection

namespace CherrisEditor;

internal class InspectorPanel
{
    private readonly Editor _editor;

    public InspectorPanel(Editor editor)
    {
        _editor = editor;
    }

    public void DrawInspectorPanel()
    {
        ImGui.Begin("Details");

        GameObject? selectedObject = _editor.GetSelectedGameObject();

        if (selectedObject is null)
        {
            ImGui.Text("No object selected.");
        }
        else
        {
            DrawGameObjectProperties(selectedObject);
        }

        ImGui.End();
    }

    private void DrawGameObjectProperties(GameObject go)
    {
        // --- Name and Transform are part of the GameObject itself ---
        ImGui.Text($"Selected: {go.Name}");
        ImGui.Separator();

        if (ImGui.CollapsingHeader("Transform", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawTransformControl(go.Transform);
        }

        // --- Loop through all attached components ---
        foreach (var component in go.Components)
        {
            ImGui.Separator();
            string componentName = component.GetType().Name;

            if (ImGui.CollapsingHeader(componentName, ImGuiTreeNodeFlags.DefaultOpen))
            {
                // Use pattern matching to call the correct UI drawer
                switch (component)
                {
                    case MeshRenderer mr:
                        DrawMeshRendererComponent(mr);
                        break;
                    case Camera cam:
                        DrawCameraComponent(cam);
                        break;
                    case Script script: // This will handle PlayerController, Spinner, etc.
                        DrawScriptComponent(script);
                        break;
                    default:
                        // Fallback for components with no custom inspector (e.g., Skybox)
                        ImGui.Text($"No custom inspector for {componentName}.");
                        break;
                }
            }
        }
    }

    private void DrawTransformControl(Transform transform)
    {
        if (!ImGui.BeginTable("TransformTable", 2, ImGuiTableFlags.Resizable))
            return;

        ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthFixed, 80.0f);
        ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);

        // Position
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.Text("Position");
        ImGui.TableSetColumnIndex(1);
        Vector3 position = transform.Position;
        if (DrawVector3Control("Position", ref position))
        {
            transform.Position = position;
        }

        // Rotation
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.Text("Rotation");
        ImGui.TableSetColumnIndex(1);
        Vector3 eulerDegrees = EngineMath.ToEulerAngles(transform.Rotation) * (180.0f / MathF.PI);
        if (DrawVector3Control("Rotation", ref eulerDegrees))
        {
            Vector3 eulerRadians = eulerDegrees * (MathF.PI / 180.0f);
            transform.Rotation = Quaternion.CreateFromYawPitchRoll(eulerRadians.Y, eulerRadians.X, eulerRadians.Z);
        }

        // Scale
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.Text("Scale");
        ImGui.TableSetColumnIndex(1);
        Vector3 scale = transform.Scale;
        if (DrawVector3Control("Scale", ref scale))
        {
            transform.Scale = scale;
        }

        ImGui.EndTable();
    }

    private void DrawMeshRendererComponent(MeshRenderer mr)
    {
        if (!ImGui.BeginTable("MeshRendererTable", 2, ImGuiTableFlags.Resizable)) return;
        ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthFixed, 80.0f);
        ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);

        // Tiling
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.Text("Tiling");
        ImGui.TableSetColumnIndex(1);
        ImGui.PushItemWidth(-1.0f);
        var tiling = mr.TextureTiling;
        if (ImGui.DragFloat2("##Tiling", ref tiling, 0.1f))
        {
            mr.TextureTiling = tiling;
        }
        ImGui.PopItemWidth();

        // Emissive Color
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.Text("Emissive");
        ImGui.TableSetColumnIndex(1);
        ImGui.PushItemWidth(-1.0f);
        var emissive = mr.EmissiveColor;
        if (ImGui.ColorEdit3("##Emissive", ref emissive))
        {
            mr.EmissiveColor = emissive;
        }
        ImGui.PopItemWidth();

        ImGui.EndTable();
    }

    private void DrawCameraComponent(Camera cam)
    {
        if (!ImGui.BeginTable("CameraTable", 2, ImGuiTableFlags.Resizable)) return;
        ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthFixed, 80.0f);
        ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);

        // Field of View
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.Text("Field of View");
        ImGui.TableSetColumnIndex(1);
        ImGui.PushItemWidth(-1.0f);
        float fov = cam.FieldOfView;
        if (ImGui.DragFloat("##FOV", ref fov, 1.0f, 1.0f, 179.0f))
        {
            cam.FieldOfView = fov;
        }
        ImGui.PopItemWidth();

        // Near Plane
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.Text("Near Plane");
        ImGui.TableSetColumnIndex(1);
        ImGui.PushItemWidth(-1.0f);
        float near = cam.NearClipPlane;
        if (ImGui.DragFloat("##NearPlane", ref near, 0.01f, 0.01f, 1000.0f))
        {
            cam.NearClipPlane = near;
        }
        ImGui.PopItemWidth();

        // Far Plane
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.Text("Far Plane");
        ImGui.TableSetColumnIndex(1);
        ImGui.PushItemWidth(-1.0f);
        float far = cam.FarClipPlane;
        if (ImGui.DragFloat("##FarPlane", ref far, 1.0f, 1.0f, 5000.0f))
        {
            cam.FarClipPlane = far;
        }
        ImGui.PopItemWidth();

        ImGui.EndTable();
    }

    private void DrawScriptComponent(Script script)
    {
        if (!ImGui.BeginTable(script.GetType().Name + "Table", 2, ImGuiTableFlags.Resizable)) return;
        ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthFixed, 120.0f);
        ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);

        var properties = script.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var prop in properties)
        {
            if (!prop.CanRead || !prop.CanWrite) continue;

            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.Text(prop.Name);
            ImGui.TableSetColumnIndex(1);
            ImGui.PushItemWidth(-1.0f);

            object currentValue = prop.GetValue(script);

            // Generate UI based on property type
            if (prop.PropertyType == typeof(float))
            {
                float val = (float)currentValue;
                if (ImGui.DragFloat($"##{prop.Name}", ref val, 0.01f))
                {
                    prop.SetValue(script, val);
                }
            }
            else if (prop.PropertyType == typeof(bool))
            {
                bool val = (bool)currentValue;
                if (ImGui.Checkbox($"##{prop.Name}", ref val))
                {
                    prop.SetValue(script, val);
                }
            }
            else
            {
                ImGui.Text(currentValue.ToString());
            }

            ImGui.PopItemWidth();
        }

        ImGui.EndTable();
    }

    private static bool DrawVector3Control(string label, ref Vector3 values)
    {
        bool valueChanged = false;
        ImGui.PushID(label);

        var style = ImGui.GetStyle();
        float availableWidth = ImGui.GetContentRegionAvail().X;
        float totalLabelWidth = ImGui.CalcTextSize("X").X + ImGui.CalcTextSize("Y").X + ImGui.CalcTextSize("Z").X;
        float totalSpacingWidth = style.ItemSpacing.X * 5;
        float totalInputWidth = availableWidth - totalLabelWidth - totalSpacingWidth;
        float itemWidth = totalInputWidth / 3.0f;

        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.2f, 0.2f, 1.0f));
        ImGui.Text("X");
        ImGui.PopStyleColor();
        ImGui.SameLine();
        ImGui.PushItemWidth(itemWidth);
        if (ImGui.DragFloat($"##{label}X", ref values.X, 0.1f)) valueChanged = true;
        ImGui.PopItemWidth();
        ImGui.SameLine();

        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.2f, 0.8f, 0.2f, 1.0f));
        ImGui.Text("Y");
        ImGui.PopStyleColor();
        ImGui.SameLine();
        ImGui.PushItemWidth(itemWidth);
        if (ImGui.DragFloat($"##{label}Y", ref values.Y, 0.1f)) valueChanged = true;
        ImGui.PopItemWidth();
        ImGui.SameLine();

        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.2f, 0.3f, 0.8f, 1.0f));
        ImGui.Text("Z");
        ImGui.PopStyleColor();
        ImGui.SameLine();
        ImGui.PushItemWidth(itemWidth);
        if (ImGui.DragFloat($"##{label}Z", ref values.Z, 0.1f)) valueChanged = true;
        ImGui.PopItemWidth();
        ImGui.PopID();

        return valueChanged;
    }
}