using System.Numerics;

namespace Cherris
{
    public class Scene : IDisposable
    {
        public Guid Id { get; } = Guid.NewGuid();
        public string Name { get; set; }
        public string FilePath { get; set; }
        public List<GameObject> GameObjects { get; } = new List<GameObject>();
        public Camera MainCamera { get; set; }
        public Skybox Skybox { get; private set; }
        public bool IsDirty { get; set; }

        public Scene(string filePath, List<GameObject> gameObjects)
        {
            FilePath = filePath;
            Name = string.IsNullOrEmpty(filePath) ? "Untitled Scene" : Path.GetFileName(filePath);
            GameObjects.AddRange(gameObjects);
            FindMainComponents();
        }

        public void AddGameObject(GameObject go)
        {
            GameObjects.Add(go);
            IsDirty = true;
        }

        public void RemoveGameObject(GameObject go)
        {
            // Recursively remove children first to avoid modifying collection during iteration
            foreach (var childTransform in go.Transform.Children.ToList())
            {
                RemoveGameObject(childTransform.GameObject);
            }

            // Remove the object from its parent's list
            go.Transform.Parent = null;

            // Remove from the root scene list
            GameObjects.Remove(go);

            // Dispose its managed resources
            go.GetComponent<MeshRenderer>()?.Dispose();
            IsDirty = true;
        }

        public void Start(PhysicsSystem physicsSystem)
        {
            FindMainComponents();

            // Initialize scripts
            foreach (var gameObject in GameObjects)
            {
                foreach (var script in gameObject.GetComponents<Script>())
                {
                    if (script is RigidBody rb)
                    {
                        rb.Initialize(physicsSystem);
                    }
                    script.Start();
                }
            }

            // Handle case where no camera was found in the scene
            if (MainCamera is null)
            {
                CreateDefaultCamera();
            }
        }

        public void Update(float deltaTime)
        {
            foreach (var gameObject in GameObjects)
            {
                foreach (var script in gameObject.GetComponents<Script>())
                {
                    if (script.Enabled)
                    {
                        script.Update(deltaTime);
                    }
                }
            }
        }

        public void FindMainComponents()
        {
            MainCamera = null;
            Skybox = null;
            foreach (var gameObject in GameObjects)
            {
                if (MainCamera is null) MainCamera = gameObject.GetComponent<Camera>();
                if (Skybox is null) Skybox = gameObject.GetComponent<Skybox>();
            }
        }

        private void CreateDefaultCamera()
        {
            Console.WriteLine("Warning: No active camera found in scene. Creating a default one.");
            var go = new GameObject("Default Camera");
            go.Transform.Position = new Vector3(0, 1, 3);
            MainCamera = go.AddComponent(new Camera());
            GameObjects.Add(go);
        }

        public void Dispose()
        {
            foreach (var gameObject in GameObjects)
            {
                gameObject.GetComponent<MeshRenderer>()?.Dispose();
            }
        }
    }
}