using Overcore.Components;
using Overcore.Utils;
using OvercoreEditor.Undo;
using ImGuiNET;
using System.Numerics;
using System.Reflection;
using System.Text.RegularExpressions;

namespace OvercoreEditor.Inspectors;

public sealed class DefaultInspector(Editor editor, EditorTextureManager textures, HistoryManager history)
{
    private readonly Editor _editor = editor;
    private readonly EditorTextureManager _textures = textures;
    private readonly HistoryManager _history = history;
    private readonly PropertyDrawer _propertyDrawer = new PropertyDrawer(editor, textures, history);

    public bool Draw(Component component)
    {
        bool dirty = false;
        Type type = component.GetType();

        if (!ImGui.BeginTable($"{type.Name}Table", 3))
        {
            return false;
        }

        ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthStretch, 0.475f);
        ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch, 0.475f);
        ImGui.TableSetupColumn("##Reset", ImGuiTableColumnFlags.WidthStretch, 0.05f);

        IEnumerable<PropertyInfo> properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite && !p.IsDefined(typeof(HideInInspectorAttribute), false));

        foreach (PropertyInfo? prop in properties)
        {
            if (DrawPropertyRow(component, prop))
            {
                dirty = true;
            }
        }

        ImGui.EndTable();
        if (component is Image image)
        {
            dirty |= SpriteMetadataInspector.Draw(_editor, _textures, image);
        }
        else if (component is ParticleSystem particleSystem)
        {
            DrawParticleSystemControls(particleSystem);
        }
        else if (component is NavMeshSurface navMeshSurface)
        {
            DrawNavMeshSurfaceControls(navMeshSurface);
        }
        return dirty;
    }

    private static void DrawParticleSystemControls(ParticleSystem ps)
    {
        ImGui.Spacing();
        if (ImGui.Button("Play"))
        {
            ps.Play();
        }
        ImGui.SameLine();
        if (ImGui.Button("Pause"))
        {
            ps.Pause();
        }
        ImGui.SameLine();
        if (ImGui.Button("Restart"))
        {
            ps.Stop(ParticleStopBehavior.StopEmittingAndClear);
            ps.Play();
        }
        ImGui.SameLine();
        if (ImGui.Button("Clear"))
        {
            ps.Clear();
        }
        ImGui.SameLine();
        ImGui.TextDisabled($"Alive: {ps.ParticleCount}/{ps.MaxParticles}");
    }

    private static void DrawNavMeshSurfaceControls(NavMeshSurface surface)
    {
        ImGui.Spacing();
        if (ImGui.Button("Bake NavMesh"))
        {
            surface.BuildNavMesh();
        }
        ImGui.SameLine();
        if (ImGui.Button("Clear NavMesh"))
        {
            surface.RemoveData();
        }

        if (surface.NavMeshData is { } data)
        {
            ImGui.SameLine();
            ImGui.TextDisabled($"Polys: {data.Polygons.Count}, Verts: {data.Vertices.Length}");
        }
    }

    private bool DrawPropertyRow(Component component, PropertyInfo prop)
    {
        ImGui.TableNextRow();

        ImGui.TableSetColumnIndex(0);
        ImGui.Text(FormatPropertyName(prop.Name));

        ImGui.TableSetColumnIndex(1);
        ImGui.PushItemWidth(-1);

        _propertyDrawer.Draw(component, prop);
        ImGui.PopItemWidth();

        ImGui.TableSetColumnIndex(2);
        if (DrawResetButton(component, prop))
        {
            return true;
        }

        return ImGui.IsItemDeactivatedAfterEdit();
    }

    private bool DrawResetButton(Component component, PropertyInfo prop)
    {
        IntPtr icon = _textures.GetTexture("Reset");
        float size = ImGui.GetFrameHeight() - 4;

        if (!ImGui.ImageButton($"Reset{prop.Name}", icon, new Vector2(size, size)))
        {
            return false;
        }

        if (!TryGetDefaultValue(component.GetType(), prop.Name, out object? defaultValue))
        {
            return false;
        }

        prop.SetValue(component, defaultValue);
        return true;
    }

    private static bool TryGetDefaultValue(Type componentType, string propertyName, out object? defaultValue)
    {
        defaultValue = null;
        try
        {
            object? instance = Activator.CreateInstance(componentType);
            PropertyInfo? prop = componentType.GetProperty(propertyName);
            if (prop is null)
            {
                return false;
            }

            defaultValue = prop.GetValue(instance);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string FormatPropertyName(string input)
    {
        return Regex.Replace(input, "(?<!^)([A-Z])", " $1");
    }
}