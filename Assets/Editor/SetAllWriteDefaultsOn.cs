using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class SetAllWriteDefaultsOn
{
    [MenuItem("Tools/Animator/Set ALL Write Defaults ON")]
    private static void SetAllOn()
    {
        var controller = Selection.activeObject as AnimatorController;

        if (controller == null)
        {
            EditorUtility.DisplayDialog(
                "Set Write Defaults ON",
                "请先在 Project 窗口中选中一个 Animator Controller (.controller)",
                "OK"
            );
            return;
        }

        int changed = 0;
        int total = 0;

        foreach (var layer in controller.layers)
        {
            ProcessStateMachine(layer.stateMachine, ref total, ref changed);
        }

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();

        Debug.Log(
            $"[Write Defaults] {controller.name}: " +
            $"扫描 {total} 个 State，修改 {changed} 个为 ON。"
        );
    }

    private static void ProcessStateMachine(
        AnimatorStateMachine stateMachine,
        ref int total,
        ref int changed)
    {
        foreach (var childState in stateMachine.states)
        {
            var state = childState.state;
            total++;

            if (!state.writeDefaultValues)
            {
                Undo.RecordObject(state, "Set Write Defaults ON");
                state.writeDefaultValues = true;
                EditorUtility.SetDirty(state);
                changed++;
            }
        }

        foreach (var childMachine in stateMachine.stateMachines)
        {
            ProcessStateMachine(
                childMachine.stateMachine,
                ref total,
                ref changed
            );
        }
    }

    [MenuItem("Tools/Animator/Set ALL Write Defaults ON", true)]
    private static bool ValidateSetAllOn()
    {
        return Selection.activeObject is AnimatorController;
    }
}