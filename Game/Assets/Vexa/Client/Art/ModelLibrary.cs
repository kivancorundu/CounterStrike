using System.Collections.Generic;
using UnityEngine;
using Vexa.Core;

namespace Vexa.Client.Art
{
    /// <summary>
    /// Loads the generated models (Tools/Blender → Resources/Models). The mobile build picks the low-poly
    /// set. Every lookup returns null if a model is missing, and callers fall back to primitives.
    /// </summary>
    public static class ModelLibrary
    {
        static readonly Dictionary<string, GameObject> _prefabs = new Dictionary<string, GameObject>();
        static readonly Dictionary<string, AnimationClip[]> _clips = new Dictionary<string, AnimationClip[]>();

        static string Folder(string kind, bool lod) => lod && PlatformProfile.IsMobile ? $"Models/{kind}/Mobile/" : $"Models/{kind}/";

        static GameObject Prefab(string path)
        {
            if (_prefabs.TryGetValue(path, out var p)) return p;
            p = Resources.Load<GameObject>(path);
            if (p == null) Debug.LogWarning("[models] missing " + path);
            _prefabs[path] = p;
            return p;
        }

        static GameObject Spawn(string path)
        {
            var p = Prefab(path);
            if (p == null) return null;
            var go = Object.Instantiate(p);
            foreach (var c in go.GetComponentsInChildren<Collider>()) Object.Destroy(c);
            foreach (var r in go.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return go;
        }

        public static string WeaponKey(WeaponId id) => id == WeaponId.None || id == WeaponId.Grenade ? null : id.ToString().ToLowerInvariant();

        public static string GrenadeKey(GrenadeType g)
        {
            switch (g)
            {
                case GrenadeType.HE: return "he";
                case GrenadeType.Flash: return "flash";
                case GrenadeType.Smoke: return "smoke";
                case GrenadeType.Molotov: return "molotov";
                case GrenadeType.Incendiary: return "incendiary";
                case GrenadeType.Decoy: return "decoy";
                default: return null;
            }
        }

        /// <summary>Weapon / grenade / c4 / kit model by key (see Tools/Blender/vexa_weapons.py).</summary>
        public static GameObject Weapon(string key) => string.IsNullOrEmpty(key) ? null : Spawn(Folder("Weapons", true) + key);

        public static string Faction(Team t) => t == Team.CT ? "muhafiz" : "akinci";

        public static GameObject Character(Team t) => Spawn(Folder("Characters", true) + Faction(t));

        /// <summary>First-person forearms: children "RightArm" and "LeftArm" with the palm at each origin.</summary>
        public static GameObject Arms(Team t) => Spawn("Models/Arms/" + Faction(t));

        /// <summary>Animation clips of a character, by short name (idle, walk, run, crouch, crouch_walk, jump, death).</summary>
        public static AnimationClip Clip(Team t, string name)
        {
            string path = Folder("Characters", true) + Faction(t);
            if (!_clips.TryGetValue(path, out var list)) _clips[path] = list = Resources.LoadAll<AnimationClip>(path);
            foreach (var c in list)
            {
                if (c == null || c.name.StartsWith("__preview__")) continue;
                // Blender names takes "<armature>|<action>"
                var n = c.name;
                int bar = n.LastIndexOf('|');
                if ((bar >= 0 ? n.Substring(bar + 1) : n) == name) return c;
            }
            return null;
        }

        public static Transform FindDeep(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var f = FindDeep(root.GetChild(i), name);
                if (f != null) return f;
            }
            return null;
        }
    }
}
