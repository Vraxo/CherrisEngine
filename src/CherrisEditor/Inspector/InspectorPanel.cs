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

    /// <summary>Draw a foldout, icon, enable checkbox and title in Unity's header order.</summary>
    /// <param name="component">The component the toggle switches, or null for the Transform.</param>
    private bool DrawComponentHeader(string title, IntPtr icon, out bool isOpen, Component? component = null)
    {
        const float iconSize = 20.0f;
        float frameHeight = ImGui.GetFrameHeight();

        ImGui.BeginGroup();

        // NoTreePushOnOpen keeps the properties at their usual indentation and gives the foldout its own hit area.
        isOpen = ImGui.TreeNodeEx("##fold", ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.NoTreePushOnOpen);
        ImGui.SameLine();

        IntPtr displayIcon = icon != IntPtr.Zero ? icon : _textureManager.GetTexture("File");
        ImGui.Image(displayIcon, new Vector2(iconSize, iconSize));
        ImGui.SameLine();

        if (component is not null)
        {
            bool enabled = component.Enabled;
            if (ImGui.Checkbox("##enabled", ref enabled))
            {
                component.Enabled = enabled;

                if (_editor.SceneManager.ActiveScene is { } scene)
                {
                    scene.FindMainComponents();
                    scene.IsDirty = true;
                }
            }
        }
        else
        {
            // The Transform is always enabled; reserve the same space for aligned component names.
            ImGui.Dummy(new Vector2(frameHeight, frameHeight));
        }

        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(title);
        if (ImGui.IsItemClicked())
        {
            // Match the previous collapsing header: clicking its title also opens or closes it.
            isOpen = !isOpen;
            ImGui.GetStateStorage().SetInt(ImGui.GetID("##fold"), isOpen ? 1 : 0);
        }

        ImGui.EndGroup();
        return isOpen;
    }

    private static string SplitPascalCase(string input)
    {
        return Regex.Replace(input, "(?<!^)([A-Z])", " $1");
    }
}