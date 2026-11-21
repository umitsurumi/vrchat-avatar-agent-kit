// Poses an avatar in play mode for motion tests: PhysBones, constraints and garments only move there.
// Every call starts again from the rest pose (saved on the first call of the play session), puts the arms down
// and applies the named poses in order. The Animator is switched off, or it would reset the bones.
// Wait 1 to 3 s for the PhysBones to settle, then render in a separate call (RenderAvatar keeps the pose).
//
// From code (execute_code, in play mode):
//   return AvatarAgentKit.Editor.Pose.Run("MyAvatar", "lean-back");
//   return AvatarAgentKit.Editor.Pose.Run("MyAvatar", "squat");
//   return AvatarAgentKit.Editor.Pose.Reset("MyAvatar");      // rest pose, Animator back on
// Poses (comma-separated, applied in order):
//   torso, for hair and braids: lean-back, lean-forward, lean-left, lean-right (15 degrees on Spine and on Chest)
//   legs, for skirts and garments: spread (legs 40 degrees apart), kick-back (right leg 45 back),
//     side-kick (right leg 70 out), squat (thighs 110 forward, knees 130), sit (thighs 90 forward, knees 90)
//   stand: the rest pose with the arms down (ReportClipping's reference pose).
//   rest: the rest pose only, arms not moved.
// All but sit caught real clipping. Static poses still miss fast motion: ask the user to check in game
// (an MMD dance world shows a lot).
//
// Verified on: Unity 2022.3.22f1, VRChat SDK 3.10.5; a MANUKA avatar in play mode.

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AvatarAgentKit.Editor
{
    [InitializeOnLoad]
    public static class Pose
    {
        const string Tool = "Pose";

        // A new play session starts with no saved rest pose (also when domain reload on play is off).
        static Pose() => EditorApplication.playModeStateChanged += _ => { Rest.Clear(); Current.Clear(); };

        static readonly string[] Names =
            { "rest", "stand", "lean-back", "lean-forward", "lean-left", "lean-right", "spread", "kick-back", "side-kick", "squat", "sit" };

        // Rest pose per avatar (instance ID), for the current play session.
        static readonly Dictionary<int, Dictionary<HumanBodyBones, Quaternion>> Rest = new Dictionary<int, Dictionary<HumanBodyBones, Quaternion>>();

        // The poses last applied per avatar (instance ID), for the current play session; ReportClipping labels with it.
        static readonly Dictionary<int, string> Current = new Dictionary<int, string>();

        // "rest", "stand", a pose list like "squat", or null when the avatar wasn't posed in this play session.
        internal static string CurrentPose(GameObject source) => Current.TryGetValue(source.GetInstanceID(), out var p) ? p : null;

        public static string Run(string avatar = null, string pose = "rest")
        {
            var err = Prepare(avatar, out var source, out var anim);
            if (err != null) return Tool + ": refused: " + err;
            var poses = (pose ?? "").Split(',').Select(p => p.Trim().ToLowerInvariant()).Where(p => p.Length > 0).ToList();
            var unknown = poses.Where(p => !Names.Contains(p)).ToList();
            if (poses.Count == 0 || unknown.Count > 0)
                return Tool + ": refused: unknown pose " + string.Join(", ", unknown.Select(Targets.Quote)) + ". Poses: " + string.Join(", ", Names);

            var savedNow = Save(source, anim);
            anim.enabled = false;
            Restore(source, anim);

            var root = source.transform;
            Vector3 right = root.right, forward = root.forward;
            Func<HumanBodyBones, Transform> bone = anim.GetBoneTransform;
            var armsDown = !(poses.Count == 1 && poses[0] == "rest") && Posing.ArmsDown(bone, root);
            foreach (var p in poses)
            {
                switch (p)
                {
                    case "lean-back": Torso(bone, -15f, right); break;
                    case "lean-forward": Torso(bone, 15f, right); break;
                    case "lean-left": Torso(bone, 15f, forward); break;
                    case "lean-right": Torso(bone, -15f, forward); break;
                    case "spread":
                        Posing.Rotate(bone(HumanBodyBones.LeftUpperLeg), -20f, forward);
                        Posing.Rotate(bone(HumanBodyBones.RightUpperLeg), 20f, forward);
                        break;
                    case "kick-back": Posing.Rotate(bone(HumanBodyBones.RightUpperLeg), 45f, right); break;
                    case "side-kick": Posing.Rotate(bone(HumanBodyBones.RightUpperLeg), 70f, forward); break;
                    case "squat": Legs(bone, 110f, 130f, right); break;
                    case "sit": Legs(bone, 90f, 90f, right); break;
                }
            }

            Current[source.GetInstanceID()] = string.Join(",", poses);
            var legs = poses.Any(p => p == "spread" || p == "kick-back" || p == "side-kick" || p == "squat" || p == "sit");
            var focus = legs ? "focus: \"Hips\", size: 0.9f" : "focus: \"Head\", size: 0.9f";
            return Tool + ": " + string.Join(", ", poses) + " on " + Targets.Quote(source.name)
                + (armsDown ? ", arms down" : "") + (savedNow ? "; rest pose saved" : "") + "; Animator off.\n"
                + "Wait 1 to 3 s for the PhysBones to settle, then render in a separate call, e.g. "
                + Targets.Ns + "RenderAvatar.Run(" + Targets.Quote(source.name) + ", \"front,left,back\", " + focus + ")";
        }

        public static string Reset(string avatar = null)
        {
            const string tool = "Pose.Reset";
            var err = Prepare(avatar, out var source, out var anim);
            if (err != null) return tool + ": refused: " + err;
            if (!Rest.ContainsKey(source.GetInstanceID()))
                return tool + ": nothing to reset: " + Targets.Quote(source.name) + " was not posed in this play session.";
            Restore(source, anim);
            Rest.Remove(source.GetInstanceID());
            Current.Remove(source.GetInstanceID());
            anim.enabled = true;
            return tool + ": " + Targets.Quote(source.name) + " is back in its rest pose; Animator on.";
        }

        static string Prepare(string avatar, out GameObject source, out Animator anim)
        {
            source = null;
            anim = null;
            if (!EditorApplication.isPlaying)
                return "not in play mode. PhysBones and constraints only move in play mode: enter it, then pose.";
            if (Targets.IsBakedName(avatar)) avatar = Targets.SourceName(avatar);
            var err = Targets.FindSource(avatar, out source);
            if (err != null) return err;
            if (!source.activeInHierarchy) return Targets.Quote(source.name) + " is inactive; activate it, or leave and re-enter play mode.";
            anim = source.GetComponent<Animator>();
            if (anim == null || !anim.isHuman) return Targets.Quote(source.name) + " has no humanoid Animator.";
            return null;
        }

        // Saves the rest pose once per play session. True when it was saved now.
        static bool Save(GameObject source, Animator anim)
        {
            var id = source.GetInstanceID();
            if (Rest.ContainsKey(id)) return false;
            var pose = new Dictionary<HumanBodyBones, Quaternion>();
            for (var b = HumanBodyBones.Hips; b < HumanBodyBones.LastBone; b++)
            {
                var t = anim.GetBoneTransform(b);
                if (t != null) pose[b] = t.localRotation;
            }
            Rest[id] = pose;
            return true;
        }

        static void Restore(GameObject source, Animator anim)
        {
            foreach (var kv in Rest[source.GetInstanceID()])
            {
                var t = anim.GetBoneTransform(kv.Key);
                if (t != null) t.localRotation = kv.Value;
            }
        }

        static void Torso(Func<HumanBodyBones, Transform> bone, float degrees, Vector3 axis)
        {
            Posing.Rotate(bone(HumanBodyBones.Spine), degrees, axis);
            Posing.Rotate(bone(HumanBodyBones.Chest), degrees, axis);
        }

        static void Legs(Func<HumanBodyBones, Transform> bone, float thigh, float knee, Vector3 right)
        {
            foreach (var side in new[] { (HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg), (HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg) })
            {
                Posing.Rotate(bone(side.Item1), -thigh, right);
                Posing.Rotate(bone(side.Item2), knee, right);
            }
        }
    }
}
