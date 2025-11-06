using Cherris.OpenTK;
using Cherris.Rendering;
using System.Globalization;
using System.Numerics;

namespace Cherris;

public abstract class Engine
{
    public Action<float>? OnDrawUI;
    public float Exposure { get; set; } = 1.0f;

    public readonly IResourceManager ResourceManager;
    public readonly SceneLoader SceneLoader;
    public readonly SceneManager SceneManager;
    public IRenderer Renderer { get; }

    protected GameObject? SelectedGameObject { get; set; }
    protected readonly RenderingInterface _backend;

    private readonly IGameWindow _gameWindow;
    private readonly GameLoop _gameLoop;

    private float _lastDeltaTime;
    private float _snapshotTimer;
    private const float SnapshotInterval = 1.0f;
    private bool _snapshotsEnabled;

    public Ray CreateRayFromMouse()
    {
        Camera camera = SceneManager.MainCamera;

        if (camera is null)
        {
            return new();
        }

        float x = (2.0f * Input.MousePosition.X) / _gameWindow.Width - 1.0f;
        float y = 1.0f - (2.0f * Input.MousePosition.Y) / _gameWindow.Height;
        Vector4 ndc = new(x, y, 1.0f, 1.0f);

        Matrix4x4.Invert(camera.GetProjectionMatrix(_gameWindow.Width / _gameWindow.Height), out var invProjection);

        Vector4 viewRay = Vector4.Transform(ndc, invProjection);
        viewRay.Z = -1.0f;
        viewRay.W = 0.0f;

        Matrix4x4.Invert(camera.GetViewMatrix(), out var invView);
        Vector4 worldRay = Vector4.Transform(viewRay, invView);

        Vector3 rayDir = Vector3.Normalize(new(worldRay.X, worldRay.Y, worldRay.Z));
        Vector3 rayOrigin = camera.GameObject.Transform.Position;

        return new(rayOrigin, rayDir);
    }

    protected Engine(string windowTitle, bool startWithMouseLocked, GraphicsAPI api)
    {
        _backend = CreateBackend(api);
        _backend.Initialize(windowTitle, 1920, 1080, startWithMouseLocked);

        _gameWindow = _backend.GameWindow;
        ResourceManager = _backend.ResourceManager;
        Renderer = _backend.Renderer;

        // Initialize systems that depend on the backend
        SceneLoader = new(ResourceManager);
        SceneManager = new();

        // Game loop is created last, as it depends on the Update/Draw methods
        _gameLoop = new(_gameWindow, Update, Draw);

        // Subscribe to resize event
        _gameWindow.Resized += OnWindowResized;

        RegisterEngineComponents();
    }

    protected virtual void Update(float deltaTime)
    {
        _lastDeltaTime = deltaTime;

        _backend.UIController?.Update(deltaTime);
        OnDrawUI?.Invoke(deltaTime);

        if (Input.WasKeyPressed(Key.F12))
        {
            _snapshotsEnabled = !_snapshotsEnabled;

            Console.WriteLine(
                $"[Engine] Snapshots {(_snapshotsEnabled ? "enabled" : "disabled")}. " +
                $"Press F12 to toggle.");
        }
    }

    protected virtual void OnStart() { }

    protected abstract void LoadContent();

    private static RenderingInterface CreateBackend(GraphicsAPI api)
    {
        return api switch
        {
            GraphicsAPI.Veldrid => new VeldridBackend(),
            GraphicsAPI.OpenTK => new OpenTKBackend(),
            _ => throw new ArgumentOutOfRangeException(nameof(api), api, null),
        };
    }

