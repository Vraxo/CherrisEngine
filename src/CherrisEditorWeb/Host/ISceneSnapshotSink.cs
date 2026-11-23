namespace CherrisEditorWeb.Host;

/// <summary>
/// Optional sink the engine uses to publish periodic, immutable scene snapshots so a UI thread can show
/// the hierarchy without touching the live scene.
/// </summary>
public interface ISceneSnapshotSink
{
    /// <summary>Called on the render thread every N frames.</summary>
    void PublishScene(SceneSnapshot snapshot);
}
