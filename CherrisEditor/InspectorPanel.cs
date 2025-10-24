using Cherris;
using ImGuiNET;
using System.Numerics;
using System.Reflection;
using System.Collections.Generic;
using System;
using System.Runtime.InteropServices;
using System.IO;
using System.Linq;

namespace CherrisEditor;

internal class InspectorPanel
{
    private readonly Editor _editor;
    private readonly EditorTextureManager _textureManager;
    private readonly Dictionary<Type, object> _defaultComponentCache = new();

    public InspectorPanel(Editor editor, EditorTextureManager textureManager)
    {
        _editor = editor;
        _textureManager = textureManager;
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
        ImGui.Text($"Selected: {go.Name}");
        ImGui.Separator();

        if (ImGui.CollapsingHeader("Transform", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawTransformControl(go.Transform);
        }

        foreach (var component in go.Components)
        {
            ImGui.Separator();
            string componentName = component.GetType().Name;

            if (ImGui.CollapsingHeader(componentName, ImGuiTreeNodeFlags.DefaultOpen))
            {
                switch (component)
                {
                    case MeshRenderer mr:
                        DrawMeshRendererComponent(mr);
                        break;
                    case Camera cam:
                        DrawCameraComponent(cam);
                        break;
                    case Script script:
                        DrawScriptComponent(script);
                        break;
                    default:
                        ImGui.Text($"No custom inspector for {componentName}.");
                        break;
                }
            }
        }

        DrawAddComponentButton(go);
    }

    private void DrawAddComponentButton(GameObject go)
    {
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        float availableWidth = ImGui.GetContentRegionAvail().X;
        float buttonWidth = 120.0f;
        float buttonPosX = (availableWidth - buttonWidth) * 0.5f;
        if (buttonPosX > 0)
        {
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + buttonPosX);
        }

        if (ImGui.Button("Add Component", new Vector2(buttonWidth, 0)))
        {
            ImGui.OpenPopup("AddComponentPopup");
        }

        if (ImGui.BeginPopup("AddComponentPopup"))
        {
            // Add Camera
            if (go.GetComponent<Camera>() == null)
            {
                if (ImGui.MenuItem("Camera"))
                {
                    go.AddComponent(new Camera());
                    ImGui.CloseCurrentPopup();
                }
            }

            ImGui.Separator();

            // Add Scripts
            foreach (var scriptType in _editor.AvailableScriptTypes)
            {
                if (!go.Components.Any(c => c.GetType() == scriptType))
                {
                    if (ImGui.MenuItem(scriptType.Name))
                    {
                        var newComponent = (Component)Activator.CreateInstance(scriptType);
                        go.AddComponent(newComponent);
                        ImGui.CloseCurrentPopup();
                    }
                }
            }

            ImGui.EndPopup();
        }
    }


    private void DrawTransformControl(Transform transform)
    {
        if (!ImGui.BeginTable("TransformTable", 3, ImGuiTableFlags.Resizable)) return;

        ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthFixed, 80.0f);
        ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("##Reset", ImGuiTableColumnFlags.WidthFixed, 25.0f);

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
        ImGui.TableSetColumnIndex(2);
        if (ImGui.Button("R##Pos")) transform.Position = Vector3.Zero;

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
        ImGui.TableSetColumnIndex(2);
        if (ImGui.Button("R##Rot")) transform.Rotation = Quaternion.Identity;


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
        ImGui.TableSetColumnIndex(2);
        if (ImGui.Button("R##Sca")) transform.Scale = Vector3.One;

