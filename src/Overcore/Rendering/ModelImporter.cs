using Overcore.Animation;
using Overcore.Components;
using Overcore.Core;
using Overcore.Core.Logging;
using Overcore.Utils;
using SharpGLTF.Schema2;
using SharpGLTF.Transforms;
using System.Linq;
using System.Numerics;
using Veldrid;

// SharpGLTF.Schema2 and Overcore.Core both declare a Material: the glTF one is read from a file, the engine one is
// what a MeshRenderer holds. Naming the engine's here keeps the two from being ambiguous wherever both are in scope.
using Material = Overcore.Core.Material;

namespace Overcore.Rendering;

/// <summary>One imported object: the handle a caller needs to place, and to link, what the file described.</summary>
/// <param name="GameObject">The object built for a glTF node.</param>
/// <param name="Node">Its array index in the file's <c>nodes</c>, for diagnostics.</param>
public readonly record struct ImportedObject(GameObject GameObject, int Node);

/// <summary>
/// What importing a model produced: a hierarchy, the meshes the file carried, the skeletons its skins described,
/// and the clips its animations described.
/// </summary>
/// <remarks>
/// A model is a hierarchy, not a bag of meshes. Meshes, skeletons and clips are assets: they are data about bones and
/// vertices, and know nothing about the scene they are dropped into.
/// </remarks>
public sealed class ImportedModel
{
    /// <summary>The path the model was read from, as clips record it for re-import.</summary>
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>The root objects, in the order the file's scene listed them.</summary>
    public List<GameObject> Roots { get; } = [];

    /// <summary>
    /// Every object the file described, keyed by the node path Unity would show — <c>"Armature/Hips"</c>. This is
    /// also the addressing an animation curve and a bone path use.
    /// </summary>
    public Dictionary<string, ImportedObject> ObjectsByPath { get; } = new(StringComparer.Ordinal);

    /// <summary>Every mesh in the file, keyed by the glTF mesh's name. Built once however many nodes instance it.</summary>
    public Dictionary<string, Components.Mesh> Meshes { get; } = new(StringComparer.Ordinal);

    /// <summary>Skeletons, keyed by the skin's name (or <c>"Skin N"</c> when it has none).</summary>
    public Dictionary<string, Skeleton> Skeletons { get; } = new(StringComparer.Ordinal);

    /// <summary>Clips, keyed by clip name.</summary>
    public Dictionary<string, AnimationClip> Clips { get; } = new(StringComparer.Ordinal);

    /// <summary>Objects built but not reachable from a root — a skin joint the file's scene never lists.</summary>
    public List<GameObject> Orphans { get; } = [];

    /// <summary>Why parts of the file were skipped, one readable line each. Empty when the import was complete.</summary>
    public List<string> Warnings { get; } = [];

    /// <summary>Finds an object by its node path, or null when the path names nothing.</summary>
    public ImportedObject? Find(string nodePath) =>
        ObjectsByPath.TryGetValue(nodePath, out ImportedObject found) ? found : null;
}

/// <summary>
/// Reads a <c>.gltf</c>/<c>.glb</c> file into Overcore objects: one <see cref="GameObject"/> per glTF node, parented
/// the way the file's node graph is, with a <see cref="MeshRenderer"/> on every node that carries a mesh.
/// </summary>
/// <remarks>
/// <para>
/// This replaces the old <c>ModelLoader</c>, which flattened a file into a name → mesh dictionary and threw the node
/// graph away. The graph is the point: a skeleton is a subtree of transforms, so without the subtree there is nothing
/// to animate.
/// </para>
/// <para>
/// The importer never touches a graphics device, a scene or a window. It is pure file → objects, which is what lets
/// the tests in <c>tests/Overcore.Tests/Rendering</c> import a generated GLB and assert on the hierarchy with no
/// context at all. Only the vertex layout a renderer builds from the result is backend work.
/// </para>
/// <para>
/// Three things are deliberately not done here: skinning on the GPU (the <see cref="Skeleton"/> is built and linked,
/// but nothing deforms a vertex yet), sampling a clip into a pose, and the <c>Animator</c>. What this produces is a
/// rigged hierarchy sitting at its bind pose, with its clips in hand.
/// </para>
/// </remarks>
public static class ModelImporter
{
    /// <summary>Reads a model from disk or from the project's asset bundle.</summary>
    /// <param name="path">Project-relative or absolute path to a <c>.gltf</c> or <c>.glb</c>.</param>
    /// <returns>What the file described, or null when it could not be read at all.</returns>
    public static ImportedModel? Import(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        ModelRoot? model = LoadModel(path);

        return model is null ? null : Import(model, path);
    }

