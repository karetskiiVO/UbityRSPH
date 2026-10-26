using NUnit.Framework;

using UnityEditor;

using UnityEngine;

namespace RSPH.Tests {
    public sealed class SPHScanTests {
        [Test]
        public void HierarchicalGridPreservesEveryParticleAcrossThreeScanLevels() {
            if (!SystemInfo.supportsComputeShaders) Assert.Ignore("Compute shaders required.");
            var shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Scripts/RSPH/Shaders/sph.compute"));
            const int cells = 301 * 3 * 301, count = 513;
            try {
                using (var input = new ComputeBuffer(count, 32))
                using (var grid = new SPHSpatialGrid(shader, count, cells)) {
                    // Each particle's position.w is a persistent test identifier.
                    var data = new Vector4[count * 2];
                    for (int i = 0; i < count; i++) {
                        int cell = (i * 509) % cells;
                        data[i * 2] = new Vector4((cell % 301 + .5f) * .2f,
                            ((cell / 301) % 3 + .5f) * .2f, (cell / 903 + .5f) * .2f, i);
                    }
                    input.SetData(data);
                    shader.SetInt("_ParticleCount", count);
                    shader.SetInt("_CellCount", cells);
                    shader.SetInts("_GridSize", 301, 3, 301);
                    shader.SetVector("_BoundsMin", Vector4.zero);
                    shader.SetFloat("_SmoothingRadius", .2f);
                    grid.Build(input);
                    var starts = new uint[cells]; var counts = new uint[cells];
                    grid.CellStarts.GetData(starts); grid.CellCounts.GetData(counts);
                    uint total = 0;
                    for (int cell = 0; cell < cells; cell++) {
                        Assert.That(starts[cell], Is.EqualTo(total), "Exclusive prefix at cell " + cell);
                        total += counts[cell];
                    }
                    Assert.That(total, Is.EqualTo(count));
                    var sorted = new Vector4[count * 2]; grid.SortedParticles.GetData(sorted);
                    var seen = new bool[count];
                    for (int i = 0; i < count; i++) {
                        int id = (int)sorted[i * 2].w;
                        Assert.That(id, Is.InRange(0, count - 1));
                        Assert.That(seen[id], Is.False, "Scatter must not duplicate particles.");
                        seen[id] = true;
                        Assert.That(sorted[i * 2], Is.EqualTo(data[id * 2]));
                    }
                }
            } finally { Object.DestroyImmediate(shader); }
        }

        [Test]
        public void ExclusiveScanHandlesPartialBlocksAndBlockOffsets() {
            if (!SystemInfo.supportsComputeShaders) Assert.Ignore("Compute shaders required.");
            var shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Scripts/RSPH/Shaders/sph.compute"));
            try {
                Assert.That(shader.HasKernel("ScanCells"), Is.True, "Million-particle grid requires a GPU prefix scan.");
                using (var input = new ComputeBuffer(1003, 4))
                using (var output = new ComputeBuffer(1003, 4))
                using (var sums = new ComputeBuffer(4, 4))
                using (var parents = new ComputeBuffer(4, 4)) {
                    var values = new uint[1003];
                    values[0] = 3; values[255] = 7; values[256] = 11; values[1002] = 5;
                    input.SetData(values);
                    parents.SetData(new uint[] { 0, 10, 21, 21 });
                    int scan = shader.FindKernel("ScanCells");
                    shader.SetInt("_ScanCount", 1003);
                    shader.SetBuffer(scan, "_ScanInput", input);
                    shader.SetBuffer(scan, "_ScanOutput", output);
                    shader.SetBuffer(scan, "_ScanSums", sums);
                    shader.Dispatch(scan, 4, 1, 1);
                    var blockSums = new uint[4]; sums.GetData(blockSums);
                    Assert.That(blockSums, Is.EqualTo(new uint[] { 10, 11, 0, 5 }));
                    int add = shader.FindKernel("AddBlockOffsets");
                    shader.SetBuffer(add, "_ScanOutput", output);
                    shader.SetBuffer(add, "_ParentOffsets", parents);
                    shader.Dispatch(add, 4, 1, 1);
                    var actual = new uint[1003]; output.GetData(actual);
                    Assert.That(actual[0], Is.EqualTo(0));
                    Assert.That(actual[1], Is.EqualTo(3));
                    Assert.That(actual[255], Is.EqualTo(3));
                    Assert.That(actual[256], Is.EqualTo(10));
                    Assert.That(actual[257], Is.EqualTo(21));
                    Assert.That(actual[1002], Is.EqualTo(21));
                }
            } finally { Object.DestroyImmediate(shader); }
        }
    }
}
