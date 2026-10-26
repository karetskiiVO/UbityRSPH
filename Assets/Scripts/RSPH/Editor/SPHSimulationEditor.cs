using UnityEditor;

using UnityEngine;

namespace RSPH.Editor {
    [CustomEditor(typeof(SPHSimulation))]
    public sealed class SPHSimulationEditor : UnityEditor.Editor {
        public override void OnInspectorGUI() {
            var simulation = (SPHSimulation)target;
            EditorGUILayout.LabelField("Large particle presets", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("1 Million")) Preset(simulation, 100);
            if (GUILayout.Button("2 Million")) Preset(simulation, 128);
            if (GUILayout.Button("4 Million")) Preset(simulation, 160);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox("Presets fit the particle block to the tank, use billboards and one substep per frame. Simulation may run slower than real time.", MessageType.Info);
            DrawDefaultInspector();
            if (!string.IsNullOrEmpty(simulation.LastError)) EditorGUILayout.HelpBox(simulation.LastError, MessageType.Error);
            if (simulation.IsInitialized)
                EditorGUILayout.LabelField($"GPU: {simulation.AllocatedGpuBytes / 1048576f:F1} MiB | step: {simulation.CurrentTimeStep * 1000:F3} ms");
        }

        static void Preset(SPHSimulation simulation, int axis) {
            Undo.RecordObject(simulation, "SPH particle preset");
            simulation.billboardMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Scripts/RSPH/Materials/Billboards.mat");
            simulation.SetLargePreset(axis);
            EditorUtility.SetDirty(simulation);
        }
    }
}