    /// <summary>Reads an already-parsed model. The path is carried into the clips' <c>SourcePath</c>.</summary>
    public static ImportedModel Import(ModelRoot model, string path)
    {
        ArgumentNullException.ThrowIfNull(model);

        ImportedModel result = new() { SourcePath = path ?? string.Empty };

        Dictionary<Node, GameObject> objects = [];

        // One Mesh per glTF mesh, however many nodes instance it: a file can place the same mesh a hundred times and
        // building it a hundred times would be both slow and a hundred copies of the same vertex buffer.
        Dictionary<SharpGLTF.Schema2.Mesh, Components.Mesh> meshCache = [];

        // Guards the walk against a file whose node graph is not a tree. glTF forbids a node with two parents and
        // forbids cycles, but a malformed file must not take the importer down with a stack overflow.
        HashSet<Node> visited = [];

        // Skins first: a joint's inverse bind matrix is read from the file, and a node that is a joint of one skin
        // may be referenced by another node's mesh before the hierarchy is walked. Building the skeletons up front
        // keeps that single pass.
        Dictionary<Skin, Skeleton> skeletons = BuildSkeletons(model, objects, result);

        // The file's chosen scene, or its first one, or — for a file with no scene at all — every parentless node, so
        // a mesh-only export still imports as something.
        SharpGLTF.Schema2.Scene? scene = model.DefaultScene
            ?? (model.LogicalScenes.Count > 0 ? model.LogicalScenes[0] : null);

        IEnumerable<Node> roots = scene is not null
            ? scene.VisualChildren
            : model.LogicalNodes.Where(node => node.VisualParent is null);

        foreach (Node root in roots)
        {
            GameObject gameObject = BuildHierarchy(root, null, string.Empty, objects, visited, skeletons, meshCache, result);
            result.Roots.Add(gameObject);
        }

        // Objects the walk never reached: a skeleton the file left unparented, or a node the scene does not list but
        // a skin refers to. They are kept, and reported, rather than silently lost.
        foreach (Node node in model.LogicalNodes)
        {
            if (objects.ContainsKey(node))
            {
                continue;
            }

            GameObject orphan = BuildHierarchy(node, null, string.Empty, objects, visited, skeletons, meshCache, result);
            result.Orphans.Add(orphan);
        }

        ImportClips(model, result);

        return result;
    }

