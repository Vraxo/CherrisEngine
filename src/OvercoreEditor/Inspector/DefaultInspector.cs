using Overcore.Components;
using Overcore.Terrain;
using Overcore.Utils;
using OvercoreEditor.Undo;
using ImGuiNET;
using System.Numerics;
using System.Reflection;
using System.Text.RegularExpressions;
using TerrainComponent = Overcore.Components.Terrain;

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
        else if (component is TerrainComponent terrain)
        {
            dirty |= DrawTerrainControls(terrain);
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

    /// <summary>
    /// The terrain's brush and layers: arming the brush hands the viewport mouse to
    /// <see cref="UI.Viewport.TerrainBrushTool"/> for as long as it stays armed, and the layer list is
    /// where ground gets its faces. Returns true when the ground itself changed (a layer added, a tint
    /// edited), which is a scene edit; brush settings are editor state and never dirty the scene.
    /// </summary>
    private bool DrawTerrainControls(TerrainComponent terrain)
    {
        bool dirty = false;
        TerrainData data = terrain.TerrainData;
        UI.Viewport.TerrainBrushSettings brush = _editor.TerrainBrush;

        ImGui.Spacing();
        ImGui.TextDisabled($"Heightmap {data.HeightmapResolution}×{data.HeightmapResolution}"
            + $" · {data.Size.X:0}×{data.Size.Z:0} m · heights {data.MinHeight:0.0}…{data.MaxHeight:0.0} m");

        bool armed = brush.Enabled;
        if (ImGui.Checkbox("Sculpt in Viewport", ref armed))
        {
            brush.Enabled = armed;
        }

        if (!brush.Enabled)
        {
            ImGui.TextDisabled("Arm the brush, then drag on the ground in the viewport. One stroke is one undo step.");
            dirty |= DrawTerrainLayers(terrain);
            dirty |= DrawTerrainTrees(terrain);
            return dirty;
        }

        ImGui.Spacing();
        foreach (TerrainBrushMode mode in Enum.GetValues<TerrainBrushMode>())
        {
            if (mode != TerrainBrushMode.Raise)
            {
                ImGui.SameLine();
            }

            if (ImGui.RadioButton(mode.ToString(), brush.Mode == mode))
            {
                brush.Mode = mode;
            }
        }

        float radius = brush.RadiusMetres;
        if (ImGui.SliderFloat("Brush Size (m)", ref radius, 0.5f, MathF.Max(10f, data.Size.X)))
        {
            brush.RadiusMetres = radius;
        }

        float strength = brush.Strength;
        if (ImGui.SliderFloat("Brush Strength", ref strength, 0.1f, 20f))
        {
            brush.Strength = strength;
        }

        if (brush.Mode == TerrainBrushMode.Holes)
        {
            ImGui.TextDisabled("Drag to cut holes in the ground; hold Shift to fill them back in.");
        }

        if (brush.Mode == TerrainBrushMode.Flatten)
        {
            float flattenHeight = brush.FlattenHeight;
            if (ImGui.DragFloat("Flatten Height (m)", ref flattenHeight, 0.1f))
            {
                brush.FlattenHeight = flattenHeight;
            }
        }

        if (brush.Mode == TerrainBrushMode.Paint)
        {
            ImGui.Spacing();
            ImGui.Text("Paint Layer");
            for (int layer = 0; layer < data.Layers.Count; layer++)
            {
                if (ImGui.RadioButton($"{data.Layers[layer].Name}##PaintLayer{layer}", brush.LayerIndex == layer))
                {
                    brush.LayerIndex = layer;
                }
            }
        }

        if (brush.Mode == TerrainBrushMode.Trees)
        {
            ImGui.Spacing();
            if (data.TreePrototypes.Count == 0)
            {
                ImGui.TextDisabled("Add a tree prototype below, then drag on the ground to plant it.");
            }
            else
            {
                ImGui.Text("Plant");
                for (int prototype = 0; prototype < data.TreePrototypes.Count; prototype++)
                {
                    if (ImGui.RadioButton(
                        $"{data.TreePrototypes[prototype].Name}##PlantTree{prototype}",
                        brush.TreePrototypeIndex == prototype))
                    {
                        brush.TreePrototypeIndex = prototype;
                    }
                }

                ImGui.TextDisabled("Drag to plant; hold Shift to uproot. Trees stand on the ground and follow its sculpting.");
            }
        }

        dirty |= DrawTerrainLayers(terrain);
        dirty |= DrawTerrainTrees(terrain);
        return dirty;
    }

    /// <summary>The tree prototypes: the kinds of tree this ground can grow, each wearing a named mesh.</summary>
    private bool DrawTerrainTrees(TerrainComponent terrain)
    {
        bool dirty = false;
        TerrainData data = terrain.TerrainData;

        ImGui.Spacing();
        ImGui.Text($"Tree Prototypes · {data.Trees.Count} planted");
        for (int prototype = 0; prototype < data.TreePrototypes.Count; prototype++)
        {
            TerrainTreePrototype current = data.TreePrototypes[prototype];
            string meshName = current.MeshName;
            if (ImGui.InputText($"{current.Name} Mesh##TreeMesh{prototype}", ref meshName, 256))
            {
                current.MeshName = meshName;
                terrain.Rebuild();
                dirty = true;
            }

            Vector3 tint = current.Tint;
            if (ImGui.ColorEdit3($"Tint##TreeTint{prototype}", ref tint))
            {
                current.Tint = tint;
                terrain.Rebuild();
                dirty = true;
            }

            float trunkRadius = current.TrunkRadius;
            if (ImGui.DragFloat($"Trunk Radius (m)##TreeTrunkRadius{prototype}", ref trunkRadius, 0.02f))
            {
                current.TrunkRadius = MathF.Max(trunkRadius, 0f);
                terrain.Rebuild();
                dirty = true;
            }

            float trunkHeight = current.TrunkHeight;
            if (ImGui.DragFloat($"Trunk Height (m)##TreeTrunkHeight{prototype}", ref trunkHeight, 0.05f))
            {
                current.TrunkHeight = MathF.Max(trunkHeight, 0f);
                terrain.Rebuild();
                dirty = true;
            }
        }

        if (ImGui.Button("Add Tree Prototype"))
        {
            data.AddTreePrototype(new TerrainTreePrototype(
                $"Tree {data.TreePrototypes.Count}", "Cube", Vector3.One));
            terrain.Rebuild();
            dirty = true;
        }

        if (data.Trees.Count > 0 && ImGui.Button("Clear All Trees"))
        {
            data.ClearTrees();
            terrain.Rebuild();
            dirty = true;
        }

        return dirty;
    }

    /// <summary>The layer list, shared by the armed and unarmed inspector: tints to edit, a layer to add.</summary>
    private bool DrawTerrainLayers(TerrainComponent terrain)
    {
        bool dirty = false;
        TerrainData data = terrain.TerrainData;

        ImGui.Spacing();
        ImGui.Text("Layers");
        for (int layer = 0; layer < data.Layers.Count; layer++)
        {
            TerrainLayer current = data.Layers[layer];
            Vector3 tint = current.Tint;
            if (ImGui.ColorEdit3($"{current.Name}##LayerTint{layer}", ref tint))
            {
                current.Tint = tint;
                terrain.Rebuild();
                dirty = true;
            }
        }

        if (ImGui.Button("Add Layer"))
        {
            data.AddLayer(new TerrainLayer(
                $"Layer {data.Layers.Count}", string.Empty, Vector3.One));
            terrain.Rebuild();
            dirty = true;
        }

        return dirty;
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