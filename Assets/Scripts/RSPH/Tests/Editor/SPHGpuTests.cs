using System;
using System.Runtime.InteropServices;

using NUnit.Framework;

using UnityEditor;

using UnityEngine;

namespace RSPH.Tests {
    // These tests dispatch the shipping compute shader on the real graphics device.
    public sealed class SPHGpuTests {
        [StructLayout(LayoutKind.Sequential)]
        struct Particle {
            public Vector4 position;
            public Vector4 velocity;
            public Particle(Vector3 p, Vector3 v = default) {
                position = new Vector4(p.x, p.y, p.z, 0);
                velocity = new Vector4(v.x, v.y, v.z, 0);
            }
        }

        ComputeShader shader;
        ComputeBuffer particles, density, forces;
        SPHSpatialGrid grid;
        int count;

        [TearDown]
        public void Dispose() {
            particles?.Release(); density?.Release(); forces?.Release();
            grid?.Dispose();
            if (shader != null) UnityEngine.Object.DestroyImmediate(shader);
        }

        void Setup(params Particle[] initial) {
            if (!SystemInfo.supportsComputeShaders || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("A compute-capable graphics device is required.");
            shader = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Assets/Scripts/RSPH/Shaders/sph.compute"));
            Assert.That(shader, Is.Not.Null);
            count = initial.Length;
            particles = new ComputeBuffer(count, 32);
            density = new ComputeBuffer(count, 8);
            forces = new ComputeBuffer(count, 16);
            grid = new SPHSpatialGrid(shader, count, 1000);
            particles.SetData(initial);
            shader.SetInt("_ParticleCount", count);
            shader.SetInts("_GridSize", 10, 10, 10);
            shader.SetInt("_CellCount", 1000);
            shader.SetVector("_BoundsMin", new Vector4(-1, -1, -1, 0));
            shader.SetVector("_BoundsMax", new Vector4(1, 1, 1, 0));
            shader.SetFloat("_SmoothingRadius", .2f);
            shader.SetFloat("_ParticleRadius", .04f);
            shader.SetFloat("_ParticleMass", 1);
            shader.SetFloat("_RestDensity", 100);
            shader.SetFloat("_PressureStiffness", 120);
            shader.SetFloat("_Viscosity", .05f);
            shader.SetFloat("_BoundaryDamping", .3f);
            shader.SetFloat("_MaxSpeed", 15);
            shader.SetVector("_Gravity", Vector4.zero);
            grid.BindNeighbors(shader.FindKernel("CalculateDensityPressure"));
            grid.BindNeighbors(shader.FindKernel("CalculateForces"));
            Bind("CalculateDensityPressure", "_DensityPressure", density);
            Bind("CalculateForces", "_DensityPressure", density, "_Forces", forces);
            Bind("Integrate", "_Particles", particles, "_ParticlesRead", grid.SortedParticles, "_Forces", forces);
        }

        void Bind(string kernel, params object[] bindings) {
            int k = shader.FindKernel(kernel);
            for (int i = 0; i < bindings.Length; i += 2)
                shader.SetBuffer(k, (string)bindings[i], (ComputeBuffer)bindings[i + 1]);
        }

        void Dispatch(string kernel, int elements) {
            int k = shader.FindKernel(kernel);
            shader.GetKernelThreadGroupSizes(k, out uint x, out _, out _);
            shader.Dispatch(k, Mathf.CeilToInt(elements / (float)x), 1, 1);
        }

        void Calculate() {
            grid.Build(particles);
            Dispatch("CalculateDensityPressure", count);
            Dispatch("CalculateForces", count);
        }

        [Test]
        public void IsolatedParticleHasAnalyticalPoly6DensityAndNoInternalForce() {
            Setup(new Particle(Vector3.zero));
            Calculate();
            var rho = new Vector2[1]; density.GetData(rho);
            // 315 / (64 pi h^3), h = 0.2, mass = 1.
            Assert.That(rho[0].x, Is.EqualTo(195.8352f).Within(.01f));
            var acceleration = new Vector4[1]; forces.GetData(acceleration);
            Assert.That(((Vector3)acceleration[0]).magnitude, Is.LessThan(1e-5f));
        }

        [Test]
        public void PressureRepelsPairWithEqualAndOppositeForces() {
            Setup(new Particle(new Vector3(-.05f, 0, 0)), new Particle(new Vector3(.05f, 0, 0)));
            Calculate();
            var a = new Vector4[2]; forces.GetData(a);
            var sorted = new Particle[2]; grid.SortedParticles.GetData(sorted);
            Assert.That(a[0].x * sorted[0].position.x, Is.GreaterThan(0));
            Assert.That(a[1].x * sorted[1].position.x, Is.GreaterThan(0));
            Assert.That(((Vector3)(a[0] + a[1])).magnitude, Is.LessThan(.001f));
        }

        [Test]
        public void ViscosityReducesRelativeVelocity() {
            Setup(new Particle(new Vector3(-.05f, 0, 0), Vector3.up),
                new Particle(new Vector3(.05f, 0, 0), Vector3.down));
            shader.SetFloat("_PressureStiffness", 0);
            Calculate();
            shader.SetFloat("_DeltaTime", .001f);
            Dispatch("Integrate", count);
            var result = new Particle[2]; particles.GetData(result);
            Assert.That(Mathf.Abs(result[0].velocity.y), Is.InRange(0f, .99999f));
            Assert.That(Mathf.Abs(result[1].velocity.y), Is.InRange(0f, .99999f));
        }

        [Test]
        public void PartialGroupAndCrowdedCellIncludeEveryNeighbor() {
            var initial = new Particle[65];
            for (int i = 0; i < initial.Length; i++) initial[i] = new Particle(Vector3.zero);
            Setup(initial);
            Calculate();
            var rho = new Vector2[65]; density.GetData(rho);
            foreach (var value in rho)
                Assert.That(value.x, Is.EqualTo(12729.288f).Within(.5f));
        }

        [Test]
        public void IntegrationContainsParticlesAndReflectsOnlyOutwardVelocity() {
            Setup(new Particle(new Vector3(.959f, 0, 0), Vector3.right * 5));
            Calculate();
            shader.SetFloat("_DeltaTime", .01f);
            Dispatch("Integrate", count);
            var result = new Particle[1]; particles.GetData(result);
            Assert.That(result[0].position.x, Is.EqualTo(.96f).Within(1e-5f));
            Assert.That(result[0].velocity.x, Is.EqualTo(-1.5f).Within(1e-5f));
        }

        [Test]
        public void FluidRemainsFiniteAndInsideContainerAfterThreeSeconds() {
            var initial = new Particle[125];
            for (int i = 0; i < initial.Length; i++)
                initial[i] = new Particle(new Vector3((i % 5) * .1f - .5f,
                    ((i / 5) % 5) * .1f, (i / 25) * .1f - .2f));
            Setup(initial);
            shader.SetFloat("_RestDensity", 1000);
            shader.SetVector("_Gravity", new Vector4(0, -9.81f, 0, 0));
            shader.SetFloat("_DeltaTime", .002f);
            for (int step = 0; step < 1500; step++) {
                Calculate();
                Dispatch("Integrate", count);
            }
            var result = new Particle[count]; particles.GetData(result);
            foreach (var p in result) {
                Assert.That(float.IsNaN(p.position.x + p.position.y + p.position.z), Is.False);
                Assert.That(Mathf.Abs(p.position.x), Is.LessThanOrEqualTo(.96001f));
                Assert.That(Mathf.Abs(p.position.y), Is.LessThanOrEqualTo(.96001f));
                Assert.That(Mathf.Abs(p.position.z), Is.LessThanOrEqualTo(.96001f));
                Assert.That(((Vector3)p.velocity).magnitude, Is.LessThanOrEqualTo(15.001f));
            }
            float meanY = 0;
            foreach (var p in result) meanY += p.position.y / count;
            Assert.That(meanY, Is.LessThan(-.25f), "Gravity should move the fluid toward the floor.");
        }
    }
}
