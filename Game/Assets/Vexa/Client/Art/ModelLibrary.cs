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
        static readonly Dictionary<string, Material> _materials = new Dictionary<string, Material>();

        /// <summary>
        /// PBR material from the baked textures "<path>_albedo / _mask / _normal" (Tools/Blender/vexa_textures.py).
        /// Mask layout: R = metallic, A = smoothness. Returns null if the textures aren't there.
        /// </summary>
        public static Material MaterialFor(string texturePath)
        {
            // normal maps are skipped on the low-detail set (phones, DÜŞÜK)
            bool useNormal = !GraphicsQuality.LowDetail;
            string key = useNormal ? texturePath : texturePath + "|flat";
            if (_materials.TryGetValue(key, out var m)) return m;
            var albedo = Resources.Load<Texture2D>(texturePath + "_albedo");
            if (albedo == null) { _materials[key] = null; return null; }
            var mask = Resources.Load<Texture2D>(texturePath + "_mask");
            var normal = useNormal ? Resources.Load<Texture2D>(texturePath + "_normal") : null;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            m = new Material(shader) { name = texturePath };
            m.SetTexture("_BaseMap", albedo);
            m.SetTexture("_MainTex", albedo);
            m.SetColor("_BaseColor", Color.white);
            m.color = Color.white;
            if (mask != null)
            {
                m.SetTexture("_MetallicGlossMap", mask);
                m.SetFloat("_Metallic", 1f);
                m.SetFloat("_Smoothness", 1f);
                m.SetFloat("_GlossMapScale", 1f);
                m.EnableKeyword("_METALLICSPECGLOSSMAP"); // URP Lit
                m.EnableKeyword("_METALLICGLOSSMAP");     // built-in Standard
            }
            if (normal != null)
            {
                m.SetTexture("_BumpMap", normal);
                m.SetFloat("_BumpScale", 1f);
                m.EnableKeyword("_NORMALMAP");
            }
            _materials[key] = m;
            return m;
        }

        static void Texture(GameObject go, string texturePath)
        {
            var m = MaterialFor(texturePath);
            if (m == null) return;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = m;
                r.sharedMaterials = mats;
            }
        }

        /// <summary>The low-poly set with small textures on phones and on the DÜŞÜK preset.</summary>
        static string Folder(string kind, bool lod) => lod && GraphicsQuality.LowDetail ? $"Models/{kind}/Mobile/" : $"Models/{kind}/";

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
            var shadows = GraphicsQuality.Current == GraphicsQuality.Level.Low ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
            foreach (var r in go.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = shadows;
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
        public static GameObject Weapon(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            var go = Spawn(Folder("Weapons", true) + key);
            if (go != null) Texture(go, Folder("Weapons", true) + "Textures/" + key);
            return go;
        }

        /// <summary>Physics prop model (crate, smallcrate, barrel, redbarrel; Tools/Blender/vexa_props.py), origin at its center.</summary>
        public static GameObject Prop(PropKind k)
        {
            string key = Vexa.Core.Props.NameOf(k);
            var go = Spawn(Folder("Props", true) + key);
            if (go != null) Texture(go, Folder("Props", true) + "Textures/" + key);
            return go;
        }

        public static string Faction(Team t) => t == Team.CT ? "muhafiz" : "akinci";

        public static GameObject Character(Team t)
        {
            var go = Spawn(Folder("Characters", true) + Faction(t));
            if (go != null) Texture(go, Folder("Characters", true) + "Textures/" + Faction(t));
            return go;
        }

        /// <summary>
        /// First-person arms for a hold (rifle, pistol, knife, grenade, c4; Tools/Blender/vexa_fparms.py): children
        /// "RightArm" (origin = the weapon's grip origin) and, for two-handed holds, "LeftArm" (origin = the weapon's
        /// "Support" point). Both arms share one texture set per faction.
        /// </summary>
        public static GameObject Arms(Team t, string hold)
        {
            var go = Spawn($"Models/Arms/{Faction(t)}_{hold}");
            if (go == null) return null;
            Texture(go, $"Models/Arms/Textures/{Faction(t)}");
            return go;
        }

        /// <summary>
        /// Animation clip of a character by short name. The clips live in their own file
        /// (Characters/Anims/&lt;faction&gt;, Tools/Blender/vexa_anim.py) shared by both detail levels; older clips inside
        /// the character model are the fallback.
        /// </summary>
        public static AnimationClip Clip(Team t, string name)
        {
            return FindClip($"Models/Characters/Anims/{Faction(t)}", name) ?? FindClip(Folder("Characters", true) + Faction(t), name);
        }

        static AnimationClip FindClip(string path, string name)
        {
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
