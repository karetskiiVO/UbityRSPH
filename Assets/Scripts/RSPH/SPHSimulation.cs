using System;

using UnityEngine;
using UnityEngine.Rendering;

namespace RSPH {
    [DisallowMultipleComponent]
    [AddComponentMenu("RSPH/SPH Simulation")]
    public sealed class SPHSimulation : MonoBehaviour {
        public enum ParticleRendering { Spheres, Billboards, Hidden }
        [Header("GPU assets")]
        public ComputeShader solver;
        public Material particleMaterial;
        public Material billboardMaterial;

        [Header("Container (local units)")]
        public Vector3 containerSize = new Vector3(4, 3, 3);
        [Range(0, 1)] public float boundaryDamping = .25f;

        [Header("Particles (changing these resets the simulation)")]
        public Vector3Int spawnCounts = new Vector3Int(20, 20, 20);
        public bool autoFitParticles;
        [Range(.1f, .95f)] public float spawnFillFraction = .65f;
        [Min(.00001f)] public float particleSpacing = .1f;
        [Min(.00001f)] public float particleRadius = .045f;
        [Min(.00002f)] public float smoothingRadius = .2f;
        [Min(64)] public int gpuMemoryBudgetMB = 1536;

        [Header("Fluid")]
        [Min(1)] public float restDensity = 1000;
        [Min(0)] public float pressureStiffness = 120;
        [Min(0)] public float viscosity = .05f;
        public Vector3 gravity = new Vector3(0, -9.81f, 0);
        [Min(.1f)] public float maxSpeed = 15;

        [Header("Time")]
        [Range(.0001f, .004f)] public float fixedTimeStep = .002f;
        [Range(1, 64)] public int maxSubsteps = 16;
        [Range(0, 2)] public float timeScale = 1;
        public bool paused;

        [Header("Appearance")]
        public ParticleRendering particleRendering;
        [Min(1)] public int renderEveryNthParticle = 1;
        public Color slowColor = new Color(.015f, .22f, .5f, 1);
        public Color fastColor = new Color(.12f, .85f, 1, 1);
        [Range(.5f, 2)] public float renderRadiusScale = 1.15f;

