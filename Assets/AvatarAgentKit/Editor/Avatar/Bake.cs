// Bakes an avatar the way VRChat receives it: a clone named <avatar>_bakecheck, run through NDMF
// (Modular Avatar, AAO, Meshia, FaceEmo and the rest). Judge changes on the clone, never on the scene
// avatar: that one is pre-NDMF, and NDMF preview can make it look already processed.
//
// From code (execute_code):  return AvatarAgentKit.Editor.Bake.Run("MyAvatar");
//                            return AvatarAgentKit.Editor.Bake.Cleanup("MyAvatar");
// The avatar name may be left out when the open scenes hold only one avatar.
// Menu: Tools/Avatar Agent Kit/Bake Avatar and Clean Up Bake (the avatar selected in the Hierarchy, or the
// only one); the result goes to the Console.
//
// Run hides the source avatar so the scene shows only the clone, and replaces any earlier clone: after a
// change, Run again. NDMF's errors and warnings come back in the result. If the call times out, don't bake
// again: the editor finishes the bake first, and the next call (e.g. ReportPerformance) finds the clone.
// Cleanup destroys the clone, restores the source and saves the scene only if the scene had no unsaved
// changes before the first bake. That state is kept in SessionState: it survives script reloads, not an
// editor restart.
//
// Verified on: Unity 2022.3.22f1, NDMF 1.14.8, VRChat SDK 3.10.5; a MANUKA avatar with MA, AAO, Meshia
// and FaceEmo.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using nadena.dev.ndmf;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AvatarAgentKit.Editor
{
    public static class Bake
    {
        const int MaxErrors = 10;

        public static string Run(string avatar = null)
        {
            const string tool = "Bake";
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return tool + ": refused: play mode is on. Leave play mode, then run it again.";
            var err = Targets.FindSource(avatar, out var source);
            if (err != null) return tool + ": refused: " + err;

            var cloneName = source.name + Targets.BakeSuffix;
            var existing = Targets.RootsNamed(cloneName);
            // The first bake records the scene as the user left it; a bake that replaces a clone keeps that record.
            if (existing.Count == 0) Remember(source);
            foreach (var old in existing) Object.DestroyImmediate(old);

            var watch = System.Diagnostics.Stopwatch.StartNew();
            var clone = Object.Instantiate(source);
            clone.name = cloneName;
            if (clone.scene != source.scene) SceneManager.MoveGameObjectToScene(clone, source.scene);
            clone.SetActive(true);
            List<ErrorContext> errors;
            try
            {
                errors = ErrorReport.CaptureErrors(() => AvatarProcessor.ProcessAvatar(clone));
            }
            catch (Exception e)
            {
                Object.DestroyImmediate(clone);
                return tool + ": failed: NDMF threw " + e.GetType().Name + ": " + e.Message + " The clone was removed; the source is unchanged.";
            }
            source.SetActive(false);

            var sb = new StringBuilder();
            sb.Append(tool).Append(": baked ").Append(Targets.Quote(cloneName))
                .Append(" in ").Append((watch.ElapsedMilliseconds / 1000.0).ToString("F1")).Append(" s; ")
                .Append(Targets.Quote(source.name)).Append(" is hidden")
                .AppendLine(existing.Count > 0 ? "; the earlier clone was replaced." : ".");
            var reported = errors.Where(e => e.TheError != null && e.TheError.Severity != ErrorSeverity.Information).ToList();
            if (reported.Count > 0)
            {
                sb.Append("NDMF reported ").Append(reported.Count).AppendLine(":");
                foreach (var e in reported.Take(MaxErrors))
                    sb.Append("  ").Append(e.TheError.Severity).Append(" in ").Append(e.PassName ?? "?").Append(": ")
                        .AppendLine(FirstLine(e.TheError.ToMessage()));
                if (reported.Count > MaxErrors) sb.Append("  (").Append(reported.Count - MaxErrors).AppendLine(" more in the NDMF error report window)");
            }
            var q = Targets.Quote(cloneName);
            sb.Append("Next: ").Append(Targets.Ns).Append("ReportPerformance.Run(").Append(q).Append("), ")
                .Append(Targets.Ns).Append("RenderAvatar.Run(").Append(q).Append("); when done: ")
                .Append(Targets.Ns).Append("Bake.Cleanup(").Append(Targets.Quote(source.name)).Append(")");
            return sb.ToString();
        }

        public static string Cleanup(string avatar = null)
        {
            const string tool = "Bake.Cleanup";
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return tool + ": refused: play mode is on. Leave play mode, then run it again.";

            var sourceName = avatar;
            if (string.IsNullOrEmpty(sourceName))
            {
                var clones = Targets.Roots().Where(g => Targets.IsBakedName(g.name)).ToList();
                if (clones.Count > 1) return tool + ": refused: several baked clones, name the avatar: " + Targets.Names(clones);
                if (clones.Count == 1) sourceName = Targets.SourceName(clones[0].name);
                else
                {
                    var err = Targets.FindSource(null, out var only);
                    if (err != null) return tool + ": refused: " + err;
                    sourceName = only.name;
                }
            }
            if (Targets.IsBakedName(sourceName)) sourceName = Targets.SourceName(sourceName);

            var sourceErr = Targets.FindSource(sourceName, out var source);
            if (sourceErr != null) return tool + ": refused: " + sourceErr;
            var cloneName = source.name + Targets.BakeSuffix;
            var clonesNamed = Targets.RootsNamed(cloneName);
            var state = SessionState.GetString(Key(source), "");
            if (clonesNamed.Count == 0 && state == "")
                return tool + ": nothing to clean up: no " + Targets.Quote(cloneName) + " and no bake recorded.";

            foreach (var c in clonesNamed) Object.DestroyImmediate(c);
            var wasActive = state.Length != 2 || state[1] == '1';
            source.SetActive(wasActive);
            SessionState.EraseString(Key(source));

            string saved;
            if (state.Length != 2)
                saved = "scene not saved: there is no record of it before the bake (was Unity restarted?). Save it yourself only if it holds no other changes.";
            else if (state[0] == '1')
                saved = "scene not saved: it had unsaved changes before the bake.";
            else
                saved = EditorSceneManager.SaveScene(source.scene)
                    ? "scene saved (it had no unsaved changes before the bake)."
                    : "scene not saved: saving failed, see the Console.";
            return tool + ": removed " + Targets.Quote(cloneName) + "; " + Targets.Quote(source.name)
                + (wasActive ? " is active again; " : " left inactive, as it was before; ") + saved;
        }

        static string Key(GameObject source) => "AvatarAgentKit.Bake:" + source.scene.path + ":" + source.name;

        static void Remember(GameObject source) =>
            SessionState.SetString(Key(source), (source.scene.isDirty ? "1" : "0") + (source.activeSelf ? "1" : "0"));

        static string FirstLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var line = s.Split('\n')[0].Trim();
            return line.Length > 200 ? line.Substring(0, 200) + "…" : line;
        }

        static string MenuTarget()
        {
            var name = Targets.SelectedRootName();
            return Targets.IsBakedName(name) ? Targets.SourceName(name) : name;
        }

        [MenuItem("Tools/Avatar Agent Kit/Bake Avatar", false, 100)]
        static void BakeMenu() => Debug.Log(Run(MenuTarget()));

        [MenuItem("Tools/Avatar Agent Kit/Clean Up Bake", false, 101)]
        static void CleanupMenu() => Debug.Log(Cleanup(MenuTarget()));
    }
}