        ImGui.EndTable();
    }

    private unsafe void DrawMeshRendererComponent(MeshRenderer mr)
    {
        if (!ImGui.BeginTable("MRTable", 3, ImGuiTableFlags.Resizable)) return;
        ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthFixed, 80.0f);
        ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("##Reset", ImGuiTableColumnFlags.WidthFixed, 25.0f);

        // Texture
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.Text("Texture");
        ImGui.TableSetColumnIndex(1);

        IntPtr textureHandle = IntPtr.Zero;
        if (mr.Texture?.GetBackendHandle() is int handle && handle != 0)
        {
            textureHandle = (IntPtr)handle;
        }
        else
        {
            textureHandle = _textureManager.GetTexture("File");
        }

        // Use flipped UVs because game textures are loaded upside-down for OpenGL.
        ImGui.ImageButton("TextureThumb", textureHandle, new Vector2(64, 64), new Vector2(0, 1), new Vector2(1, 0));

        if (ImGui.BeginDragDropTarget())
        {
            ImGuiPayloadPtr payload = ImGui.AcceptDragDropPayload("ASSET_PATH_TEXTURE");
            if (payload.NativePtr != null)
            {
                string path = Marshal.PtrToStringAnsi(payload.Data);
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    string textureName = Path.GetFileNameWithoutExtension(path);
                    var newTexture = _editor.ResourceManager.GetTexture(textureName);
                    mr.TextureName = textureName;
                    mr.Texture = newTexture;
                }
            }
            ImGui.EndDragDropTarget();
        }

        ImGui.SameLine();
        ImGui.Text(mr.TextureName);

        ImGui.TableSetColumnIndex(2);
        if (ImGui.Button("R##Texture"))
        {
            mr.TextureName = "White";
            mr.Texture = _editor.ResourceManager.GetTexture("White");
        }


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
        ImGui.TableSetColumnIndex(2);
        if (ImGui.Button("R##Tiling")) mr.TextureTiling = Vector2.One;

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
        ImGui.TableSetColumnIndex(2);
        if (ImGui.Button("R##Emissive")) mr.EmissiveColor = Vector3.Zero;

        ImGui.EndTable();
    }

    private void DrawCameraComponent(Camera cam)
    {
        if (!ImGui.BeginTable("CamTable", 3, ImGuiTableFlags.Resizable)) return;
        ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthFixed, 80.0f);
        ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("##Reset", ImGuiTableColumnFlags.WidthFixed, 25.0f);

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
        ImGui.TableSetColumnIndex(2);
        if (ImGui.Button("R##FOV")) cam.FieldOfView = (float)GetDefaultValue(typeof(Camera), "FieldOfView");

        // Near & Far Planes... (repeat pattern for other properties)
        ImGui.EndTable();
    }

    private void DrawScriptComponent(Script script)
    {
        Type scriptType = script.GetType();
        if (!ImGui.BeginTable(scriptType.Name + "Table", 3, ImGuiTableFlags.Resizable)) return;
        ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthFixed, 120.0f);
        ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("##Reset", ImGuiTableColumnFlags.WidthFixed, 25.0f);

        var properties = scriptType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var prop in properties)
        {
            if (!prop.CanRead || !prop.CanWrite) continue;

            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.Text(prop.Name);
            ImGui.TableSetColumnIndex(1);
            ImGui.PushItemWidth(-1.0f);

            object currentValue = prop.GetValue(script);
            bool valueChanged = false;

            if (prop.PropertyType == typeof(float))
            {
                float val = (float)currentValue;
                if (ImGui.DragFloat($"##{prop.Name}", ref val, 0.01f))
                {
                    prop.SetValue(script, val);
                    valueChanged = true;
                }
            }
            // ... other types

            ImGui.PopItemWidth();

            // Reset Button Column
            ImGui.TableSetColumnIndex(2);
            if (ImGui.Button($"R##{prop.Name}"))
            {
                object defaultValue = GetDefaultValue(scriptType, prop.Name);
                if (defaultValue != null)
                {
                    prop.SetValue(script, defaultValue);
                }
            }
        }

        ImGui.EndTable();
    }

    /// <summary>
    /// Gets the default value of a property from a cached default instance of a component.
    /// </summary>
    private object GetDefaultValue(Type componentType, string propertyName)
    {
        if (!_defaultComponentCache.TryGetValue(componentType, out object defaultInstance))
        {
            try
            {
                // Create and cache a new default instance if not found.
                defaultInstance = Activator.CreateInstance(componentType);
                _defaultComponentCache[componentType] = defaultInstance;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Inspector] Could not create default instance of {componentType.Name}: {ex.Message}");
                return null;
            }
        }

        return componentType.GetProperty(propertyName)?.GetValue(defaultInstance);
    }

    private static bool DrawVector3Control(string label, ref Vector3 values)
    {
        // ... (this function remains unchanged)
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