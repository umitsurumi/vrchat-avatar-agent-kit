// Shared by the avatar tools: finding avatars and their baked clones in the open scenes.
// A baked clone is a root object named <avatar>_bakecheck, made by Bake.Run.

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.Avatars.Components;

namespace AvatarAgentKit.Editor
{
    static class Targets
    {
        public const string BakeSuffix = "_bakecheck";
        public const string Ns = "AvatarAgentKit.Editor.";

        // Root objects of every loaded scene, inactive ones included. Preview scenes are not among them.
        public static IEnumerable<GameObject> Roots()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects()) yield return root;
            }
        }

        public static bool IsAvatar(GameObject go) => go.GetComponent<VRCAvatarDescriptor>() != null;
        public static bool IsBakedName(string name) => name != null && name.EndsWith(BakeSuffix);
        public static string SourceName(string bakedName) => bakedName.Substring(0, bakedName.Length - BakeSuffix.Length);

        public static List<GameObject> RootsNamed(string name) => Roots().Where(g => g.name == name).ToList();

        // The scene avatar called `name` (not a baked clone), or the only one when `name` is empty.
        // Returns null when found, otherwise why not.
        public static string FindSource(string name, out GameObject avatar)
        {
            avatar = null;
            var sources = Roots().Where(g => IsAvatar(g) && !IsBakedName(g.name)).ToList();
            if (string.IsNullOrEmpty(name))
            {
                if (sources.Count == 1) { avatar = sources[0]; return null; }
                return sources.Count == 0
                    ? "no avatar (a root object with a VRC Avatar Descriptor) in the open scenes"
                    : "several avatars in the open scenes, name one: " + Names(sources);
            }
            if (IsBakedName(name))
                return Quote(name) + " is a baked clone; pass the avatar it was baked from, " + Quote(SourceName(name));
            var match = sources.Where(g => g.name == name).ToList();
            if (match.Count == 1) { avatar = match[0]; return null; }
            if (match.Count > 1) return "several root objects are called " + Quote(name) + "; rename all but one";
            return "no avatar called " + Quote(name) + " at the root of the open scenes; avatars: " + Names(sources);
        }

        // A baked clone: `name` itself, or the clone of avatar `name`, or the only clone when `name` is empty.
        // Returns null when found, otherwise why not, with the call that fixes it.
        public static string FindBaked(string name, out GameObject clone)
        {
            clone = null;
            var clones = Roots().Where(g => IsBakedName(g.name)).ToList();
            if (string.IsNullOrEmpty(name))
            {
                if (clones.Count == 1) { clone = clones[0]; return null; }
                if (clones.Count > 1) return "several baked clones, name one: " + Names(clones);
                var err = FindSource(null, out var only);
                return err != null ? "nothing baked, and " + err : NotBaked(only.name);
            }
            var wanted = IsBakedName(name) ? name : name + BakeSuffix;
            var match = clones.Where(g => g.name == wanted).ToList();
            if (match.Count == 1) { clone = match[0]; return null; }
            if (match.Count > 1)
                return "several objects are called " + Quote(wanted) + "; run " + Ns + "Bake.Cleanup(" + Quote(SourceName(wanted)) + ") and bake again";
            return NotBaked(IsBakedName(name) ? SourceName(name) : name);
        }

        static string NotBaked(string avatar) =>
            Quote(avatar) + " is not baked. The scene avatar is the avatar before NDMF, not what VRChat receives. Bake it first: "
            + Ns + "Bake.Run(" + Quote(avatar) + ")";

        // Name of the avatar (or baked clone) selected in the Hierarchy, or anything under it; else null.
        public static string SelectedRootName()
        {
            var go = UnityEditor.Selection.activeGameObject;
            if (go == null || !go.scene.IsValid()) return null;
            var root = go.transform.root.gameObject;
            return IsAvatar(root) ? root.name : null;
        }

        public static string Quote(string s) => "\"" + s + "\"";

        public static string Names(IEnumerable<GameObject> objects)
        {
            var names = objects.Select(g => Quote(g.name)).ToList();
            return names.Count == 0 ? "none" : string.Join(", ", names);
        }
    }
}
