// Sets Write Defaults ON on every state of the selected Animator Controller, sub-state machines included.
// Menu: Tools/Avatar Agent Kit/Set All Write Defaults ON (select a .controller in the Project window first).
// Undoable. Mixing WD ON and OFF states in one avatar is a common source of stuck toggles and faces.

using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarAgentKit.Editor
{
    public static class SetAllWriteDefaultsOn
    {
        const string MenuPath = "Tools/Avatar Agent Kit/Set All Write Defaults ON";

        [MenuItem(MenuPath)]
        static void SetAllOn()
        {
            var controller = Selection.activeObject as AnimatorController;
            if (controller == null)
            {
                EditorUtility.DisplayDialog(
                    "Set Write Defaults ON",
                    "Select an Animator Controller (.controller) in the Project window first.",
                    "OK");
                return;
            }

            int changed = 0, total = 0;
            foreach (var layer in controller.layers)
                ProcessStateMachine(layer.stateMachine, ref total, ref changed);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Write Defaults] {controller.name}: {total} states scanned, {changed} set to ON.");
        }

        static void ProcessStateMachine(AnimatorStateMachine stateMachine, ref int total, ref int changed)
        {
            foreach (var childState in stateMachine.states)
            {
                var state = childState.state;
                total++;
                if (state.writeDefaultValues)
                    continue;
                Undo.RecordObject(state, "Set Write Defaults ON");
                state.writeDefaultValues = true;
                EditorUtility.SetDirty(state);
                changed++;
            }

            foreach (var childMachine in stateMachine.stateMachines)
                ProcessStateMachine(childMachine.stateMachine, ref total, ref changed);
        }

        [MenuItem(MenuPath, true)]
        static bool ValidateSetAllOn() => Selection.activeObject is AnimatorController;
    }
}
