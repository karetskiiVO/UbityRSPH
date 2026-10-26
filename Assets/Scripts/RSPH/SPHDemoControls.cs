using UnityEngine;

namespace RSPH {
    [DisallowMultipleComponent]
    public sealed class SPHDemoControls : MonoBehaviour {
        public SPHSimulation simulation;
        public Camera viewCamera;
        public Material tankMaterial;
        public bool showControls = true;
        public float yaw = -32;
        public float pitch = 22;
        public float distance = 7.3f;
        Material ownedMaterial;
        Vector3 visualSize;

        void Start() {
            if (simulation == null) simulation = GetComponent<SPHSimulation>();
            if (viewCamera == null) viewCamera = Camera.main;
            BuildContainerVisuals();
            UpdateCamera();
        }

        public void BuildContainerVisuals() {
            if (simulation == null) return;
            Material material = tankMaterial;
            if (material == null) {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) return;
                if (ownedMaterial == null) ownedMaterial = new Material(shader);
                ownedMaterial.SetColor("_BaseColor", new Color(.065f, .12f, .17f));
                material = ownedMaterial;
            }
            Transform root = transform.Find("Tank Visuals");
            if (root == null) {
                root = new GameObject("Tank Visuals").transform;
                root.SetParent(transform, false);
            }
            Vector3 size = simulation.containerSize;
            visualSize = size;
            int index = 0;
            const float rail = .018f;
            UpdateBox(root, index++, "Floor", new Vector3(0, -size.y * .5f - .035f, 0),
                new Vector3(size.x + .08f, .07f, size.z + .08f), material);
            for (int axis = 0; axis < 3; axis++)
                for (int a = -1; a <= 1; a += 2)
                    for (int b = -1; b <= 1; b += 2) {
                        Vector3 center = Vector3.zero, scale = Vector3.one * rail;
                        scale[axis] = size[axis];
                        int other = (axis + 1) % 3, last = (axis + 2) % 3;
                        center[other] = a * size[other] * .5f;
                        center[last] = b * size[last] * .5f;
                        UpdateBox(root, index++, "Tank edge", center, scale, material);
                    }
        }

        static void UpdateBox(Transform parent, int index, string objectName, Vector3 position, Vector3 scale, Material material) {
            GameObject box = index < parent.childCount ? parent.GetChild(index).gameObject :
                GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = objectName;
            box.transform.SetParent(parent, false);
            box.transform.localPosition = position;
            box.transform.localScale = scale;
            box.GetComponent<Renderer>().sharedMaterial = material;
            // GPU collisions use the analytic tank, not PhysX colliders.
            var collider = box.GetComponent<Collider>();
            if (collider == null) return;
            collider.enabled = false;
            if (Application.isPlaying) Destroy(collider); else DestroyImmediate(collider);
        }

        void LateUpdate() {
            if (simulation != null && visualSize != simulation.containerSize) BuildContainerVisuals();
            UpdateCamera();
        }

        void UpdateCamera() {
            if (viewCamera == null) return;
            Vector3 target = transform.TransformPoint(new Vector3(0, -.15f, 0));
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0);
            viewCamera.transform.SetPositionAndRotation(target + rotation * Vector3.back * distance, rotation);
        }

        void OnGUI() {
            if (simulation == null) return;
            Event e = Event.current;
            if (e.type == EventType.MouseDrag && e.button == 1) {
                yaw += e.delta.x * .3f;
                pitch = Mathf.Clamp(pitch + e.delta.y * .3f, -10, 80);
                e.Use();
            }
            if (e.type == EventType.ScrollWheel && (!showControls || e.mousePosition.x > 328 || e.mousePosition.y > 333)) {
                distance = Mathf.Clamp(distance + e.delta.y * .2f, 3, 16);
                e.Use();
            }
            if (!showControls) return;
            GUILayout.BeginArea(new Rect(18, 18, 310, 315), GUI.skin.box);
            GUILayout.Label("GPU SPH / LIQUID CONTAINER");
            GUILayout.Space(6);
            if (simulation.IsInitialized) {
                GUILayout.Label($"{simulation.ActiveParticleCount:N0} particles  |  {simulation.SimulationTime:F1} s");
                GUILayout.Label($"Drawn: {simulation.RenderedParticleCount:N0} | GPU: {simulation.AllocatedGpuBytes / 1048576f:F0} MiB");
                GUILayout.Label($"Step: {simulation.CurrentTimeStep * 1000:F3} ms | {simulation.maxSubsteps} substeps/frame max");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(simulation.paused ? "Resume" : "Pause")) simulation.SetPaused(!simulation.paused);
                if (GUILayout.Button("Reset")) simulation.ResetSimulation();
                GUI.enabled = simulation.paused;
                if (GUILayout.Button("Step")) simulation.SingleStep();
                GUI.enabled = true;
                GUILayout.EndHorizontal();
                GUILayout.Space(8);
                GUILayout.Label($"Simulation speed: {simulation.timeScale:F2}x");
                simulation.timeScale = GUILayout.HorizontalSlider(simulation.timeScale, .1f, 2);
            } else {
                GUILayout.Label("Simulation unavailable. Check the Console.");
                if (GUILayout.Button("Retry")) { simulation.enabled = true; simulation.ResetSimulation(); }
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("1 Million")) simulation.UseOneMillionPreset();
            if (GUILayout.Button("2 Million")) simulation.UseTwoMillionPreset();
            if (GUILayout.Button("4 Million")) simulation.UseFourMillionPreset();
            GUILayout.EndHorizontal();
            GUILayout.Space(12);
            GUILayout.Label("Right drag: orbit  |  Wheel: zoom");
            GUILayout.Label("Blue = slow   /   Cyan = fast");
            GUILayout.Label("Fluid settings are on SPH Simulation.");
            GUILayout.EndArea();
        }

        void OnDestroy() {
            if (ownedMaterial == null) return;
            if (Application.isPlaying) Destroy(ownedMaterial); else DestroyImmediate(ownedMaterial);
        }
    }
}
