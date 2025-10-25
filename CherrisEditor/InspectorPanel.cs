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
    private readonly Dictionary<Type, IComponentInspector> _customInspectors = new();
    private readonly DefaultInspector _defaultInspector;
    private readonly TransformInspector _transformInspector;

    public InspectorPanel(Editor editor, EditorTextureManager textureManager)
    {
        _editor = editor;
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

        if (ImGui.CollapsingHeader("Transform", ImGuiTreeNodeFlags.DefaultOpen))
        {
            _transformInspector.Draw(go.Transform);
        }

        foreach (var component in go.Components.ToList())
        {
            ImGui.Separator();
            string componentName = SplitPascalCase(component.GetType().Name);
            bool headerOpen = ImGui.CollapsingHeader(componentName, ImGuiTreeNodeFlags.DefaultOpen);

            if (ImGui.BeginPopupContextItem())
            {
                if (ImGui.MenuItem("Remove Component"))
                {
                    go.RemoveComponent(component);
                    ImGui.EndPopup();
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
        if (buttonPosX > 0) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + buttonPosX);

        if (ImGui.Button("Add Component", new Vector2(buttonWidth, 0)))
        {
            ImGui.OpenPopup("AddComponentPopup");
        }

        if (ImGui.BeginPopup("AddComponentPopup"))
        {
            if (go.GetComponent<Camera>() == null && ImGui.MenuItem("Camera"))
            {
                go.AddComponent(new Camera());
                ImGui.CloseCurrentPopup();
            }
            ImGui.Separator();
            foreach (var scriptType in _editor.AvailableScriptTypes)
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
            ImGui.EndPopup();
        }
    }

    private static string SplitPascalCase(string input) => Regex.Replace(input, "(?<!^)([A-Z])", " $1");
}