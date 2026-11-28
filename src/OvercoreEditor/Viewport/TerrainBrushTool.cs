using Overcore;
using Overcore.Core;
using Overcore.Terrain;
using OvercoreEditor.Undo;
using OvercoreEditor.Undo.Commands;
using ImGuiNET;
using ImGuizmoNET;
using System.Numerics;
using TerrainComponent = Overcore.Components.Terrain;

namespace OvercoreEditor.UI.Viewport;

/// <summary>
/// The terrain brush in the viewport: while it is armed for the selected terrain, the mouse sculpts the
/// ground where it points instead of selecting objects or dragging the gizmo, and each stroke becomes
/// one undo step.
/// </summary>
/// <remarks>
/// <para>
/// Picking is the terrain's own raycast (<see cref="TerrainComponent.TryHit"/>), the same arithmetic the
/// physics queries use, because edit mode has no physics world to ask. A stroke begins on mouse-down
/// with a snapshot of the ground, applies continuously while the button is held — raise and lower are
/// rates, so a held brush keeps working — and on release the before/after snapshots become a
/// <see cref="TerrainStrokeCommand"/>. A stroke that changed nothing leaves no history behind.
/// </para>
/// <para>
/// The tool is modal in the small: while it is armed, object picking and the transform gizmo stand down
/// for the selected terrain, and the brush setting that disarms it lives in the terrain's inspector.
/// </para>
/// </remarks>
public sealed class TerrainBrushTool
{
    private readonly Editor _editor;
    private readonly HistoryManager _history;

    private readonly Random _random = new();

    private bool _stroking;
    private float[]? _heightsBefore;
    private float[]? _alphamapBefore;
    private byte[]? _holesBefore;
    private byte[]? _treesBefore;
    private float[]? _detailBefore;

    public TerrainBrushTool(Editor editor, HistoryManager history)
    {
        _editor = editor;
        _history = history;
    }

    /// <summary>True while the brush is armed for whatever is selected, so picking and the gizmo stand down.</summary>
    public bool IsArmed
    {
        get
        {
            TerrainBrushSettings settings = _editor.TerrainBrush;
            return settings.Enabled
                && _editor.State == EditorState.Editing
                && _editor.GetSelectedGameObject()?.GetComponent<TerrainComponent>() is not null;
        }
    }

    /// <summary>
    /// Draws the brush and strokes with it for one frame. Returns true when the brush owns the mouse this
    /// frame — armed for the selected terrain — so the caller skips picking and the gizmo.
    /// </summary>
    /// <param name="viewportPos">The viewport's top-left in screen space.</param>
    /// <param name="viewportSize">The viewport's size in pixels.</param>
    /// <param name="isHovered">True while the mouse is over the viewport.</param>
    public bool Handle(Vector2 viewportPos, Vector2 viewportSize, bool isHovered)
    {
        if (_stroking && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            FinishStroke();
        }

        if (!IsArmed)
        {
            _stroking = false;
            _heightsBefore = null;
            _alphamapBefore = null;
            _holesBefore = null;
            _treesBefore = null;
            _detailBefore = null;
            return false;
        }

        TerrainComponent terrain = _editor.GetSelectedGameObject()!.GetComponent<TerrainComponent>()!;
        if (!isHovered)
        {
            return true;
        }

        Ray? ray = _editor.Selection.CreateViewportRay(ImGui.GetMousePos(), viewportPos, viewportSize);
        if (ray is null || !terrain.TryHit(ray.Value, out Vector3 point, out Vector2 uv))
        {
            return true;
        }

        DrawBrushRing(terrain, point, viewportPos, viewportSize);

        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !ImGuizmo.IsOver())
        {
            _stroking = true;
            _heightsBefore = terrain.TerrainData.GetHeightmap();
            _alphamapBefore = terrain.TerrainData.GetAlphamap();
            _holesBefore = terrain.TerrainData.GetHolesPacked();
            _treesBefore = terrain.TerrainData.GetTreesPacked();
            _detailBefore = terrain.TerrainData.GetDetailMap();
            ApplyStroke(terrain, uv, 1f / 60f);
        }
        else if (_stroking && ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            ApplyStroke(terrain, uv, ImGui.GetIO().DeltaTime);
        }

