namespace Overcore.Core;

/// <summary>
/// Where a GameObject lives inside a prefab asset: the asset's guid, and the id the object carries within
/// that asset. Every object of a prefab instance keeps this link — the same shape as Unity's
/// corresponding-source pair — so the editor can tell what is linked and to what.
/// </summary>
/// <param name="AssetGuid">The prefab asset's guid, from its <c>.meta</c> sidecar.</param>
/// <param name="FileId">The object's id within the asset: stable across instantiations of it.</param>
public sealed record PrefabSource(string AssetGuid, int FileId);
