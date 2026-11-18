// Shared by RenderAvatar and Pose: bone rotations in the avatar's own frame, whatever the rest pose (T or A)
// and the bone rolls. Rotations are around world axes taken from the avatar root.

using System;
using UnityEngine;

namespace AvatarAgentKit.Editor
{
    static class Posing
    {
        const float ArmAngleFromBody = 15f;

        // Points each upper arm down, ArmAngleFromBody away from the body. False when no arm bones were found.
        public static bool ArmsDown(Func<HumanBodyBones, Transform> bone, Transform root)
        {
            var any = false;
            foreach (var pair in new[] { (HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm), (HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm) })
            {
                var upper = bone(pair.Item1);
                var lower = bone(pair.Item2);
                if (upper == null || lower == null) continue;
                var along = (lower.position - upper.position).normalized;
                var side = Vector3.Dot(along, root.right) >= 0 ? root.right : -root.right;
                var rad = ArmAngleFromBody * Mathf.Deg2Rad;
                var want = (-root.up * Mathf.Cos(rad) + side * Mathf.Sin(rad)).normalized;
                upper.rotation = Quaternion.FromToRotation(along, want) * upper.rotation;
                any = true;
            }
            return any;
        }

        // Rotates a bone by `degrees` around a world axis.
        public static void Rotate(Transform bone, float degrees, Vector3 axis)
        {
            if (bone != null) bone.rotation = Quaternion.AngleAxis(degrees, axis) * bone.rotation;
        }
    }
}