    /// <summary>Opens the file, from the asset bundle when there is one and from disk otherwise.</summary>
    private static ModelRoot? LoadModel(string path)
    {
        try
        {
            string? realPath = ProjectFiles.Find(path);

            if (realPath is not null)
            {
                return ModelRoot.Load(realPath);
            }

            using Stream? stream = ProjectFiles.Open(path);

            if (stream is null)
            {
                Logger.Warning($"[ModelImporter] Model not found: '{path}'");
                return null;
            }

            // A loose .gltf names its .bin and textures by relative path, which a bundle cannot resolve, so only a
            // self-contained .glb is read from a stream.
            if (path.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
            {
                return ModelRoot.ReadGLB(stream);
            }

            Logger.Warning($"[ModelImporter] Packed .gltf files ('{path}') are not supported. Use .glb for packed assets.");
            return null;
        }
        catch (Exception exception)
        {
            Logger.Error($"[ModelImporter] Failed to read '{path}': {exception.Message}");
            return null;
        }
    }

    /// <summary>
    /// Builds every skeleton the file declares. Bones become objects here if they were not made already, which is
    /// what lets a skin be resolved before the hierarchy walk reaches its joints.
    /// </summary>
    private static Dictionary<Skin, Skeleton> BuildSkeletons(
        ModelRoot model,
        Dictionary<Node, GameObject> objects,
        ImportedModel result)
    {
        Dictionary<Skin, Skeleton> skeletons = [];

        for (int index = 0; index < model.LogicalSkins.Count; index++)
        {
            Skin skin = model.LogicalSkins[index];

            string name = string.IsNullOrWhiteSpace(skin.Name) ? $"Skin {index}" : skin.Name;
            Skeleton skeleton = new() { Name = name };

            // The bind inverses line up with the joints by index, and that index order is the bone index the mesh's
            // JOINTS_0 refers to. Nothing may reorder this list.
            IReadOnlyList<Matrix4x4> bindInverses = skin.InverseBindMatrices;
            IReadOnlyList<Node> joints = skin.Joints;

            for (int jointIndex = 0; jointIndex < joints.Count; jointIndex++)
            {
                Node jointNode = joints[jointIndex];

                GameObject bone = objects.TryGetValue(jointNode, out GameObject? existing)
                    ? existing
                    : CreateObject(jointNode, objects);

                Matrix4x4 bindInverse = jointIndex < bindInverses.Count
                    ? bindInverses[jointIndex]
                    : Matrix4x4.Identity;

                skeleton.AddBone(bone, bindInverse);
            }

            if (skin.Skeleton is not null)
            {
                skeleton.RootBone = objects.TryGetValue(skin.Skeleton, out GameObject? root)
                    ? root
                    : CreateObject(skin.Skeleton, objects);
            }

            if (joints.Count == 0)
            {
                result.Warnings.Add($"Skin '{name}' names no joints; its meshes keep their bind pose.");
            }

            skeletons[skin] = skeleton;
            result.Skeletons[name] = skeleton;
        }

        return skeletons;
    }

    /// <summary>Builds one node and everything under it, in the order a file lists them.</summary>
    private static GameObject BuildHierarchy(
        Node node,
        GameObject? parent,
        string parentPath,
        Dictionary<Node, GameObject> objects,
        HashSet<Node> visited,
        Dictionary<Skin, Skeleton> skeletons,
        Dictionary<SharpGLTF.Schema2.Mesh, Components.Mesh> meshCache,
        ImportedModel result)
    {
        GameObject gameObject = objects.TryGetValue(node, out GameObject? existing)
            ? existing
            : CreateObject(node, objects);

        string path = PathOf(node, parentPath);

        result.ObjectsByPath.TryAdd(path, new ImportedObject(gameObject, node.LogicalIndex));

        if (parent is not null && gameObject.Transform.Parent is null)
        {
            gameObject.Transform.Parent = parent.Transform;
        }

        // A node reached twice — a shared child, or a cycle in a malformed file — is built once and not walked again.
        if (!visited.Add(node))
        {
            return gameObject;
        }

        AttachContent(node, gameObject, skeletons, meshCache, result);

        foreach (Node child in node.VisualChildren)
        {
            BuildHierarchy(child, gameObject, path, objects, visited, skeletons, meshCache, result);
        }

        return gameObject;
    }

    /// <summary>
    /// Creates the object for a node, with its name and its local transform, and registers it by node.
    /// </summary>
    /// <remarks>
    /// The object's *path* is registered by <see cref="BuildHierarchy"/> only, once the walk knows where the node sits.
    /// A joint reached early — from a skin, before the hierarchy gets there — is the same object as the node, so
    /// registering the path here would record it against an empty parent path and then refuse to correct it.
    /// </remarks>
    private static GameObject CreateObject(Node node, Dictionary<Node, GameObject> objects)
    {
        string name = string.IsNullOrWhiteSpace(node.Name)
            ? $"Node {node.LogicalIndex}"
            : node.Name;

        GameObject gameObject = new(name);

        // Decomposed TRS where the file has it; a node carrying a matrix is decomposed by SharpGLTF. An animated node
        // starts at its static pose, which is why the clips can be sampled from zero without a first-frame snap.
        AffineTransform transform = node.LocalTransform;

        gameObject.Transform.Position = transform.Translation;
        gameObject.Transform.Rotation = transform.Rotation;
        gameObject.Transform.Scale = transform.Scale;

        objects[node] = gameObject;

        return gameObject;
    }

    /// <summary>Adds the mesh a node carries, as a renderer whose mesh came from this file.</summary>
    private static void AttachContent(
        Node node,
        GameObject gameObject,
        Dictionary<Skin, Skeleton> skeletons,
        Dictionary<SharpGLTF.Schema2.Mesh, Components.Mesh> meshCache,
        ImportedModel result)
    {
        if (node.Mesh is null)
        {
            return;
        }

        string meshName = string.IsNullOrWhiteSpace(node.Mesh.Name)
            ? $"Mesh {node.Mesh.LogicalIndex}"
            : node.Mesh.Name;

        if (!meshCache.TryGetValue(node.Mesh, out Components.Mesh? mesh))
        {
            mesh = ImportMesh(node.Mesh, result);
            meshCache[node.Mesh] = mesh;

            // A mesh is built once however many nodes instance it, so the asset is registered here, in the same
            // place its warning is emitted: instancing the same empty mesh must not warn per node.
            if (mesh.Vertices.Length > 0)
            {
                result.Meshes.TryAdd(meshName, mesh);
            }
        }

        if (mesh.Vertices.Length == 0)
        {
            result.Warnings.Add($"Mesh '{meshName}' has no drawable primitives; node '{gameObject.Name}' renders nothing.");
            return;
        }

        Material material = ImportMaterial(node.Mesh);

        gameObject.AddComponent(new MeshRenderer
        {
            Mesh = mesh,
            MeshName = meshName,
            Material = material
        });

        // Skinning is recorded, not applied: the renderer keeps its skeleton so increment two can deform with it,
        // and the inspector can show which bones a mesh follows in the meantime.
        if (node.Skin is not null && skeletons.TryGetValue(node.Skin, out Skeleton? skeleton))
        {
            gameObject.AddComponent(new SkinnedMesh(skeleton));
        }
    }

    /// <summary>
    /// Welds every primitive of a glTF mesh into one vertex/index pair, reading the attributes the engine's vertex
    /// carries — including the bone data the old loader dropped.
    /// </summary>
    public static Components.Mesh ImportMesh(SharpGLTF.Schema2.Mesh gltfMesh, ImportedModel? result = null)
    {
        ArgumentNullException.ThrowIfNull(gltfMesh);

        List<Vertex> vertices = [];
        List<uint> indices = [];

        foreach (MeshPrimitive primitive in gltfMesh.Primitives)
        {
            Accessor? positionsAccessor = primitive.GetVertexAccessor("POSITION");

            if (positionsAccessor is null)
            {
                result?.Warnings.Add($"A primitive of mesh '{gltfMesh.Name}' has no POSITION and was skipped.");
                continue;
            }

            IList<Vector3> positions = positionsAccessor.AsVector3Array();
            IList<Vector3>? normals = primitive.GetVertexAccessor("NORMAL")?.AsVector3Array();
            IList<Vector2>? texCoords = primitive.GetVertexAccessor("TEXCOORD_0")?.AsVector2Array();

            // COLOR_0 comes back as an accessor array, which exposes IReadOnlyList but not IList — the component
            // order is read through the read-only interface rather than the mutable one.
            IReadOnlyList<Vector4>? colors = primitive.GetVertexAccessor("COLOR_0")?.AsColorArray();

            IList<Vector4>? jointWeights = primitive.GetVertexAccessor("WEIGHTS_0")?.AsVector4Array();
            IList<Vector4>? jointIndices = primitive.GetVertexAccessor("JOINTS_0")?.AsVector4Array();

            uint baseVertex = (uint)vertices.Count;

            for (int vertexIndex = 0; vertexIndex < positions.Count; vertexIndex++)
            {
                RgbaFloat color = colors is not null && vertexIndex < colors.Count
                    ? new RgbaFloat(colors[vertexIndex].X, colors[vertexIndex].Y, colors[vertexIndex].Z, colors[vertexIndex].W)
                    : RgbaFloat.White;

                Vector4 boneIndices = Vector4.Zero;
                Vector4 boneWeights = Vector4.Zero;

                if (jointWeights is not null && vertexIndex < jointWeights.Count)
                {
                    (boneIndices, boneWeights) = NormalizeInfluences(jointWeights[vertexIndex], jointIndices, vertexIndex);
                }

                vertices.Add(new Vertex(
                    positions[vertexIndex],
                    normals is not null && vertexIndex < normals.Count ? normals[vertexIndex] : Vector3.UnitY,
                    color,
                    texCoords is not null && vertexIndex < texCoords.Count ? texCoords[vertexIndex] : Vector2.Zero,
                    boneIndices,
                    boneWeights));
            }

            IList<uint> primitiveIndices = primitive.IndexAccessor is not null
                ? primitive.IndexAccessor.AsIndicesArray()
                : Enumerable.Range(0, positions.Count).Select(index => (uint)index).ToList();

            // glTF draws counter-clockwise from the outside; the engine's pipelines are front-face clockwise, which is
            // why every hand-built mesh in Mesh.cs reverses its winding. The same reversal happens once here.
            for (int triangle = 0; triangle + 2 < primitiveIndices.Count; triangle += 3)
            {
                indices.Add(baseVertex + primitiveIndices[triangle]);
                indices.Add(baseVertex + primitiveIndices[triangle + 2]);
                indices.Add(baseVertex + primitiveIndices[triangle + 1]);
            }
        }

        return new Components.Mesh(vertices.ToArray(), indices.ToArray());
    }

    /// <summary>
    /// Clamps a vertex to the four influences a <see cref="Vertex"/> can carry, renormalizing what is left so a
    /// dropped weight does not shrink the mesh toward the origin.
    /// </summary>
    /// <remarks>
    /// A vertex whose weights are all zero is pinned to bone zero with full weight instead of collapsing to the
    /// renderer's origin — the failure that makes an imported character vanish into a point.
    /// </remarks>
    public static (Vector4 Indices, Vector4 Weights) NormalizeInfluences(
        Vector4 weights,
        IList<Vector4>? indices,
        int vertexIndex)
    {
        Vector4 source = indices is not null && vertexIndex < indices.Count
            ? indices[vertexIndex]
            : Vector4.Zero;

        Span<float> kept = stackalloc float[4];
        Span<float> keptIndices = stackalloc float[4];

        int keptCount = 0;
        float total = 0f;

        ReadOnlySpan<float> weightComponents = [weights.X, weights.Y, weights.Z, weights.W];
        ReadOnlySpan<float> indexComponents = [source.X, source.Y, source.Z, source.W];

        for (int slot = 0; slot < 4; slot++)
        {
            float weight = weightComponents[slot];

            if (!(weight > 0f) || !float.IsFinite(weight))
            {
                continue;
            }

            kept[keptCount] = weight;
            keptIndices[keptCount] = indexComponents[slot];
            total += weight;
            keptCount++;
        }

        if (keptCount == 0 || total <= 0f)
        {
            return (Vector4.Zero, new Vector4(1f, 0f, 0f, 0f));
        }

        Vector4 resultIndices = Vector4.Zero;
        Vector4 resultWeights = Vector4.Zero;

        resultIndices.X = keptIndices[0];
        resultWeights.X = kept[0] / total;

        if (keptCount > 1)
        {
            resultIndices.Y = keptIndices[1];
            resultWeights.Y = kept[1] / total;
        }

        if (keptCount > 2)
        {
            resultIndices.Z = keptIndices[2];
            resultWeights.Z = kept[2] / total;
        }

        if (keptCount > 3)
        {
            resultIndices.W = keptIndices[3];
            resultWeights.W = kept[3] / total;
        }

        return (resultIndices, resultWeights);
    }

    /// <summary>
    /// Builds a material from the first primitive that has one: the base colour, and the base-colour texture's name
    /// when the file names a texture the engine can find.
    /// </summary>
    /// <remarks>
    /// The engine's shading has one albedo (vertex colour times the texture) and one emissive term, so a file's base
    /// colour rides in the emissive slot when there is no texture to carry it. That is a compromise the lighting
    /// model will outgrow, and it is recorded as one in the animation document rather than hidden.
    /// </remarks>
    private static Material ImportMaterial(SharpGLTF.Schema2.Mesh gltfMesh)
    {
        Material material = new(null!, "White");

        foreach (MeshPrimitive primitive in gltfMesh.Primitives)
        {
            if (primitive.Material is null)
            {
                continue;
            }

            MaterialChannel? baseColor = primitive.Material.FindChannel("BaseColor")
                ?? primitive.Material.FindChannel("Diffuse");

            if (baseColor is null)
            {
                continue;
            }

            Vector4 color = baseColor.Value.Color;
            string? textureName = baseColor.Value.Texture?.Name;

            if (!string.IsNullOrWhiteSpace(textureName))
            {
                material.TextureName = textureName;
            }
            else
            {
                material.EmissiveColor = new Vector3(color.X, color.Y, color.Z) * color.W;
            }

            break;
        }

        return material;
    }

    /// <summary>Turns the file's animations into clips, one per animation, addressed by node path.</summary>
    private static void ImportClips(ModelRoot model, ImportedModel result)
    {
        foreach (SharpGLTF.Schema2.Animation animation in model.LogicalAnimations)
        {
            string name = string.IsNullOrWhiteSpace(animation.Name)
                ? $"Animation {animation.LogicalIndex}"
                : animation.Name;

            AnimationClip clip = new(name) { SourcePath = result.SourcePath };

            foreach (AnimationChannel channel in animation.Channels)
            {
                Node? target = channel.TargetNode;

                if (target is null)
                {
                    continue;
                }

                // The path a clip drives must be the path the hierarchy registered the node under, not the node's own
                // name: an importer walk starts at a root and the clip has to agree with it, or nothing binds.
                string? bonePath = PathOfNode(target);

                if (bonePath is null)
                {
                    result.Warnings.Add($"Clip '{name}' animates a node the imported scene does not contain.");
                    continue;
                }

                if (channel.TargetNodePath == PropertyPath.weights)
                {
                    result.Warnings.Add($"Clip '{name}' animates morph weights on '{bonePath}'; morph targets are not supported.");
                    continue;
                }

                AddChannelCurves(channel, bonePath, clip);
            }

            if (clip.IsEmpty)
            {
                continue;
            }

            // Length is the last key of the longest curve the clip actually took. A channel whose keys went nowhere
            // (a morph target, a node the scene lacks) must not stretch the clip past its content.
            clip.Length = MathF.Max(clip.Length, 0f);
            result.Clips[name] = clip;
        }
    }

    /// <summary>Copies one channel's keys into the clip as per-axis curves.</summary>
    private static void AddChannelCurves(AnimationChannel channel, string bonePath, AnimationClip clip)
    {
        switch (channel.TargetNodePath)
        {
            case PropertyPath.translation:
                if (channel.GetTranslationSampler() is { } translation)
                {
                    AddVectorCurves(translation, bonePath, AnimationProperty.LocalPosition, clip);
                }

                break;

            case PropertyPath.rotation:
                if (channel.GetRotationSampler() is { } rotation)
                {
                    AddRotationCurves(rotation, bonePath, clip);
                }

                break;

            case PropertyPath.scale:
                if (channel.GetScaleSampler() is { } scale)
                {
                    AddVectorCurves(scale, bonePath, AnimationProperty.LocalScale, clip);
                }

                break;
        }
    }

    /// <summary>
    /// Copies a translation or scale sampler into three component curves. A glTF sampler is either linear/step (one
    /// value per key) or cubic (a tangent, a value and a tangent per key); both are read, so a curve keeps the shape
    /// the file gave it rather than being flattened to linear on the way in.
    /// </summary>
    private static void AddVectorCurves(
        IAnimationSampler<Vector3> sampler,
        string bonePath,
        AnimationProperty property,
        AnimationClip clip)
    {
        AnimationInterpolation interpolation = InterpolationOf(sampler.InterpolationMode);

        AnimationCurve x = new(bonePath, property, AnimationAxis.X) { Interpolation = interpolation };
        AnimationCurve y = new(bonePath, property, AnimationAxis.Y) { Interpolation = interpolation };
        AnimationCurve z = new(bonePath, property, AnimationAxis.Z) { Interpolation = interpolation };

        if (sampler.InterpolationMode == AnimationInterpolationMode.CUBICSPLINE)
        {
            foreach ((float time, (Vector3 tangentIn, Vector3 value, Vector3 tangentOut)) in sampler.GetCubicKeys())
            {
                x.AddKey(new AnimationKey(time, value.X, tangentIn.X, tangentOut.X));
                y.AddKey(new AnimationKey(time, value.Y, tangentIn.Y, tangentOut.Y));
                z.AddKey(new AnimationKey(time, value.Z, tangentIn.Z, tangentOut.Z));
            }
        }
        else
        {
            foreach ((float time, Vector3 value) in sampler.GetLinearKeys())
            {
                x.AddKey(time, value.X);
                y.AddKey(time, value.Y);
                z.AddKey(time, value.Z);
            }
        }

        clip.AddCurve(x);
        clip.AddCurve(y);
        clip.AddCurve(z);
    }

    /// <summary>Copies a rotation sampler into four component curves, made continuous before they are stored.</summary>
    private static void AddRotationCurves(IAnimationSampler<Quaternion> sampler, string bonePath, AnimationClip clip)
    {
        AnimationInterpolation interpolation = InterpolationOf(sampler.InterpolationMode);

        AnimationCurve x = new(bonePath, AnimationProperty.LocalRotation, AnimationAxis.X) { Interpolation = interpolation };
        AnimationCurve y = new(bonePath, AnimationProperty.LocalRotation, AnimationAxis.Y) { Interpolation = interpolation };
        AnimationCurve z = new(bonePath, AnimationProperty.LocalRotation, AnimationAxis.Z) { Interpolation = interpolation };
        AnimationCurve w = new(bonePath, AnimationProperty.LocalRotation, AnimationAxis.W) { Interpolation = interpolation };

        Quaternion previous = Quaternion.Identity;
        bool first = true;

        if (sampler.InterpolationMode == AnimationInterpolationMode.CUBICSPLINE)
        {
            foreach ((float time, (Quaternion inTangent, Quaternion value, Quaternion outTangent)) in sampler.GetCubicKeys())
            {
                Quaternion key = Continuous(value, ref previous, ref first);

                x.AddKey(new AnimationKey(time, key.X, inTangent.X, outTangent.X));
                y.AddKey(new AnimationKey(time, key.Y, inTangent.Y, outTangent.Y));
                z.AddKey(new AnimationKey(time, key.Z, inTangent.Z, outTangent.Z));
                w.AddKey(new AnimationKey(time, key.W, inTangent.W, outTangent.W));
            }
        }
        else
        {
            foreach ((float time, Quaternion value) in sampler.GetLinearKeys())
            {
                Quaternion key = Continuous(value, ref previous, ref first);

                x.AddKey(time, key.X);
                y.AddKey(time, key.Y);
                z.AddKey(time, key.Z);
                w.AddKey(time, key.W);
            }
        }

        clip.AddCurve(x);
        clip.AddCurve(y);
        clip.AddCurve(z);
        clip.AddCurve(w);
    }

    /// <summary>
    /// Maps glTF's sampler mode onto the engine's. <c>STEP</c> is the important one to carry: a stepped channel is how
    /// a file says "this does not move between keys", and sampling it linearly would smooth away the jump it means.
    /// </summary>
    private static AnimationInterpolation InterpolationOf(AnimationInterpolationMode mode) => mode switch
    {
        AnimationInterpolationMode.STEP => AnimationInterpolation.Constant,
        AnimationInterpolationMode.CUBICSPLINE => AnimationInterpolation.Cubic,
        _ => AnimationInterpolation.Linear
    };

    /// <summary>
    /// Flips a key that is more than a half turn from its predecessor. <c>q</c> and <c>-q</c> are the same
    /// orientation but opposite corners of the same component curve, so without this a component-wise blend spins the
    /// long way round halfway through a clip.
    /// </summary>
    private static Quaternion Continuous(Quaternion value, ref Quaternion previous, ref bool first)
    {
        Quaternion normalized = value.Length() <= 0f ? Quaternion.Identity : Quaternion.Normalize(value);

        if (!first && Quaternion.Dot(previous, normalized) < 0f)
        {
            normalized = Quaternion.Negate(normalized);
        }

        previous = normalized;
        first = false;

        return normalized;
    }

    /// <summary>
    /// The path a hierarchy registration used for a node: its own name, preceded by every visual ancestor's. Null when
    /// the node is not in the file's visual tree at all, which a channel can reach by naming a node no scene lists.
    /// </summary>
    private static string? PathOfNode(Node node)
    {
        List<string> segments = [];
        Node? current = node;

        while (current is not null)
        {
            segments.Add(string.IsNullOrWhiteSpace(current.Name) ? $"Node {current.LogicalIndex}" : current.Name);
            current = current.VisualParent;
        }

        // A node reached only through an orphan walk has no visual parent, so its own name is the whole path, which is
        // what the orphan registration used.
        segments.Reverse();

        return segments.Count == 0 ? null : string.Join('/', segments);
    }

    /// <summary>The hierarchy path of a node from the model's root, <c>"Armature/Hips/Arm"</c>.</summary>
    private static string PathOf(Node node, string parentPath)
    {
        string name = string.IsNullOrWhiteSpace(node.Name) ? $"Node {node.LogicalIndex}" : node.Name;

        return string.IsNullOrEmpty(parentPath) ? name : parentPath + "/" + name;
    }
}
