namespace CherrisEditor;

public static class EditorConstants
{
    public static class DragDropPayloads
    {
        public const string Texture = "ASSET_PATH_TEXTURE";
        public const string Prefab = "ASSET_PATH_PREFAB";
        public const string Mesh = "ASSET_PATH_MESH";
        public const string GameObjectId = "GAMEOBJECT_ID";
    }

    public static class FileExtensions
    {
        public static readonly string[] Meshes =
        [
            ".obj",
            ".gltf",
            ".glb",
            ".fbx",
            ".dae"
        ];
    }
}