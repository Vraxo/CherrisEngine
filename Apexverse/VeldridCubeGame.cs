using System.Linq;
using System.Numerics;

namespace VeldridCube
{
    public class VeldridCubeGame : Engine
    {
        private GameObject _spinningCube;
        private float _totalTime;

        public VeldridCubeGame() : base("Veldrid Engine Demo")
        {
        }

        protected override void LoadContent()
        {
            // Pre-load assets that the scene will need
            ResourceManager.LoadInitialAssets();

            // Load the scene from the file
            var loadedObjects = SceneLoader.LoadScene("Assets/Scene.yaml");
            Scene.AddRange(loadedObjects);

            // Find the cube we want to animate
            _spinningCube = Scene.FirstOrDefault(go => go.Name == "SpinningCube");
        }

        protected override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            _totalTime += deltaTime;

            // Update the spinning cube's transform
            if (_spinningCube != null)
            {
                _spinningCube.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, _totalTime) *
                                                   Quaternion.CreateFromAxisAngle(Vector3.UnitX, _totalTime * 0.7f);
            }
        }
    }
}