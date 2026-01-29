using Cherris.Components;
using Cherris.Core;
using Cherris.Core.Logging;
using CherrisEditor.Inspectors;
using CherrisEditor.Undo;
using ImGuiNET;
using System.Numerics;
using System.Reflection;
using System.Text.RegularExpressions;

namespace CherrisEditor.UI;

internal class InspectorPanel
{
    private readonly Editor _editor;
    private readonly EditorTextureManager _textureManager;
    private readonly HistoryManager _history;
    private readonly Dictionary<Type, IComponentInspector> _customInspectors = [];
    private readonly DefaultInspector _defaultInspector;
    private readonly TransformInspector _transformInspector;
    private string _newScriptName = "";
    private string _componentSearchText = "";

    public InspectorPanel(Editor editor, EditorTextureManager textureManager, HistoryManager history)
    {
        _editor = editor;
        _textureManager = textureManager;
        _history = history;
        _defaultInspector = new DefaultInspector(textureManager, history);
        _transformInspector = new TransformInspector(textureManager, history);
        RegisterCustomInspectors(editor, textureManager, history);
    }

    private void RegisterCustomInspectors(Editor editor, EditorTextureManager textureManager, HistoryManager history)
    {
        var inspectorTypes = Assembly.GetExecutingAssembly().GetTypes()
            .Where(t => t.IsDefined(typeof(CustomInspectorAttribute), false) && typeof(IComponentInspector).IsAssignableFrom(t));

        foreach (var inspectorType in inspectorTypes)
        {
            var attribute = (CustomInspectorAttribute)inspectorType.GetCustomAttribute(typeof(CustomInspectorAttribute), false);
            try
            {
                IComponentInspector instance = null;

                var ctorWithAll = inspectorType.GetConstructor(new[] { typeof(Editor), typeof(EditorTextureManager), typeof(HistoryManager) });
                var ctorWithEditor = inspectorType.GetConstructor(new[] { typeof(Editor), typeof(EditorTextureManager) });
                var ctorWithHistory = inspectorType.GetConstructor(new[] { typeof(EditorTextureManager), typeof(HistoryManager) });
                var ctorWithHistoryOnly = inspectorType.GetConstructor(new[] { typeof(HistoryManager) });
                var ctorSimple = inspectorType.GetConstructor(new[] { typeof(EditorTextureManager) });
                var ctorParameterless = inspectorType.GetConstructor(Type.EmptyTypes);

                if (ctorWithAll is not null)
                {
                    instance = (IComponentInspector)Activator.CreateInstance(inspectorType, editor, textureManager, history);
                }
                else if (ctorWithEditor is not null)
                {
                    instance = (IComponentInspector)Activator.CreateInstance(inspectorType, editor, textureManager);
                }
                else if (ctorWithHistory is not null)
                {
                    instance = (IComponentInspector)Activator.CreateInstance(inspectorType, textureManager, history);
                }
                else if (ctorWithHistoryOnly is not null)
                {
                    instance = (IComponentInspector)Activator.CreateInstance(inspectorType, history);
                }
                else if (ctorSimple is not null)
                {
                    instance = (IComponentInspector)Activator.CreateInstance(inspectorType, textureManager);
                }
                else if (ctorParameterless is not null)
                {
                    instance = (IComponentInspector)Activator.CreateInstance(inspectorType);
                }

                if (instance is not null)
                {
                    _customInspectors[attribute.InspectedType] = instance;
                    Logger.Info($"[Inspector] Registered custom inspector for '{attribute.InspectedType.Name}'");
                }
                else
                {
                    Logger.Warning($"[Inspector] Could not find a suitable constructor for '{inspectorType.Name}'.");
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"[Inspector] Failed to register custom inspector '{inspectorType.Name}': {ex.Message}");
            }
        }
    }

    public void DrawInspectorPanel()
    {
        _ = ImGui.Begin("Details");
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
        string name = go.Name;
        if (ImGui.InputText("##GameObjectName", ref name, 256, ImGuiInputTextFlags.EnterReturnsTrue))
        {
            go.Name = name;
            _editor.SceneManager.ActiveScene.IsDirty = true;
        }
        ImGui.Separator();

        ImGui.PushID("TransformComponent");
        IntPtr transformIcon = _textureManager.GetTexture("Component_Transform");
        if (DrawComponentHeader("Transform", transformIcon, out _))
        {
            _ = _transformInspector.Draw(go.Transform);
        }
        ImGui.PopID();

        foreach (var component in go.Components.ToList())
        {
            ImGui.Separator();
            ImGui.PushID(component.GetHashCode());

            string componentName = SplitPascalCase(component.GetType().Name);
            string textureKey = $"Component_{component.GetType().Name}";
            if (component is Light)
            {
                textureKey = "Component_Light";
            }

            if (component is AudioSource or AudioListener)
            {
                textureKey = "Component_AudioSource";
            }

            IntPtr icon = _textureManager.GetTexture(textureKey);
            if (icon == IntPtr.Zero && typeof(Script).IsAssignableFrom(component.GetType()))
            {
                icon = _textureManager.GetTexture("Component_Script");
            }

            if (DrawComponentHeader(componentName, icon, out _))
            {
                if (_customInspectors.TryGetValue(component.GetType(), out var customInspector))
                {
                    if (customInspector.Draw(component))
                    {
                        _editor.SceneManager.ActiveScene.IsDirty = true;
                    }
                }
                else
                {
                    if (_defaultInspector.Draw(component))
                    {
                        _editor.SceneManager.ActiveScene.IsDirty = true;
                    }
                }
            }

            if (ImGui.BeginPopupContextItem("ComponentContextMenu"))
            {
                if (ImGui.MenuItem("Remove Component"))
                {
                    go.RemoveComponent(component);
                    _editor.SceneManager.ActiveScene.IsDirty = true;
                    ImGui.CloseCurrentPopup();
                    ImGui.PopID();
                    ImGui.EndPopup();
                    return;
                }
                ImGui.EndPopup();
            }

            ImGui.PopID();
        }

        DrawAddComponentButton(go);
    }