    private void RegisterEngineComponents()
    {
        SceneLoader.RegisterComponentFactory("MeshRenderer", (properties) =>
        {
            if (properties is not Dictionary<object, object> propsDict) return null;

            // Get Mesh
            if (!propsDict.TryGetValue("Mesh", out var meshNameObj) || meshNameObj is not string meshName) return null;
            Mesh? mesh = ResourceManager.GetMesh(meshName);
            if (mesh is null) return null;

            // Get Material properties from a nested dictionary
            if (!propsDict.TryGetValue("Material", out var materialObj) || materialObj is not Dictionary<object, object> matProps)
            {
                // For backward compatibility, check for top-level properties.
                matProps = propsDict;
            }


            // Inside Material dictionary
            string textureName = "White"; // Default value
            if (matProps.TryGetValue("Texture", out var textureNameObj) && textureNameObj is string parsedTextureName)
            {
                textureName = parsedTextureName;
            }
            ITexture texture = ResourceManager.GetTexture(textureName);
            var material = new Material(texture, textureName);

            if (matProps.TryGetValue("TextureTiling", out var tilingObj) && tilingObj is List<object> tilingList && tilingList.Count == 2)
            {
                try
                {
                    material.TextureTiling = new Vector2(
                        Convert.ToSingle(tilingList[0], CultureInfo.InvariantCulture),
                        Convert.ToSingle(tilingList[1], CultureInfo.InvariantCulture));
                }
                catch (Exception e)
                {
                    Console.WriteLine($"[Engine] Warning: Could not parse TextureTiling values. Using default. Error: {e.Message}");
                }
            }

            if (matProps.TryGetValue("EmissiveColor", out var emissiveObj) && emissiveObj is List<object> emissiveList && emissiveList.Count == 3)
            {
                try
                {
                    material.EmissiveColor = new(
                        Convert.ToSingle(emissiveList[0], CultureInfo.InvariantCulture),
                        Convert.ToSingle(emissiveList[1], CultureInfo.InvariantCulture),
                        Convert.ToSingle(emissiveList[2], CultureInfo.InvariantCulture));
                }
                catch (Exception e)
                {
                    Console.WriteLine($"[Engine] Warning: Could not parse EmissiveColor values. Using default. Error: {e.Message}");
                }
            }

            if (matProps.TryGetValue("SpecularIntensity", out var specIntObj))
            {
                material.SpecularIntensity = Convert.ToSingle(specIntObj, CultureInfo.InvariantCulture);
            }

            if (matProps.TryGetValue("Shininess", out var shininessObj))
            {
                material.Shininess = Convert.ToSingle(shininessObj, CultureInfo.InvariantCulture);
            }

            return new MeshRenderer(mesh, material, meshName);
        });

        SceneLoader.RegisterComponentFactory("Camera", (properties) =>
        {
            return new Camera();
        });

        SceneLoader.RegisterComponentFactory("Skybox", (properties) =>
        {
            if (properties is not Dictionary<object, object> propsDict)
            {
                return null;
            }

            if (propsDict.TryGetValue("CubeMap", out var cubemapNameObj) && cubemapNameObj is string cubemapName)
            {
                return ResourceManager.GetSkybox(cubemapName);
            }

            return null;
        });

        SceneLoader.RegisterComponentFactory("Light", (properties) =>
        {
            var light = new Light();
            if (properties is not Dictionary<object, object> propsDict)
            {
                return light;
            }

            if (propsDict.TryGetValue("Type", out var typeObj) && Enum.TryParse<LightType>(typeObj as string, out var type))
            {
                light.Type = type;
            }

            if (propsDict.TryGetValue("Color", out var colorObj) && colorObj is List<object> colorList && colorList.Count == 3)
            {
                try
                {
                    light.Color = new Vector3(
                        Convert.ToSingle(colorList[0], CultureInfo.InvariantCulture),
                        Convert.ToSingle(colorList[1], CultureInfo.InvariantCulture),
                        Convert.ToSingle(colorList[2], CultureInfo.InvariantCulture));
                }
                catch (Exception e)
                {
                    Console.WriteLine($"[Engine] Warning: Could not parse Light Color values. Using default. Error: {e.Message}");
                }
            }

            if (propsDict.TryGetValue("Intensity", out var intensityObj))
            {
                light.Intensity = Convert.ToSingle(intensityObj, CultureInfo.InvariantCulture);
            }

            if (propsDict.TryGetValue("AmbientStrength", out var ambientObj))
            {
                light.AmbientStrength = Convert.ToSingle(ambientObj, CultureInfo.InvariantCulture);
            }

            if (light.Type == LightType.Point || light.Type == LightType.Spot)
            {
                if (propsDict.TryGetValue("Range", out var rangeObj))
                {
                    light.Range = Convert.ToSingle(rangeObj, CultureInfo.InvariantCulture);
                }
            }

            if (light.Type == LightType.Spot)
            {
                if (propsDict.TryGetValue("InnerConeAngle", out var innerAngleObj))
                {
                    light.InnerConeAngle = Convert.ToSingle(innerAngleObj, CultureInfo.InvariantCulture);
                }
                if (propsDict.TryGetValue("OuterConeAngle", out var outerAngleObj))
                {
                    light.OuterConeAngle = Convert.ToSingle(outerAngleObj, CultureInfo.InvariantCulture);
                }
            }

            return light;
        });

        SceneLoader.RegisterComponentFactory("RigidBody", (properties) =>
        {
            var rb = new RigidBody();
            if (properties is not Dictionary<object, object> propsDict)
            {
                return rb;
            }

            if (propsDict.TryGetValue("IsStatic", out var isStaticObj))
            {
                rb.IsStatic = Convert.ToBoolean(isStaticObj);
            }

            if (propsDict.TryGetValue("Mass", out var massObj) && !rb.IsStatic)
            {
                rb.Mass = Convert.ToSingle(massObj, CultureInfo.InvariantCulture);
            }

            if (propsDict.TryGetValue("Friction", out var frictionObj))
            {
                rb.Friction = Convert.ToSingle(frictionObj, CultureInfo.InvariantCulture);
            }

            if (propsDict.TryGetValue("Bounciness", out var bouncinessObj))
            {
                rb.Bounciness = Convert.ToSingle(bouncinessObj, CultureInfo.InvariantCulture);
            }

            return rb;
        });
    }

    public void Run()
    {
        LoadContent();
        Start();
        OnStart();
        _gameLoop.Run();
        DisposeResources();
    }

    private void Start()
    {
        SceneManager.Start();
    }

    private void Draw()
    {
        if (_snapshotsEnabled)
        {
            _snapshotTimer += _lastDeltaTime;

            if (_snapshotTimer >= SnapshotInterval)
            {
                _snapshotTimer -= SnapshotInterval;
                string path = $"Snapshots/snap_{DateTime.Now:yyyyMMdd_HHmmss_fff}.bmp";

                Renderer.RequestSnapshot(path);
            }
        }

        Renderer.RenderFrame(
            SceneManager.MainCamera, SceneManager.Skybox, SceneManager.GameObjects,
            SelectedGameObject, _gameWindow.Width, _gameWindow.Height, Exposure);

        Renderer.ProcessSnapshot();
        _gameWindow.SwapBuffers();
    }

    private void OnWindowResized()
    {
        Renderer.OnWindowResized();
    }

    private void DisposeResources()
    {
        SceneManager.Dispose();
        _backend.Dispose();
    }
}