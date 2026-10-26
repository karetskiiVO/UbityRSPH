using System;
using System.Collections.Generic;

using UnityEngine;

namespace RSPH {
    /// <summary>GPU counting sort into contiguous cell ranges. No particle readback.</summary>
    public sealed class SPHSpatialGrid : IDisposable {
        public ComputeBuffer SortedParticles { get; private set; }
        public ComputeBuffer CellCounts { get; private set; }
        public ComputeBuffer CellStarts { get; private set; }

        readonly ComputeShader shader;
        readonly int particleCount, cellCount;
        readonly int clear, count, scan, add, scatter;
        ComputeBuffer particleSlots;
        readonly List<ScanLevel> levels = new List<ScanLevel>();

        sealed class ScanLevel {
            public int count;
            public ComputeBuffer input, output, sums;
        }

        public SPHSpatialGrid(ComputeShader shader, int particleCount, int cellCount) {
            this.shader = shader;
            this.particleCount = particleCount;
            this.cellCount = cellCount;
            clear = shader.FindKernel("ClearGrid");
            count = shader.FindKernel("BuildGrid");
            scan = shader.FindKernel("ScanCells");
            add = shader.FindKernel("AddBlockOffsets");
            scatter = shader.FindKernel("ScatterParticles");
            try {
                SortedParticles = new ComputeBuffer(particleCount, 32);
                particleSlots = new ComputeBuffer(particleCount, 4);
                CellCounts = new ComputeBuffer(cellCount, 4);
                CellStarts = new ComputeBuffer(cellCount, 4);
                int elements = cellCount;
                ComputeBuffer input = CellCounts, output = CellStarts;
                while (true) {
                    int groups = (elements + 255) / 256;
                    var level = new ScanLevel { count = elements, input = input, output = output };
                    levels.Add(level);
                    level.sums = new ComputeBuffer(groups, 4);
                    if (groups == 1) break;
                    input = level.sums;
                    output = new ComputeBuffer(groups, 4);
                    elements = groups;
                }
                shader.SetBuffer(clear, "_CellCounts", CellCounts);
                shader.SetBuffer(count, "_CellCounts", CellCounts);
                shader.SetBuffer(count, "_ParticleSlots", particleSlots);
                shader.SetBuffer(scatter, "_CellStarts", CellStarts);
                shader.SetBuffer(scatter, "_ParticleSlots", particleSlots);
                shader.SetBuffer(scatter, "_SortedParticles", SortedParticles);
            } catch { Dispose(); throw; }
        }

        public void BindNeighbors(int kernel) {
            shader.SetBuffer(kernel, "_ParticlesRead", SortedParticles);
            shader.SetBuffer(kernel, "_CellCounts", CellCounts);
            shader.SetBuffer(kernel, "_CellStarts", CellStarts);
        }

        public void Build(ComputeBuffer particles) {
            shader.SetBuffer(count, "_ParticlesRead", particles);
            shader.SetBuffer(scatter, "_ParticlesRead", particles);
            Dispatch(shader, clear, cellCount);
            Dispatch(shader, count, particleCount);
            foreach (var level in levels) {
                shader.SetInt("_ScanCount", level.count);
                shader.SetBuffer(scan, "_ScanInput", level.input);
                shader.SetBuffer(scan, "_ScanOutput", level.output);
                shader.SetBuffer(scan, "_ScanSums", level.sums);
                Dispatch(shader, scan, level.count);
            }
            for (int i = levels.Count - 2; i >= 0; i--) {
                shader.SetInt("_ScanCount", levels[i].count);
                shader.SetBuffer(add, "_ScanOutput", levels[i].output);
                shader.SetBuffer(add, "_ParentOffsets", levels[i + 1].output);
                Dispatch(shader, add, levels[i].count);
            }
            Dispatch(shader, scatter, particleCount);
        }

        public static void Dispatch(ComputeShader shader, int kernel, int elements) {
            shader.GetKernelThreadGroupSizes(kernel, out uint x, out _, out _);
            int groups = (elements + (int)x - 1) / (int)x;
            if (groups > 65535) throw new ArgumentOutOfRangeException(nameof(elements), "GPU dispatch exceeds 65535 groups.");
            shader.Dispatch(kernel, groups, 1, 1);
        }

        public void Dispose() {
            foreach (var level in levels) {
                level.sums?.Release();
                if (level.output != CellStarts) level.output?.Release();
            }
            levels.Clear();
            SortedParticles?.Release(); SortedParticles = null;
            CellCounts?.Release(); CellCounts = null;
            CellStarts?.Release(); CellStarts = null;
            particleSlots?.Release(); particleSlots = null;
        }
    }
}