        public bool IsInitialized => particles != null;
        public int ActiveParticleCount { get; private set; }
        public ComputeBuffer ParticleBuffer => particles;
        public float SimulationTime { get; private set; }
        public string LastError { get; private set; }
        public long AllocatedGpuBytes { get; private set; }
        public float CurrentTimeStep => IsInitialized ? StableTimeStep() : 0;
        int RenderStride => Mathf.Clamp(renderEveryNthParticle, 1, Mathf.Max(1, ActiveParticleCount));
        public int RenderedParticleCount => particleRendering == ParticleRendering.Hidden ? 0 :
            (ActiveParticleCount + RenderStride - 1) / RenderStride;
        public Bounds WorldRenderBounds {
            get {
                Bounds bounds = new Bounds(transform.position, Vector3.zero);
                for (int i = 0; i < 8; i++)
                    bounds.Encapsulate(transform.TransformPoint(Vector3.Scale(containerSize * .5f,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
                Vector3 scale = transform.lossyScale;
                float largestScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                bounds.Expand(2 * particleRadius * renderRadiusScale * largestScale);
                return bounds;
            }
        }

        public const int MaxParticles = 16000000;
        public const int MaxCells = 16000000;
        ComputeShader compute;
        Material material;
        Mesh sphere;
        MaterialPropertyBlock properties;
        ComputeBuffer particles, densityPressure, forces;
        SPHSpatialGrid spatialGrid;
        int initializeKernel, densityKernel, forceKernel, integrateKernel;
        int cellCount;
        Vector3Int gridSize;
        float accumulator;
        bool settingsDirty;

        void OnEnable() {
            if (Application.isPlaying) InitializeSimulation();
        }

        void OnDisable() => ReleaseResources();
        void OnDestroy() => ReleaseResources();
        void OnValidate() => settingsDirty = true;

        [ContextMenu("Reset Simulation")]
        public void ResetSimulation() {
            if (Application.isPlaying) InitializeSimulation();
        }

        public void SetPaused(bool value) => paused = value;

        public void InitializeSimulation() {
            settingsDirty = false;
            LastError = null;
            ReleaseResources();
            try {
                ValidateSettings();
                // Every component owns a shader instance: buffer bindings never leak across simulations.
                compute = Instantiate(solver);
                compute.name = solver.name + " (SPH instance)";
                initializeKernel = compute.FindKernel("Initialize");
                densityKernel = compute.FindKernel("CalculateDensityPressure");
                forceKernel = compute.FindKernel("CalculateForces");
                integrateKernel = compute.FindKernel("Integrate");

                ActiveParticleCount = spawnCounts.x * spawnCounts.y * spawnCounts.z;
                gridSize = new Vector3Int(Mathf.CeilToInt(containerSize.x / smoothingRadius),
                    Mathf.CeilToInt(containerSize.y / smoothingRadius), Mathf.CeilToInt(containerSize.z / smoothingRadius));
                cellCount = gridSize.x * gridSize.y * gridSize.z;
                particles = new ComputeBuffer(ActiveParticleCount, 32);
                densityPressure = new ComputeBuffer(ActiveParticleCount, 8);
                forces = new ComputeBuffer(ActiveParticleCount, 16);
                spatialGrid = new SPHSpatialGrid(compute, ActiveParticleCount, cellCount);

                compute.SetBuffer(initializeKernel, "_Particles", particles);
                spatialGrid.BindNeighbors(densityKernel);
                spatialGrid.BindNeighbors(forceKernel);
                compute.SetBuffer(densityKernel, "_DensityPressure", densityPressure);
                compute.SetBuffer(forceKernel, "_DensityPressure", densityPressure);
                compute.SetBuffer(forceKernel, "_Forces", forces);
                compute.SetBuffer(integrateKernel, "_Particles", particles);
                compute.SetBuffer(integrateKernel, "_ParticlesRead", spatialGrid.SortedParticles);
                compute.SetBuffer(integrateKernel, "_Forces", forces);
                compute.SetInt("_ParticleCount", ActiveParticleCount);
                compute.SetInt("_CellCount", cellCount);
                compute.SetInts("_GridSize", gridSize.x, gridSize.y, gridSize.z);
                compute.SetInts("_SpawnCounts", spawnCounts.x, spawnCounts.y, spawnCounts.z);
                compute.SetFloat("_ParticleSpacing", particleSpacing);

                Vector3 extent = (Vector3)(spawnCounts - Vector3Int.one) * particleSpacing;
                Vector3 free = containerSize - extent - Vector3.one * (2 * particleRadius);
                // A block near the left side, slightly lifted above the floor, collapses into the tank.
                Vector3 origin = -containerSize * .5f + Vector3.one * particleRadius +
                    new Vector3(free.x * .08f, free.y * .4f, free.z * .5f);
                compute.SetVector("_SpawnOrigin", origin);
                UploadParameters();
                Dispatch(initializeKernel, ActiveParticleCount);
                AllocatedGpuBytes = EstimateGpuBytes(ActiveParticleCount, cellCount);

                Material template = particleRendering == ParticleRendering.Billboards ? billboardMaterial : particleMaterial;
                if (particleRendering == ParticleRendering.Billboards && template == null) {
                    Shader billboard = Shader.Find("RSPH/Particle Billboards");
                    if (billboard == null || !billboard.isSupported) throw new InvalidOperationException("Assign the SPH billboard material.");
                    material = new Material(billboard) { enableInstancing = true };
                } else if (particleRendering != ParticleRendering.Hidden && template != null) {
                    material = new Material(template) { enableInstancing = true };
                }
                if (material != null) {
                    if (particleRendering == ParticleRendering.Spheres) sphere = CreateSphere();
                    properties = new MaterialPropertyBlock();
                    properties.SetBuffer("_Particles", particles);
                }
                accumulator = 0;
                SimulationTime = 0;
                settingsDirty = false;
            } catch (Exception exception) {
                ReleaseResources();
                LastError = exception.Message;
                Debug.LogError("SPH could not start: " + LastError, this);
            }
        }

        void ValidateSettings() {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsInstancing)
                throw new InvalidOperationException("Compute shaders and GPU instancing are required.");
            if (solver == null) throw new InvalidOperationException("Assign the SPH compute shader.");
            Material selectedMaterial = particleRendering == ParticleRendering.Billboards ? billboardMaterial : particleMaterial;
            if (selectedMaterial != null && !selectedMaterial.shader.isSupported)
                throw new InvalidOperationException("The particle material shader is unsupported.");
            if (spawnCounts.x < 1 || spawnCounts.y < 1 || spawnCounts.z < 1 ||
                (long)spawnCounts.x * spawnCounts.y > MaxParticles ||
                (long)spawnCounts.x * spawnCounts.y * spawnCounts.z > MaxParticles)
                throw new ArgumentException("Spawn counts must be positive, with at most 16000000 particles.");
            if (autoFitParticles) FitParticlesToContainer();
            if (!PositiveFinite(particleSpacing) || !PositiveFinite(particleRadius) ||
                !PositiveFinite(smoothingRadius) || smoothingRadius < particleSpacing ||
                !PositiveFinite(restDensity) || !PositiveFinite(maxSpeed) ||
                !PositiveFinite(fixedTimeStep) || !PositiveFinite(containerSize.x) ||
                !PositiveFinite(containerSize.y) || !PositiveFinite(containerSize.z))
                throw new ArgumentException("Sizes, density, time step and speed must be finite and positive; smoothing radius must cover spacing.");
            if (!Finite(pressureStiffness) || pressureStiffness < 0 || !Finite(viscosity) || viscosity < 0 ||
                !Finite(gravity.x) || !Finite(gravity.y) || !Finite(gravity.z))
                throw new ArgumentException("Fluid forces must be finite, with nonnegative stiffness and viscosity.");
            Vector3 spawnExtent = (Vector3)(spawnCounts - Vector3Int.one) * particleSpacing +
                Vector3.one * (2 * particleRadius);
            if (spawnExtent.x >= containerSize.x || spawnExtent.y >= containerSize.y || spawnExtent.z >= containerSize.z)
                throw new ArgumentException("The initial particle block must fit inside the container.");
            double cells = Math.Ceiling(containerSize.x / (double)smoothingRadius) *
                Math.Ceiling(containerSize.y / (double)smoothingRadius) * Math.Ceiling(containerSize.z / (double)smoothingRadius);
            if (cells > MaxCells) throw new ArgumentException("The spatial grid exceeds 16000000 cells; increase smoothing radius or reduce the container.");
            long count = (long)spawnCounts.x * spawnCounts.y * spawnCounts.z;
            if (SystemInfo.maxGraphicsBufferSize > 0 && count * 32 > SystemInfo.maxGraphicsBufferSize)
                throw new ArgumentException("A particle buffer exceeds the graphics device buffer-size limit.");
            long budget = (long)Mathf.Max(64, gpuMemoryBudgetMB) * 1024 * 1024;
            if (SystemInfo.graphicsMemorySize > 0) budget = Math.Min(budget, (long)SystemInfo.graphicsMemorySize * 1024 * 1024 / 2);
            long bytes = EstimateGpuBytes((int)count, (int)cells);
            if (bytes > budget)
                throw new ArgumentException($"SPH needs approximately {bytes / (1024 * 1024)} MiB; the GPU budget is {budget / (1024 * 1024)} MiB. Reduce counts or increase GPU Memory Budget MB.");
        }

        public void FitParticlesToContainer() {
            if (spawnCounts.x < 1 || spawnCounts.y < 1 || spawnCounts.z < 1 ||
                !PositiveFinite(containerSize.x) || !PositiveFinite(containerSize.y) || !PositiveFinite(containerSize.z)) return;
            float fill = Finite(spawnFillFraction) ? Mathf.Clamp(spawnFillFraction, .1f, .95f) : .65f;
            particleSpacing = Mathf.Min(containerSize.x * fill / (spawnCounts.x - 1 + .9f),
                containerSize.y * fill / (spawnCounts.y - 1 + .9f), containerSize.z * fill / (spawnCounts.z - 1 + .9f));
            particleRadius = particleSpacing * .45f;
            smoothingRadius = particleSpacing * 2;
        }

        [ContextMenu("Presets/1 Million (100 cubed)")]
        public void UseOneMillionPreset() => SetLargePreset(100);
        [ContextMenu("Presets/2 Million (128 cubed)")]
        public void UseTwoMillionPreset() => SetLargePreset(128);
        [ContextMenu("Presets/4 Million (160 cubed)")]
        public void UseFourMillionPreset() => SetLargePreset(160);

        public void SetLargePreset(int particlesPerAxis) {
            spawnCounts = Vector3Int.one * particlesPerAxis;
            autoFitParticles = true;
            FitParticlesToContainer();
            maxSubsteps = 1;
            particleRendering = ParticleRendering.Billboards;
            renderEveryNthParticle = 1;
            settingsDirty = true;
            if (!enabled) enabled = true;
            else if (Application.isPlaying) InitializeSimulation();
        }

        public static long EstimateGpuBytes(int count, int cells) {
            if (count < 0 || cells < 1) throw new ArgumentOutOfRangeException("Particle count and cell count must be valid.");
            long bytes = 92L * count + 8L * cells;
            while (true) {
                int groups = (cells + 255) / 256;
                bytes += 4L * groups;
                if (groups == 1) return bytes;
                bytes += 4L * groups;
                cells = groups;
            }
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static bool PositiveFinite(float value) => Finite(value) && value > 0;

        void UploadParameters() {
            compute.SetVector("_BoundsMin", -containerSize * .5f);
            compute.SetVector("_BoundsMax", containerSize * .5f);
            compute.SetFloat("_SmoothingRadius", smoothingRadius);
            compute.SetFloat("_ParticleRadius", particleRadius);
            compute.SetFloat("_ParticleMass", restDensity * particleSpacing * particleSpacing * particleSpacing);
            compute.SetFloat("_RestDensity", restDensity);
            compute.SetFloat("_PressureStiffness", pressureStiffness);
            compute.SetFloat("_Viscosity", viscosity);
            compute.SetFloat("_BoundaryDamping", Mathf.Clamp01(boundaryDamping));
            compute.SetFloat("_MaxSpeed", maxSpeed);
            compute.SetVector("_Gravity", transform.InverseTransformDirection(gravity));
        }

        float StableTimeStep() {
            float cfl = .25f * smoothingRadius / (maxSpeed + Mathf.Sqrt(pressureStiffness));
            float viscous = .125f * smoothingRadius * smoothingRadius / Mathf.Max(viscosity, 1e-6f);
            return Mathf.Max(1e-6f, Mathf.Min(fixedTimeStep, .004f, cfl, viscous));
        }

        void Update() {
            if (settingsDirty) InitializeSimulation();
            if (!IsInitialized) return;
            if (!paused) {
                float step = StableTimeStep();
                int budget = Mathf.Clamp(maxSubsteps, 1, 64);
                // Drop excess backlog rather than increasing dt after a slow frame.
                accumulator = Mathf.Min(accumulator + Mathf.Min(Time.deltaTime, .05f) * Mathf.Clamp(timeScale, 0, 2), step * budget);
                UploadParameters();
                for (int n = 0; n < budget && accumulator >= step; n++) {
                    DispatchStep(step);
                    accumulator -= step;
                }
            }
            RenderParticles();
        }

        public void SimulateStep(float deltaTime) {
            if (!IsInitialized || !PositiveFinite(deltaTime)) return;
            UploadParameters();
            DispatchStep(Mathf.Min(deltaTime, StableTimeStep()));
        }

        [ContextMenu("Single Step")]
        public void SingleStep() => SimulateStep(fixedTimeStep);

        void DispatchStep(float step) {
            compute.SetFloat("_DeltaTime", step);
            spatialGrid.Build(particles);
            Dispatch(densityKernel, ActiveParticleCount);
            Dispatch(forceKernel, ActiveParticleCount);
            Dispatch(integrateKernel, ActiveParticleCount);
            SimulationTime += step;
        }

        void Dispatch(int kernel, int elements) {
            SPHSpatialGrid.Dispatch(compute, kernel, elements);
        }

        void RenderParticles() {
            if (material == null || particleRendering == ParticleRendering.Hidden) return;
            properties.SetMatrix("_SimulationToWorld", transform.localToWorldMatrix);
            properties.SetFloat("_Radius", particleRadius * renderRadiusScale);
            properties.SetInt("_ParticleStride", RenderStride);
            properties.SetColor("_SlowColor", slowColor);
            properties.SetColor("_FastColor", fastColor);
            properties.SetFloat("_ColorSpeed", maxSpeed * .4f);
            var parameters = new RenderParams(material) {
                worldBounds = WorldRenderBounds,
                matProps = properties,
                layer = gameObject.layer,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false
            };
            if (particleRendering == ParticleRendering.Billboards)
                Graphics.RenderPrimitives(parameters, MeshTopology.Triangles, 6, RenderedParticleCount);
            else if (sphere != null)
                Graphics.RenderMeshPrimitives(parameters, sphere, 0, RenderedParticleCount);
        }

        static Mesh CreateSphere() {
            const int rings = 6, segments = 10;
            var vertices = new Vector3[(rings + 1) * (segments + 1)];
            var triangles = new int[rings * segments * 6];
            for (int y = 0; y <= rings; y++)
                for (int x = 0; x <= segments; x++) {
                    float phi = Mathf.PI * y / rings, theta = 2 * Mathf.PI * x / segments;
                    vertices[y * (segments + 1) + x] = new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta),
                        Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(theta));
                }
            int index = 0;
            for (int y = 0; y < rings; y++)
                for (int x = 0; x < segments; x++) {
                    int a = y * (segments + 1) + x, b = a + segments + 1;
                    triangles[index++] = a; triangles[index++] = a + 1; triangles[index++] = b;
                    triangles[index++] = a + 1; triangles[index++] = b + 1; triangles[index++] = b;
                }
            var mesh = new Mesh { name = "SPH particle sphere", vertices = vertices, normals = vertices, triangles = triangles };
            mesh.RecalculateBounds();
            return mesh;
        }

        void ReleaseResources() {
            particles?.Release(); particles = null;
            densityPressure?.Release(); densityPressure = null;
            forces?.Release(); forces = null;
            spatialGrid?.Dispose(); spatialGrid = null;
            DestroyOwned(compute); compute = null;
            DestroyOwned(material); material = null;
            DestroyOwned(sphere); sphere = null;
            properties = null;
            ActiveParticleCount = 0;
            AllocatedGpuBytes = 0;
            accumulator = 0;
        }

        static void DestroyOwned(UnityEngine.Object asset) {
            if (asset == null) return;
            if (Application.isPlaying) Destroy(asset); else DestroyImmediate(asset);
        }

        void OnDrawGizmos() {
            Gizmos.color = new Color(.2f, .75f, 1, .7f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, containerSize);
        }
    }
}
