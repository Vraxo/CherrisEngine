using Cherris;
using Cherris.Components;
using Cherris.Core;
using CherrisEditor.Core;
using ImGuiNET;

namespace CherrisEditor.UI.Inspector;

public class ComponentAdderPresenter
{
    private readonly ComponentTypeMenuBuilder _menuBuilder;
    private readonly SceneManager _sceneManager;
    private readonly ScriptManager _scriptManager;

    private string _componentSearchText = "";
    private string _newScriptName = "";

    public ComponentAdderPresenter(
        ComponentTypeMenuBuilder menuBuilder,
        SceneManager sceneManager,
        ScriptManager scriptManager)
    {
        _menuBuilder = menuBuilder;
        _sceneManager = sceneManager;
        _scriptManager = scriptManager;
    }

    public void Draw(GameObject target)
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

        if (ImGui.Button("Add Component", new System.Numerics.Vector2(buttonWidth, 0)))
        {
            _componentSearchText = "";
            ImGui.OpenPopup("AddComponentPopup");
        }

        DrawPopup(target);
    }

    private void DrawPopup(GameObject target)
    {
        if (!ImGui.BeginPopup("AddComponentPopup"))
        {
            return;
        }

        DrawSearchField();
        ImGui.Separator();

        DrawBuiltInComponents(target);
        DrawScriptComponents(target);
        DrawNewScriptSection(target);

        ImGui.EndPopup();
    }

    private void DrawSearchField()
    {
        ImGui.PushItemWidth(-1);
        ImGui.InputTextWithHint("##ComponentSearch", "Search...", ref _componentSearchText, 100);
        ImGui.PopItemWidth();
    }

    private void DrawBuiltInComponents(GameObject target)
    {
        bool searchIsActive = !string.IsNullOrWhiteSpace(_componentSearchText);

        foreach (var entry in _menuBuilder.GetBuiltInEntries())
        {
            if (target.Components.Any(c => c.GetType() == entry.ComponentType))
            {
                continue;
            }

            if (searchIsActive && !entry.DisplayName.Contains(_componentSearchText, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (ImGui.MenuItem(entry.DisplayName))
            {
                var instance = (Component)Activator.CreateInstance(entry.ComponentType)!;
                target.AddComponent(instance);

                if (instance is Script script && _sceneManager.ActiveScene is not null)
                {
                    _sceneManager.ActiveScene.IsDirty = true;
                    if (_menuBuilder.IsInEditMode)
                    {
                        script.Enabled = false;
                    }
                }

                ImGui.CloseCurrentPopup();
            }
        }

        if (_menuBuilder.GetBuiltInEntries().Any())
        {
            ImGui.Separator();
        }
    }

    private void DrawScriptComponents(GameObject target)
    {
        bool searchIsActive = !string.IsNullOrWhiteSpace(_componentSearchText);

        var filteredTypes = searchIsActive
            ? _scriptManager.AvailableScriptTypes.Where(t =>
                t.Name.Contains(_componentSearchText, StringComparison.OrdinalIgnoreCase))
            : _scriptManager.AvailableScriptTypes;

        foreach (var scriptType in filteredTypes)
        {
            if (target.Components.Any(c => c.GetType() == scriptType))
            {
                continue;
            }

            if (ImGui.MenuItem(SplitPascalCase(scriptType.Name)))
            {
                var newComponent = (Script)Activator.CreateInstance(scriptType)!;
                target.AddComponent(newComponent);
                _sceneManager.ActiveScene!.IsDirty = true;

                if (_menuBuilder.IsInEditMode)
                {
                    newComponent.Enabled = false;
                }

                ImGui.CloseCurrentPopup();
            }
        }

        if (_scriptManager.AvailableScriptTypes.Any())
        {
            ImGui.Separator();
        }
    }

    private void DrawNewScriptSection(GameObject target)
    {
        ImGui.Text("New Script");
        ImGui.PushItemWidth(-1.0f);

        if (ImGui.InputText("##NewScriptName", ref _newScriptName, 100, ImGuiInputTextFlags.EnterReturnsTrue))
        {
            CreateAndAttachScript(target);
        }

        ImGui.PopItemWidth();
    }

    private void CreateAndAttachScript(GameObject target)
    {
        if (string.IsNullOrWhiteSpace(_newScriptName))
        {
            return;
        }

        string scriptName = _newScriptName;

        _menuBuilder.ScriptCreator?.Invoke(scriptName);

        Type? newScriptType = _scriptManager.AvailableScriptTypes.FirstOrDefault(t => t.Name == scriptName);

        if (newScriptType is not null)
        {
            var newComponent = (Script)Activator.CreateInstance(newScriptType)!;
            target.AddComponent(newComponent);
            _sceneManager.ActiveScene!.IsDirty = true;

            if (_menuBuilder.IsInEditMode)
            {
                newComponent.Enabled = false;
            }
        }

        _newScriptName = "";
        ImGui.CloseCurrentPopup();
    }

    private static string SplitPascalCase(string input)
    {
        return System.Text.RegularExpressions.Regex.Replace(input, "(?<!^)([A-Z])", " $1");
    }
}