    private bool DrawComponentHeader(string title, IntPtr icon, out bool isOpen)
    {
        var style = ImGui.GetStyle();
        float iconSize = 20.0f;
        float frameHeight = ImGui.GetFrameHeight();
        float yPadding = (frameHeight - iconSize) * 0.5f;

        ImGui.BeginGroup();

        var startPos = ImGui.GetCursorPos();
        ImGui.SetCursorPosY(startPos.Y + yPadding);
        ImGui.Image(icon != IntPtr.Zero ? icon : _textureManager.GetTexture("File"), new Vector2(iconSize, iconSize));

        float headerX = startPos.X + iconSize + style.ItemSpacing.X;
        ImGui.SetCursorPos(new Vector2(headerX, startPos.Y));
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - (headerX - startPos.X));
        isOpen = ImGui.CollapsingHeader(title, ImGuiTreeNodeFlags.DefaultOpen);

        ImGui.EndGroup();
        return isOpen;
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
            _componentSearchText = "";
            ImGui.OpenPopup("AddComponentPopup");
        }

        if (ImGui.BeginPopup("AddComponentPopup"))
        {
            ImGui.PushItemWidth(-1);
            _ = ImGui.InputTextWithHint("##ComponentSearch", "Search...", ref _componentSearchText, 100);
            ImGui.PopItemWidth();
            ImGui.Separator();

            bool searchIsActive = !string.IsNullOrWhiteSpace(_componentSearchText);

            if (go.GetComponent<Camera>() is null && (!searchIsActive || "Camera".Contains(_componentSearchText, StringComparison.OrdinalIgnoreCase)))
            {
                if (ImGui.MenuItem("Camera"))
                {
                    _ = go.AddComponent(new Camera());
                    _editor.SceneManager.ActiveScene.IsDirty = true;
                    ImGui.CloseCurrentPopup();
                }
            }

            if (go.GetComponent<Light>() is null && (!searchIsActive || "Light".Contains(_componentSearchText, StringComparison.OrdinalIgnoreCase)))
            {
                if (ImGui.MenuItem("Light"))
                {
                    _ = go.AddComponent(new Light());
                    _editor.SceneManager.ActiveScene.IsDirty = true;
                    ImGui.CloseCurrentPopup();
                }
            }

            if (go.GetComponent<AudioSource>() is null && (!searchIsActive || "Audio Source".Contains(_componentSearchText, StringComparison.OrdinalIgnoreCase)))
            {
                if (ImGui.MenuItem("Audio Source"))
                {
                    _ = go.AddComponent(new AudioSource());
                    _editor.SceneManager.ActiveScene.IsDirty = true;
                    ImGui.CloseCurrentPopup();
                }
            }

            if (go.GetComponent<AudioListener>() is null && (!searchIsActive || "Audio Listener".Contains(_componentSearchText, StringComparison.OrdinalIgnoreCase)))
            {
                if (ImGui.MenuItem("Audio Listener"))
                {
                    _ = go.AddComponent(new AudioListener());
                    _editor.SceneManager.ActiveScene.IsDirty = true;
                    ImGui.CloseCurrentPopup();
                }
            }

            ImGui.Separator();

            var filteredTypes = searchIsActive
                ? _editor.AvailableScriptTypes.Where(t => t.Name.Contains(_componentSearchText, StringComparison.OrdinalIgnoreCase))
                : _editor.AvailableScriptTypes;

            foreach (var scriptType in filteredTypes)
            {
                if (!go.Components.Any(c => c.GetType() == scriptType) && ImGui.MenuItem(SplitPascalCase(scriptType.Name)))
                {
                    var newComponent = (Script)Activator.CreateInstance(scriptType);
                    _ = go.AddComponent(newComponent);
                    _editor.SceneManager.ActiveScene.IsDirty = true;

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
                    if (newScriptType is not null)
                    {
                        var newComponent = (Script)Activator.CreateInstance(newScriptType);
                        _ = go.AddComponent(newComponent);
                        _editor.SceneManager.ActiveScene.IsDirty = true;
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

    private static string SplitPascalCase(string input)
    {
        return Regex.Replace(input, "(?<!^)([A-Z])", " $1");
    }
}