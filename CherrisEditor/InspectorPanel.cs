using Cherris;
using CherrisEditor.Inspectors;
using CherrisEditor.Undo;
using ImGuiNET;
using System.Numerics;
using System.Reflection;
using System.Text.RegularExpressions;

namespace CherrisEditor;

internal class InspectorPanel
{
    private readonly Editor _editor;
    private readonly EditorTextureManager _textureManager;
    private readonly HistoryManager _history;
    private readonly Dictionary<Type, IComponentInspector> _customInspectors = new();
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
                var ctorSimple = inspectorType.GetConstructor(new[] { typeof(EditorTextureManager) });
                var ctorParameterless = inspectorType.GetConstructor(Type.EmptyTypes);

                if (ctorWithAll != null)
                    instance = (IComponentInspector)Activator.CreateInstance(inspectorType, editor, textureManager, history);
                else if (ctorWithEditor != null)
                    instance = (IComponentInspector)Activator.CreateInstance(inspectorType, editor, textureManager);
                else if (ctorWithHistory != null)
                    instance = (IComponentInspector)Activator.CreateInstance(inspectorType, textureManager, history);
                else if (ctorSimple != null)
                    instance = (IComponentInspector)Activator.CreateInstance(inspectorType, textureManager);
                else if (ctorParameterless != null)
                    instance = (IComponentInspector)Activator.CreateInstance(inspectorType);

                if (instance != null)
                {
                    _customInspectors[attribute.InspectedType] = instance;
                    Console.WriteLine($"[Inspector] Registered custom inspector for '{attribute.InspectedType.Name}'");
                }
                else
                {
                    Console.WriteLine($"[Inspector] Could not find a suitable constructor for '{inspectorType.Name}'.");
                }
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

        // --- Transform Component Header ---
        ImGui.PushID("TransformComponent");
        bool transformHeaderOpen;
        IntPtr transformIcon = _textureManager.GetTexture("Component_Transform");
        var style = ImGui.GetStyle();

        // Use a group to handle the custom layout
        ImGui.BeginGroup();
        // Calculate vertical padding to center the icon
        float iconSize = 20.0f;
        float frameHeight = ImGui.GetFrameHeight();
        float yPadding = (frameHeight - iconSize) * 0.5f;

        // Save original cursor pos
        var startPos = ImGui.GetCursorPos();

        // Draw icon at padded Y position
        ImGui.SetCursorPos(new Vector2(startPos.X, startPos.Y + yPadding));
        if (transformIcon != IntPtr.Zero)
        {
            ImGui.Image(transformIcon, new Vector2(iconSize, iconSize));
        }
        else
        {
            ImGui.Dummy(new Vector2(iconSize, iconSize)); // Placeholder
        }

        // Draw header at original Y position but offset X
        float headerX = startPos.X + iconSize + style.ItemSpacing.X;
        ImGui.SetCursorPos(new Vector2(headerX, startPos.Y));
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - (headerX - startPos.X));
        transformHeaderOpen = ImGui.CollapsingHeader("Transform", ImGuiTreeNodeFlags.DefaultOpen);
        ImGui.EndGroup();

        if (transformHeaderOpen)
        {
            _transformInspector.Draw(go.Transform);
        }
        ImGui.PopID();


        // --- Other Components ---
        foreach (var component in go.Components.ToList())
        {
            ImGui.Separator();
            string componentName = SplitPascalCase(component.GetType().Name);

            ImGui.PushID(component.GetHashCode());

            bool headerOpen;

            string textureKey = $"Component_{component.GetType().Name}";
            if (component is Light)
            {
                textureKey = "Component_Light";
            }

            IntPtr icon = _textureManager.GetTexture(textureKey);
            if (icon == IntPtr.Zero && typeof(Script).IsAssignableFrom(component.GetType()))
            {
                icon = _textureManager.GetTexture("Component_Script");
            }

            ImGui.BeginGroup();
            startPos = ImGui.GetCursorPos();
            ImGui.SetCursorPos(new Vector2(startPos.X, startPos.Y + yPadding));
            if (icon != IntPtr.Zero)
            {
                ImGui.Image(icon, new Vector2(iconSize, iconSize));
            }
            else
            {
                ImGui.Dummy(new Vector2(iconSize, iconSize)); // Placeholder
            }
            headerX = startPos.X + iconSize + style.ItemSpacing.X;
            ImGui.SetCursorPos(new Vector2(headerX, startPos.Y));
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - (headerX - startPos.X));
            headerOpen = ImGui.CollapsingHeader(componentName, ImGuiTreeNodeFlags.DefaultOpen);
            ImGui.EndGroup();


            if (ImGui.BeginPopupContextItem())
            {
                if (ImGui.MenuItem("Remove Component"))
                {
                    go.RemoveComponent(component);
                    _editor.SceneManager.ActiveScene.IsDirty = true;
                    ImGui.EndPopup();
                    ImGui.PopID();
                    return;
                }
                ImGui.EndPopup();
            }

            if (headerOpen)
            {
                bool componentChanged = false;
                if (_customInspectors.TryGetValue(component.GetType(), out var customInspector))
                {
                    if (customInspector.Draw(component)) componentChanged = true;
                }
                else
                {
                    if (_defaultInspector.Draw(component)) componentChanged = true;
                }
                if (componentChanged)
                {
                    _editor.SceneManager.ActiveScene.IsDirty = true;
                }
            }
            ImGui.PopID();
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
                    _editor.SceneManager.ActiveScene.IsDirty = true;
                    ImGui.CloseCurrentPopup();
                }
            }

            if (go.GetComponent<Light>() == null && (!searchIsActive || "Light".Contains(_componentSearchText, StringComparison.OrdinalIgnoreCase)))
            {
                if (ImGui.MenuItem("Light"))
                {
                    go.AddComponent(new Light());
                    _editor.SceneManager.ActiveScene.IsDirty = true;
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
                        go.AddComponent(newComponent);
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

    private static string SplitPascalCase(string input) => Regex.Replace(input, "(?<!^)([A-Z])", " $1");
}