        return true;
    }

    private void ApplyStroke(TerrainComponent terrain, Vector2 uv, float deltaTime)
    {
        TerrainBrushSettings settings = _editor.TerrainBrush;
        TerrainData data = terrain.TerrainData;

        // The brush is sized in metres; the stroke it drives is normalised. The footprint's X is the
        // measure, which for the usual square terrain is the whole story.
        float radius = settings.RadiusMetres / MathF.Max(data.Size.X, 0.01f);
        float strength = settings.StrokeStrength(deltaTime);

        if (settings.Mode == TerrainBrushMode.Detail)
        {
            // Shift thins back to bare ground; otherwise the stroke grows the chosen prototype thicker.
            if (data.DetailPrototypes.Count > 0)
            {
                int prototype = Math.Clamp(settings.DetailPrototypeIndex, 0, data.DetailPrototypes.Count - 1);
                float signed = ImGui.GetIO().KeyShift ? -strength : strength;
                terrain.ApplyDetail(uv, radius, signed, prototype);
            }
        }
        else if (settings.Mode == TerrainBrushMode.Trees)
        {
            // Shift uproots instead of planting. Planting is a scatter, not a rate: each frame drops a
            // handful of seeds, and holding the brush walks a wood into being.
            if (ImGui.GetIO().KeyShift)
            {
                terrain.EraseTrees(uv, radius);
            }
            else if (data.TreePrototypes.Count > 0)
            {
                int prototype = Math.Clamp(settings.TreePrototypeIndex, 0, data.TreePrototypes.Count - 1);
                terrain.PlantTrees(uv, radius, prototype, attempts: 5, minSpacing: radius * 0.4f, _random);
            }
        }
        else if (settings.Mode == TerrainBrushMode.Holes)
        {
            // Shift is the brush running backwards: fill the holes back in.
            terrain.ApplyHoles(uv, radius, hole: !ImGui.GetIO().KeyShift);
        }
        else if (settings.Mode == TerrainBrushMode.Paint)
        {
            int layer = Math.Clamp(settings.LayerIndex, 0, Math.Max(data.Layers.Count - 1, 0));
            terrain.Paint(uv, radius, strength, layer);
        }
        else
        {
            terrain.Sculpt(uv, radius, strength, settings.Mode, settings.FlattenHeight);
        }
    }

    private void FinishStroke()
    {
        _stroking = false;

        if (_heightsBefore is null || _alphamapBefore is null || _holesBefore is null || _treesBefore is null
            || _detailBefore is null
            || _editor.GetSelectedGameObject()?.GetComponent<TerrainComponent>() is not { } terrain)
        {
            _heightsBefore = null;
            _alphamapBefore = null;
            _holesBefore = null;
            _treesBefore = null;
            _detailBefore = null;
            return;
        }

        float[] heightsAfter = terrain.TerrainData.GetHeightmap();
        float[] alphamapAfter = terrain.TerrainData.GetAlphamap();
        byte[] holesAfter = terrain.TerrainData.GetHolesPacked();
        byte[] treesAfter = terrain.TerrainData.GetTreesPacked();
        float[] detailAfter = terrain.TerrainData.GetDetailMap();
        bool changed = !_heightsBefore.SequenceEqual(heightsAfter)
            || !_alphamapBefore.SequenceEqual(alphamapAfter)
            || !_holesBefore.SequenceEqual(holesAfter)
            || !_treesBefore.SequenceEqual(treesAfter)
            || !_detailBefore.SequenceEqual(detailAfter);

        if (changed)
        {
            // Execute re-applies the after state the stroke already left, which is the point: history
            // records it, and the dirty flag follows through OnHistoryChanged.
            _history.Execute(new TerrainStrokeCommand(
                terrain, _heightsBefore, _alphamapBefore, _holesBefore, _treesBefore, _detailBefore,
                heightsAfter, alphamapAfter, holesAfter, treesAfter, detailAfter));
        }

        _heightsBefore = null;
        _alphamapBefore = null;
        _holesBefore = null;
        _treesBefore = null;
        _detailBefore = null;
    }

    private void DrawBrushRing(TerrainComponent terrain, Vector3 point, Vector2 viewportPos, Vector2 viewportSize)
    {
        if (_editor.SceneManager.MainCamera is not { } camera || camera.GameObject is null)
        {
            return;
        }

        if (!WorldToScreen(point, camera, viewportPos, viewportSize, out Vector2 center))
        {
            return;
        }

        // The ring's radius in pixels: project a point one brush-radius to the camera's right of the hit.
        Vector3 edge = point + (camera.GameObject.Transform.Right * _editor.TerrainBrush.RadiusMetres);
        float radiusPixels = WorldToScreen(edge, camera, viewportPos, viewportSize, out Vector2 edgeScreen)
            ? Vector2.Distance(center, edgeScreen)
            : 24f;

        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        uint color = ImGui.GetColorU32(new Vector4(1f, 0.85f, 0.3f, 0.9f));
        drawList.AddCircle(center, MathF.Max(radiusPixels, 4f), color, 48, 2f);
        drawList.AddCircleFilled(center, 3f, color);
    }

    /// <summary>A world point in viewport pixel space, or false when it is behind the camera.</summary>
    internal static bool WorldToScreen(
        Vector3 world, Overcore.Components.Camera camera, Vector2 viewportPos, Vector2 viewportSize, out Vector2 screen)
    {
        screen = Vector2.Zero;
        if (viewportSize.X <= 0 || viewportSize.Y <= 0)
        {
            return false;
        }

        Matrix4x4 view = camera.GetViewMatrix();
        Matrix4x4 projection = camera.GetProjectionMatrix(viewportSize.X / viewportSize.Y);
        Vector4 clip = Vector4.Transform(Vector4.Transform(new Vector4(world, 1f), view), projection);
        if (clip.W <= 0f)
        {
            return false;
        }

        Vector2 ndc = new(clip.X / clip.W, clip.Y / clip.W);
        screen = new Vector2(
            viewportPos.X + ((ndc.X * 0.5f) + 0.5f) * viewportSize.X,
            viewportPos.Y + (0.5f - (ndc.Y * 0.5f)) * viewportSize.Y);
        return true;
    }
}
