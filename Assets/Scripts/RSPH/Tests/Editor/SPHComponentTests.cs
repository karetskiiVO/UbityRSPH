using System.Collections;

using NUnit.Framework;

using UnityEditor;

using UnityEngine;
using UnityEngine.TestTools;

namespace RSPH.Tests {
    public sealed class SPHComponentTests {
        GameObject root;

        [TearDown]
        public void Cleanup() {
            if (root != null) Object.DestroyImmediate(root);
        }

        [UnityTearDown]
        public IEnumerator LeavePlayMode() {
            if (EditorApplication.isPlaying) yield return new ExitPlayMode();
        }

        [Test]
        public void BillboardBoundsContainRadiusUnderNonuniformScale() {
            root = new GameObject("SPH billboard bounds test");
            var simulation = root.AddComponent<SPHSimulation>();
            root.transform.localScale = new Vector3(10, 1, 1);
            simulation.particleRendering = SPHSimulation.ParticleRendering.Billboards;
            float radius = simulation.particleRadius * simulation.renderRadiusScale * 10;
            Assert.That(simulation.WorldRenderBounds.extents.y,
                Is.EqualTo(1.5f + radius).Within(.0001f));
            Assert.That(simulation.WorldRenderBounds.extents.z,
                Is.EqualTo(1.5f + radius).Within(.0001f));
        }

        [Test]
        [TestCase(100)]
        [TestCase(128)]
        [TestCase(160)]
        public void LargePresetsFitParticleVolumeAndKeepKernelScale(int axis) {
            root = new GameObject("Large SPH preset test");
            var simulation = root.AddComponent<SPHSimulation>();
            simulation.SetLargePreset(axis);
            Vector3 occupied = (Vector3)(simulation.spawnCounts - Vector3Int.one) * simulation.particleSpacing +
                Vector3.one * simulation.particleRadius * 2;
            Assert.That(occupied.x, Is.LessThan(simulation.containerSize.x));
            Assert.That(occupied.y, Is.LessThan(simulation.containerSize.y));
            Assert.That(occupied.z, Is.LessThan(simulation.containerSize.z));
            Assert.That(simulation.smoothingRadius / simulation.particleSpacing, Is.EqualTo(2).Within(.0001));
            Assert.That(simulation.particleRadius / simulation.particleSpacing, Is.EqualTo(.45).Within(.0001));
        }

        [Test]
        public void ContainerVisualsFollowChangedCollisionBounds() {
            root = new GameObject("SPH resizing test");
            var simulation = root.AddComponent<SPHSimulation>();
            var demo = root.AddComponent<SPHDemoControls>();
            demo.simulation = simulation;
            demo.tankMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Scripts/RSPH/Materials/Tank.mat");
            demo.BuildContainerVisuals();
            simulation.containerSize = new Vector3(8, 4, 5);
            demo.BuildContainerVisuals();
            Transform tank = root.transform.Find("Tank Visuals");
            Assert.That(tank.childCount, Is.EqualTo(13), "Resize must reuse the visual objects.");
            Assert.That(tank.GetChild(0).localScale.x, Is.EqualTo(8.08f).Within(.001f));
            Assert.That(tank.GetChild(0).localPosition.y, Is.EqualTo(-2.035f).Within(.001f));
            Assert.That(tank.GetChild(1).localScale.x, Is.EqualTo(8).Within(.001f));
        }

        SPHSimulation CreateSimulation() {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsInstancing)
                Assert.Ignore("A compute-capable graphics device is required.");
            root = new GameObject("SPH lifecycle test");
            root.SetActive(false);
            var simulation = root.AddComponent<SPHSimulation>();
            simulation.solver = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Scripts/RSPH/Shaders/sph.compute");
            simulation.spawnCounts = new Vector3Int(13, 5, 1);
            root.SetActive(true);
            return simulation;
        }

        [UnityTest]
        public IEnumerator ReinitializationResetsGpuStateAndDisableReleasesBuffers() {
            yield return new EnterPlayMode();
            var simulation = CreateSimulation();
            Assert.That(simulation.IsInitialized, Is.True);
            Assert.That(simulation.ActiveParticleCount, Is.EqualTo(65));
            var initial = new Vector4[130]; simulation.ParticleBuffer.GetData(initial);
            simulation.SimulateStep(.002f);
            Assert.That(simulation.SimulationTime, Is.GreaterThan(0));
            simulation.ResetSimulation();
            var reset = new Vector4[130]; simulation.ParticleBuffer.GetData(reset);
            Assert.That(reset, Is.EqualTo(initial));
            Assert.That(simulation.SimulationTime, Is.Zero);
            simulation.enabled = false;
            Assert.That(simulation.IsInitialized, Is.False);
            simulation.enabled = true;
            Assert.That(simulation.IsInitialized, Is.True);
        }

        [UnityTest]
        public IEnumerator SeparateSimulationsKeepIndependentShaderBindings() {
            yield return new EnterPlayMode();
            var first = CreateSimulation();
            var otherObject = new GameObject("Second SPH");
            otherObject.SetActive(false);
            try {
                var second = otherObject.AddComponent<SPHSimulation>();
                second.solver = first.solver;
                second.spawnCounts = new Vector3Int(1, 1, 1);
                second.gravity = Vector3.up;
                otherObject.SetActive(true);
                first.SimulateStep(.001f);
                second.SimulateStep(.001f);
                var a = new Vector4[130]; first.ParticleBuffer.GetData(a);
                var b = new Vector4[2]; second.ParticleBuffer.GetData(b);
                Assert.That(a[1].y, Is.LessThan(0), "First simulation must retain its own gravity and buffers.");
                Assert.That(b[1].y, Is.GreaterThan(0), "Second simulation must use its separate shader instance.");
            } finally { Object.DestroyImmediate(otherObject); }
        }

        [Test]
        public void OversizedGridIsRejectedBeforeBufferAllocation() {
            var simulation = CreateSimulation();
            simulation.containerSize = Vector3.one * 1000;
            LogAssert.Expect(LogType.Error, "SPH could not start: The spatial grid exceeds 16000000 cells; increase smoothing radius or reduce the container.");
            simulation.InitializeSimulation();
            Assert.That(simulation.IsInitialized, Is.False);
            Assert.That(simulation.enabled, Is.True, "Invalid settings must remain editable without manually re-enabling the component.");
        }
    }
}
