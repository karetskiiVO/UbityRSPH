using System.IO;

using UnityEditor;
using UnityEditor.SceneManagement;

using UnityEngine;
using UnityEngine.SceneManagement;

namespace RSPH.Editor {
    public static class SPHDemoBuilder {
        const string Root = "Assets/Scripts/RSPH";

        [MenuItem("RSPH/Create Container Demo")]
        public static void CreateDemoScene() {
            // Additive creation preserves the scene the user is currently editing.
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            var fluid = new GameObject("GPU SPH Liquid");
            var simulation = fluid.AddComponent<SPHSimulation>();
            simulation.solver = AssetDatabase.LoadAssetAtPath<ComputeShader>(Root + "/Shaders/sph.compute");
            simulation.particleMaterial = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/Particles.mat");
            var cameraObject = new GameObject("SPH Camera", typeof(Camera), typeof(AudioListener));
            var camera = cameraObject.GetComponent<Camera>();
            camera.tag = "MainCamera";
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.015f, .025f, .045f);
            camera.nearClipPlane = .03f;
            camera.farClipPlane = 100;
            camera.fieldOfView = 48;
            camera.transform.SetPositionAndRotation(new Vector3(3.59f, 2.58f, -5.74f), Quaternion.Euler(22, -32, 0));
            var controls = fluid.AddComponent<SPHDemoControls>();
            controls.simulation = simulation;
            controls.viewCamera = camera;
            controls.tankMaterial = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/Tank.mat");
            controls.BuildContainerVisuals();
            Directory.CreateDirectory("Assets/Scenes");
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/Scenes/SPHDemo.unity");
            EditorSceneManager.SaveScene(scene, path);
            Selection.activeGameObject = fluid;
            SceneView.lastActiveSceneView?.Frame(new Bounds(Vector3.zero, simulation.containerSize * 1.5f), false);
            Debug.Log("SPH demo saved to " + path + ". Press Play to run.");
        }
    }
}
