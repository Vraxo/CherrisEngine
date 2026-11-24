using Cherris.Components;
using Cherris.Core;
using CherrisEditor.Inspectors;
using CherrisEditor.UI.Inspector;
using CherrisEditor.Undo;
using ImGuiNET;
using System.Numerics;
using System.Text.RegularExpressions;

namespace CherrisEditor.UI;

internal class InspectorPanel
{
    private readonly Editor _editor;
    private readonly EditorTextureManager _textureManager;
    private readonly DefaultInspector _defaultInspector;
    private readonly TransformInspector _transformInspector;
    private readonly ComponentAdderPresenter _componentAdder;

    public InspectorPanel(Editor editor, EditorTextureManager textureManager, HistoryManager history)
    {
        _editor = editor;
        _textureManager = textureManager;
        _defaultInspector = new DefaultInspector(editor, textureManager, history);
        _transformInspector = new TransformInspector(textureManager, history);

        var menuBuilder = new ComponentTypeMenuBuilder();
        _componentAdder = new ComponentAdderPresenter(menuBuilder, editor.SceneManager, editor.ScriptManager);
        ConfigureMenuBuilder(editor, menuBuilder);
    }

    private void ConfigureMenuBuilder(Editor editor, ComponentTypeMenuBuilder builder)
    {
        builder.IsInEditMode = editor.State == EditorState.Editing;
        builder.ScriptCreator = editor.CreateAndCompileScript;
    }

    public void DrawInspectorPanel()
    {
        ImGui.Begin("Inspector");

        GameObject? selectedObject = _editor.GetSelectedGameObject();
        if (selectedObject is not null)
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
        DrawNameField(go);
        ImGui.Separator();

        DrawTransform(go);
        DrawComponents(go);
        DrawAddComponent(go);
    }

    private void DrawNameField(GameObject go)
    {
        string name = go.Name;
        if (ImGui.InputText("##GameObjectName", ref name, 256, ImGuiInputTextFlags.EnterReturnsTrue))
        {
            go.Name = name;
            _editor.SceneManager.ActiveScene!.IsDirty = true;
        }
    }

    private void DrawTransform(GameObject go)
    {
        ImGui.PushID("TransformComponent");
        IntPtr transformIcon = _textureManager.GetTexture("Component_Transform");

        if (DrawComponentHeader("Transform", transformIcon, out _))
        {
            _transformInspector.Draw(go.Transform);
        }

        ImGui.PopID();
    }

    private void DrawComponents(GameObject go)
    {
        foreach (var component in go.Components.ToList())
        {
            if (component is Transform)
            {
                continue;
            }

            ImGui.Separator();
            DrawComponent(go, component);
        }
    }

    private void DrawComponent(GameObject go, Component component)
    {
        ImGui.PushID(component.GetHashCode());

        string componentName = SplitPascalCase(component.GetType().Name);
        IntPtr icon = ResolveComponentIcon(component);

        if (DrawComponentHeader(componentName, icon, out _, component))
        {
            if (_defaultInspector.Draw(component))
            {
                _editor.SceneManager.ActiveScene!.IsDirty = true;
            }
        }

        DrawComponentContextMenu(go, component);
        ImGui.PopID();
    }

    private IntPtr ResolveComponentIcon(Component component)
    {
        string textureKey = $"Component_{component.GetType().Name}";

        if (component is Light)
        {
            textureKey = "Component_Light";
        }
        else if (component is AudioSource or AudioListener)
        {
            textureKey = "Component_AudioSource";
        }
        else if (component is Script)
        {
            textureKey = "Component_Script";
        }

        IntPtr icon = _textureManager.GetTexture(textureKey);
        if (icon == IntPtr.Zero && component is Script)
        {
            icon = _textureManager.GetTexture("Component_Script");
        }

        return icon == IntPtr.Zero ? _textureManager.GetTexture("File") : icon;
    }

    private void DrawComponentContextMenu(GameObject go, Component component)
    {
        if (!ImGui.BeginPopupContextItem("ComponentContextMenu"))
        {
            return;
        }

        if (ImGui.MenuItem("Remove Component"))
        {
            go.RemoveComponent(component);
            _editor.SceneManager.ActiveScene!.IsDirty = true;
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    private void DrawAddComponent(GameObject go)
    {
        _componentAdder.Draw(go);
    }

    /// <summary>
    /// The component header: Unity's enable checkbox between the icon and the title, then the foldout. The
    /// Transform has nothing to switch off, so it passes no component and the slot stays empty.
    /// </summary>
    /// <param name="component">The component the toggle switches, or null for the Transform.</param>
    private bool DrawComponentHeader(string title, IntPtr icon, out bool isOpen, Component? component = null)
    {
        var style = ImGui.GetStyle();
        float iconSize = 20.0f;
        float frameHeight = ImGui.GetFrameHeight();
        float yPadding = (frameHeight - iconSize) * 0.5f;

        ImGui.BeginGroup();

        var startPos = ImGui.GetCursorPos();
        ImGui.SetCursorPosY(startPos.Y + yPadding);

        IntPtr displayIcon = icon != IntPtr.Zero ? icon : _textureManager.GetTexture("File");
        ImGui.Image(displayIcon, new Vector2(iconSize, iconSize));

        // The header starts right after the icon, and takes the rest of the row.
        float headerX = startPos.X + iconSize + style.ItemSpacing.X;
        float headerWidth = ImGui.GetContentRegionAvail().X - (iconSize + style.ItemSpacing.X);

        if (component is not null)
        {
            // Its own item, so a click on the toggle does not also fold the section. Off stops the component
            // without taking it off the object (Component.Enabled), and is saved with the scene.
            bool enabled = component.Enabled;
            ImGui.SameLine();

            if (ImGui.Checkbox("##enabled", ref enabled))
            {
                component.Enabled = enabled;

                if (_editor.SceneManager.ActiveScene is { } scene)
                {
                    scene.FindMainComponents();
                    scene.IsDirty = true;
                }
            }

            // A checkbox is a square frame, so the title moves over by the frame's width.
            headerX += frameHeight + style.ItemSpacing.X;
            headerWidth -= frameHeight + style.ItemSpacing.X;
        }

        ImGui.SetCursorPos(new Vector2(headerX, startPos.Y));
        ImGui.SetNextItemWidth(headerWidth);

        isOpen = ImGui.CollapsingHeader(title, ImGuiTreeNodeFlags.DefaultOpen);

        ImGui.EndGroup();
        return isOpen;
    }

    private static string SplitPascalCase(string input)
    {
        return Regex.Replace(input, "(?<!^)([A-Z])", " $1");
    }
}