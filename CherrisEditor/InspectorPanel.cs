using Cherris;
using ImGuiNET;
using System.Numerics;
using System.Reflection;
using System.Collections.Generic;
using System;
using System.Runtime.InteropServices;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CherrisEditor.Inspectors;

namespace CherrisEditor;

internal class InspectorPanel
{
    private readonly Editor _editor;
    private readonly EditorTextureManager _textureManager;
    private readonly Dictionary<Type, IComponentInspector> _customInspectors = new();
    private readonly DefaultInspector _defaultInspector;
    private readonly TransformInspector _transformInspector;
    private string _newScriptName = "";
    private string _componentSearchText = "";

    public InspectorPanel(Editor editor, EditorTextureManager textureManager)
    {
        _editor = editor;
        _textureManager = textureManager;
        _defaultInspector = new DefaultInspector();
        _transformInspector = new TransformInspector();
        RegisterCustomInspectors(editor, textureManager);
    }

    private void RegisterCustomInspectors(Editor editor, EditorTextureManager textureManager)
    {
        var inspectorTypes = Assembly.GetExecutingAssembly().GetTypes()
            .Where(t => t.IsDefined(typeof(CustomInspectorAttribute), false) && typeof(IComponentInspector).IsAssignableFrom(t));

        foreach (var inspectorType in inspectorTypes)
        {
            var attribute = (CustomInspectorAttribute)inspectorType.GetCustomAttribute(typeof(CustomInspectorAttribute), false);
            try
            {
                object[] constructorArgs = { editor, textureManager };
                var constructor = inspectorType.GetConstructor(new[] { typeof(Editor), typeof(EditorTextureManager) });

                IComponentInspector instance;
                if (constructor != null)
                {
                    instance = (IComponentInspector)Activator.CreateInstance(inspectorType, constructorArgs);
                }
                else
                {
                    instance = (IComponentInspector)Activator.CreateInstance(inspectorType);
                }

                _customInspectors[attribute.InspectedType] = instance;
                Console.WriteLine($"[Inspector] Registered custom inspector for '{attribute.InspectedType.Name}'");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Inspector] Failed to register custom inspector '{inspectorType.Name}': {ex.Message}");
            }
        }
    }

    public void DrawInspectorPanel()
    {
        ImGui.Begin("Details");
        GameObject? selectedObject = _editor.GetSelectedGameObject();
        if (selectedObject != null)
        {
            DrawGameObjectProperties(selectedObject);
        }
        else
        {
            ImGui.Text("No object selected.");
        }
        ImGui.End();
    }

    private void DrawGameObjectProperties(GameObject go)
    {
        string name = go.Name;
        if (ImGui.InputText("##GameObjectName", ref name, 256, ImGuiInputTextFlags.EnterReturnsTrue))
        {
            go.Name = name;
        }
        ImGui.Separator();

        ImGui.PushID("TransformComponent");
        DrawComponentHeader("Transform", typeof(Transform));
        if (ImGui.CollapsingHeader("Transform", ImGuiTreeNodeFlags.DefaultOpen))
        {
            _transformInspector.Draw(go.Transform);
        }
        ImGui.PopID();


        foreach (var component in go.Components.ToList())
        {
            ImGui.Separator();
            string componentName = SplitPascalCase(component.GetType().Name);

            ImGui.PushID(component.GetHashCode());
            DrawComponentHeader(componentName, component.GetType());
            bool headerOpen = ImGui.CollapsingHeader(componentName, ImGuiTreeNodeFlags.DefaultOpen);

            if (ImGui.BeginPopupContextItem())
            {
                if (ImGui.MenuItem("Remove Component"))
                {
                    go.RemoveComponent(component);
                    ImGui.EndPopup();
                    ImGui.PopID();
                    return;
                }
                ImGui.EndPopup();
            }

            if (headerOpen)
            {
                if (_customInspectors.TryGetValue(component.GetType(), out var customInspector))
                {
                    customInspector.Draw(component);
                }
                else
                {
                    _defaultInspector.Draw(component);
                }
            }
            ImGui.PopID();
        }

        DrawAddComponentButton(go);
    }

    private void DrawComponentHeader(string headerName, Type componentType)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4, 4));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(4, 2));

        string textureKey = $"Component_{componentType.Name}";
        IntPtr icon = _textureManager.GetTexture(textureKey);

        // Fallback for scripts
        if (icon == IntPtr.Zero && typeof(Script).IsAssignableFrom(componentType))
        {
            icon = _textureManager.GetTexture("Component_Script");
        }

        if (icon != IntPtr.Zero)
        {
            ImGui.Image(icon, new Vector2(20, 20));
            ImGui.SameLine();
        }

        // The header text is now part of the CollapsingHeader, not drawn here.

        ImGui.PopStyleVar(2);
    }

    private void DrawAddComponentButton(GameObject go)
    {
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        float availableWidth = ImGui.GetContentRegionAvail().X;
        float buttonWidth = 120.0f;
        float buttonPosX = (availableWidth - buttonWidth) * 0.5f;
        if (buttonPosX > 0) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + buttonPosX);

        if (ImGui.Button("Add Component", new Vector2(buttonWidth, 0)))
        {
            _componentSearchText = ""; // Reset search when opening the popup
            ImGui.OpenPopup("AddComponentPopup");
        }

        if (ImGui.BeginPopup("AddComponentPopup"))
        {
            ImGui.PushItemWidth(-1);
            ImGui.InputTextWithHint("##ComponentSearch", "Search...", ref _componentSearchText, 100);
            ImGui.PopItemWidth();
            ImGui.Separator();

            bool searchIsActive = !string.IsNullOrWhiteSpace(_componentSearchText);

            // Built-in components
            if (go.GetComponent<Camera>() == null && (!searchIsActive || "Camera".Contains(_componentSearchText, StringComparison.OrdinalIgnoreCase)))
            {
                if (ImGui.MenuItem("Camera"))
                {
                    go.AddComponent(new Camera());
                    ImGui.CloseCurrentPopup();
                }
            }

            ImGui.Separator();

            // Scripts
            var filteredTypes = searchIsActive
                ? _editor.AvailableScriptTypes.Where(t => t.Name.Contains(_componentSearchText, StringComparison.OrdinalIgnoreCase))
                : _editor.AvailableScriptTypes;

            foreach (var scriptType in filteredTypes)
            {
                if (!go.Components.Any(c => c.GetType() == scriptType) && ImGui.MenuItem(SplitPascalCase(scriptType.Name)))
                {
                    var newComponent = (Script)Activator.CreateInstance(scriptType);
                    go.AddComponent(newComponent);

                    if (_editor.State != EditorState.Playing)
                    {
                        newComponent.Enabled = false;
                    }

                    ImGui.CloseCurrentPopup();
                }
            }

            ImGui.Separator();
            ImGui.Text("New Script");
            ImGui.PushItemWidth(-1.0f);
            if (ImGui.InputText("##NewScriptName", ref _newScriptName, 100, ImGuiInputTextFlags.EnterReturnsTrue))
            {
                if (!string.IsNullOrWhiteSpace(_newScriptName))
                {
                    string scriptName = _newScriptName;
                    _editor.CreateAndCompileScript(scriptName);

                    Type? newScriptType = _editor.AvailableScriptTypes.FirstOrDefault(t => t.Name == scriptName);
                    if (newScriptType != null)
                    {
                        var newComponent = (Script)Activator.CreateInstance(newScriptType);
                        go.AddComponent(newComponent);
                        if (_editor.State != EditorState.Playing)
                        {
                            newComponent.Enabled = false;
                        }
                    }

                    _newScriptName = "";
                    ImGui.CloseCurrentPopup();
                }
            }
            ImGui.PopItemWidth();

            ImGui.EndPopup();
        }
    }

    private static string SplitPascalCase(string input) => Regex.Replace(input, "(?<!^)([A-Z])", " $1");
}