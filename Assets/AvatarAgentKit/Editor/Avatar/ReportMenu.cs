// The menu, parameters and an animator layer (FX by default) of a baked clone, as VRChat receives them after
// Modular Avatar has merged everything: the menu tree with each control's parameter, the synced cost, and each
// layer with its states, Write Defaults and the parameters it tests (conditions, blend trees, Motion Time).
// Controls whose parameter doesn't exist are marked.
//
// With find, it traces a toggle: the menu controls and parameters whose name contains it, the layers that test
// those parameters, and what those layers animate, plus every binding whose path or property contains find.
// Bindings to an object that doesn't exist in the clone are marked "missing": that animation does nothing.
//
// From code (execute_code):  return AvatarAgentKit.Editor.ReportMenu.Run("MyAvatar");
//                            return AvatarAgentKit.Editor.ReportMenu.Run("MyAvatar", find: "Hat");
//                            return AvatarAgentKit.Editor.ReportMenu.Run("MyAvatar", layers: "Gesture");
// The target is an avatar (its _bakecheck clone is read) or the clone itself. Menu: Tools/Avatar Agent Kit/
// Report Menu; the result goes to the Console. Long sections are capped; narrow them with find.
//
// Verified on: Unity 2022.3.22f1, VRChat SDK 3.10.5, MA 1.18; a MANUKA avatar with MA toggles and FaceEmo.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace AvatarAgentKit.Editor
{
    public static class ReportMenu
    {
        const string Tool = "ReportMenu";
        const int MaxMenuLines = 80, MaxLayerLines = 60, MaxBindingLines = 60;

        public static string Run(string target = null, string find = null, string layers = "FX", int depth = 4)
        {
            var err = Targets.FindBaked(target, out var clone);
            if (err != null) return Tool + ": refused: " + err;
            if (!Enum.TryParse(layers, true, out VRCAvatarDescriptor.AnimLayerType layerType))
                return Tool + ": refused: unknown layers " + Targets.Quote(layers) + ". One of: "
                    + string.Join(", ", Enum.GetNames(typeof(VRCAvatarDescriptor.AnimLayerType)));
            var desc = clone.GetComponent<VRCAvatarDescriptor>();
            var parameters = desc.expressionParameters != null
                ? desc.expressionParameters.parameters.Where(p => p != null && !string.IsNullOrEmpty(p.name)).ToList()
                : new List<VRCExpressionParameters.Parameter>();
            var known = new HashSet<string>(parameters.Select(p => p.name));
            var controller = Controller(desc, layerType);
            find = string.IsNullOrWhiteSpace(find) ? null : find.Trim();

            var sb = new StringBuilder();
            var menuLines = new List<string>();
            int controls = 0;
            if (desc.expressionsMenu != null) WalkMenu(desc.expressionsMenu, "", 0, depth, known, menuLines, ref controls, new HashSet<VRCExpressionsMenu>());
            sb.Append(Tool).Append(": ").Append(Targets.Quote(clone.name)).Append(": menu ").Append(controls).Append(" controls; parameters ")
                .Append(parameters.Count).Append(" (").Append(parameters.Count(p => p.networkSynced)).Append(" synced, ")
                .Append(desc.expressionParameters != null ? desc.expressionParameters.CalcTotalCost() : 0).Append(" / ")
                .Append(VRCExpressionParameters.MAX_PARAMETER_COST).Append(" bits); ").Append(layerType).Append(": ")
                .AppendLine(controller != null ? controller.layers.Length + " layers" : "none");

            if (find == null)
            {
                sb.AppendLine().Append("menu (").Append(depth).AppendLine(" levels):");
                AppendCapped(sb, menuLines, MaxMenuLines, "  ");
                if (controller != null) AppendLayers(sb, controller, null);
                return sb.ToString().TrimEnd();
            }

            // find: trace what matches
            sb.AppendLine().Append("matching ").Append(Targets.Quote(find)).AppendLine(":");
            var menuHits = menuLines.Where(l => Contains(l, find)).Select(l => l.Trim()).ToList();
            sb.AppendLine("menu:");
            AppendCapped(sb, menuHits, MaxMenuLines, "  ");
            var paramHits = parameters.Where(p => Contains(p.name, find)).ToList();
            sb.AppendLine("parameters:");
            if (paramHits.Count == 0) sb.AppendLine("  none");
            foreach (var p in paramHits)
                sb.Append("  ").Append(p.name).Append(": ").Append(p.valueType).Append(p.networkSynced ? ", synced" : ", local")
                    .Append(p.saved ? ", saved" : "").Append(", default ").AppendLine(p.defaultValue.ToString(CultureInfo.InvariantCulture));
            if (controller == null) return sb.ToString().TrimEnd();

            var traced = new HashSet<string>(paramHits.Select(p => p.name));
            foreach (var p in controller.parameters) if (Contains(p.name, find)) traced.Add(p.name);
            AppendLayers(sb, controller, traced);
            AppendBindings(sb, clone, controller, find, traced);
            return sb.ToString().TrimEnd();
        }

        static AnimatorController Controller(VRCAvatarDescriptor desc, VRCAvatarDescriptor.AnimLayerType type)
        {
            foreach (var l in desc.baseAnimationLayers.Concat(desc.specialAnimationLayers ?? new VRCAvatarDescriptor.CustomAnimLayer[0]))
                if (l.type == type && !l.isDefault && l.animatorController != null) return l.animatorController as AnimatorController;
            return null;
        }

        static void WalkMenu(VRCExpressionsMenu menu, string indent, int level, int depth, HashSet<string> known,
            List<string> lines, ref int count, HashSet<VRCExpressionsMenu> seen)
        {
            if (menu == null || !seen.Add(menu)) return;
            foreach (var c in menu.controls)
            {
                count++;
                var sb = new StringBuilder(indent).Append(c.name);
                if (c.type == VRCExpressionsMenu.Control.ControlType.SubMenu) sb.Append("/");
                var details = new List<string> { c.type.ToString() };
                var p = c.parameter != null ? c.parameter.name : null;
                if (!string.IsNullOrEmpty(p))
                    details.Add(p + (c.type == VRCExpressionsMenu.Control.ControlType.Toggle || c.type == VRCExpressionsMenu.Control.ControlType.Button
                        ? " = " + c.value.ToString(CultureInfo.InvariantCulture) : "") + (known.Contains(p) ? "" : " (no such parameter)"));
                if (c.subParameters != null)
                    foreach (var sp in c.subParameters)
                        if (sp != null && !string.IsNullOrEmpty(sp.name)) details.Add(sp.name + (known.Contains(sp.name) ? "" : " (no such parameter)"));
                sb.Append(" (").Append(string.Join(", ", details)).Append(")");
                lines.Add(sb.ToString());
                if (c.type == VRCExpressionsMenu.Control.ControlType.SubMenu && level + 1 < depth)
                    WalkMenu(c.subMenu, indent + "  ", level + 1, depth, known, lines, ref count, seen);
            }
        }

        // Each layer: states, Write Defaults, the parameters it tests. With `traced`, only layers testing one of them.
        static void AppendLayers(StringBuilder sb, AnimatorController controller, HashSet<string> traced)
        {
            var lines = new List<string>();
            int wdOn = 0, wdOff = 0;
            var offLayers = new List<string>();
            for (int i = 0; i < controller.layers.Length; i++)
            {
                var layer = controller.layers[i];
                var states = States(layer.stateMachine).ToList();
                int on = states.Count(s => s.writeDefaultValues), off = states.Count - on;
                if (states.Count > 0) { if (off == 0) wdOn++; else { wdOff++; offLayers.Add(i + " " + Targets.Quote(layer.name)); } }
                var used = TestedParameters(layer.stateMachine).ToList();
                if (traced != null && !used.Any(traced.Contains)) continue;
                var wd = states.Count == 0 ? "no states" : off == 0 ? "WD on" : on == 0 ? "WD off" : "WD mixed (" + on + " on, " + off + " off)";
                lines.Add(i + " " + Targets.Quote(layer.name) + ": " + states.Count + (states.Count == 1 ? " state, " : " states, ") + wd
                    + (used.Count > 0 ? "; tests " + string.Join(", ", used.Take(6)) + (used.Count > 6 ? ", +" + (used.Count - 6) : "") : ""));
            }
            sb.AppendLine(traced == null ? "layers:" : "layers testing those parameters:");
            AppendCapped(sb, lines, MaxLayerLines, "  ");
            if (traced == null && wdOn > 0 && wdOff > 0)
                sb.Append("Write Defaults differ between layers: ").Append(wdOn).Append(" on, ").Append(wdOff).Append(" off or mixed: ")
                    .AppendLine(string.Join(", ", offLayers.Take(10)) + (offLayers.Count > 10 ? ", …" : ""));
        }

        static void AppendBindings(StringBuilder sb, GameObject clone, AnimatorController controller, string find, HashSet<string> traced)
        {
            var lines = new List<string>();
            var seen = new HashSet<string>();
            int missing = 0;
            for (int i = 0; i < controller.layers.Length; i++)
            {
                var layer = controller.layers[i];
                var layerTraced = TestedParameters(layer.stateMachine).Any(traced.Contains);
                foreach (var state in States(layer.stateMachine))
                    foreach (var clip in Clips(state.motion))
                    {
                        foreach (var b in AnimationUtility.GetCurveBindings(clip))
                        {
                            if (!layerTraced && !Contains(b.path, find) && !Contains(b.propertyName, find)) continue;
                            var curve = AnimationUtility.GetEditorCurve(clip, b);
                            var value = curve != null && curve.length > 0 ? curve.keys[0].value.ToString("0.###", CultureInfo.InvariantCulture) : "?";
                            Add(lines, seen, clone, i, layer.name, state.name, b, value, ref missing);
                        }
                        foreach (var b in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                        {
                            if (!layerTraced && !Contains(b.path, find) && !Contains(b.propertyName, find)) continue;
                            var keys = AnimationUtility.GetObjectReferenceCurve(clip, b);
                            var value = keys != null && keys.Length > 0 && keys[0].value != null ? keys[0].value.name : "none";
                            Add(lines, seen, clone, i, layer.name, state.name, b, value, ref missing);
                        }
                    }
            }
            sb.Append("bindings (layer / state: [path] component.property = first key)");
            sb.AppendLine(missing > 0 ? "; " + missing + " point at objects that don't exist in the clone:" : ":");
            AppendCapped(sb, lines, MaxBindingLines, "  ");
        }

        static void Add(List<string> lines, HashSet<string> seen, GameObject clone, int index, string layer, string state,
            EditorCurveBinding b, string value, ref int missing)
        {
            var exists = string.IsNullOrEmpty(b.path) || clone.transform.Find(b.path) != null;
            var line = index + " " + layer + " / " + state + ": [" + b.path + "] " + b.type.Name + "." + b.propertyName + " = " + value + (exists ? "" : "  (missing)");
            if (!seen.Add(line)) return;
            if (!exists) missing++;
            lines.Add(line);
        }

        static IEnumerable<AnimatorState> States(AnimatorStateMachine sm)
        {
            if (sm == null) yield break;
            foreach (var cs in sm.states) yield return cs.state;
            foreach (var child in sm.stateMachines)
                foreach (var s in States(child.stateMachine)) yield return s;
        }

        static IEnumerable<AnimationClip> Clips(Motion motion)
        {
            if (motion is AnimationClip clip) { yield return clip; yield break; }
            if (motion is BlendTree tree)
                foreach (var child in tree.children)
                    foreach (var c in Clips(child.motion)) yield return c;
        }

        // Parameters in transition conditions and blend trees, in order of first use.
        static IEnumerable<string> TestedParameters(AnimatorStateMachine root)
        {
            var seen = new HashSet<string>();
            foreach (var name in Tested(root)) if (!string.IsNullOrEmpty(name) && seen.Add(name)) yield return name;
        }

        static IEnumerable<string> Tested(AnimatorStateMachine sm)
        {
            if (sm == null) yield break;
            var transitions = new List<AnimatorTransitionBase>();
            transitions.AddRange(sm.anyStateTransitions);
            transitions.AddRange(sm.entryTransitions);
            foreach (var cs in sm.states) transitions.AddRange(cs.state.transitions);
            foreach (var child in sm.stateMachines) transitions.AddRange(sm.GetStateMachineTransitions(child.stateMachine));
            foreach (var t in transitions)
                foreach (var c in t.conditions) yield return c.parameter;
            foreach (var cs in sm.states)
            {
                var s = cs.state;
                if (s.timeParameterActive) yield return s.timeParameter; // radial puppets drive Motion Time
                if (s.speedParameterActive) yield return s.speedParameter;
                if (s.mirrorParameterActive) yield return s.mirrorParameter;
                if (s.cycleOffsetParameterActive) yield return s.cycleOffsetParameter;
                foreach (var p in TreeParameters(s.motion as BlendTree)) yield return p;
            }
            foreach (var child in sm.stateMachines)
                foreach (var p in Tested(child.stateMachine)) yield return p;
        }

        static IEnumerable<string> TreeParameters(BlendTree tree)
        {
            if (tree == null) yield break;
            if (tree.blendType == BlendTreeType.Direct)
            {
                foreach (var child in tree.children) yield return child.directBlendParameter;
            }
            else
            {
                yield return tree.blendParameter;
                if (tree.blendType != BlendTreeType.Simple1D) yield return tree.blendParameterY;
            }
            foreach (var child in tree.children)
                foreach (var p in TreeParameters(child.motion as BlendTree)) yield return p;
        }

        static bool Contains(string s, string find) => s != null && s.IndexOf(find, StringComparison.OrdinalIgnoreCase) >= 0;

        static void AppendCapped(StringBuilder sb, List<string> lines, int max, string indent)
        {
            if (lines.Count == 0) { sb.Append(indent).AppendLine("none"); return; }
            foreach (var l in lines.Take(max)) sb.Append(indent).AppendLine(l);
            if (lines.Count > max) sb.Append(indent).Append("… ").Append(lines.Count - max).AppendLine(" more; narrow it with find");
        }

        [MenuItem("Tools/Avatar Agent Kit/Report Menu", false, 111)]
        static void ReportMenuMenu() => Debug.Log(Run(Targets.SelectedRootName()));
    }
}
