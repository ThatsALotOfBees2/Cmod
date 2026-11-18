using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_6000_0_OR_NEWER
using PhysMat = UnityEngine.PhysicsMaterial;
#else
using PhysMat = UnityEngine.PhysicMaterial;
#endif

namespace Cmod
{
    /// <summary>
    /// Entities that tools create (balloons and lamps). They are spawned through
    /// EntityManager.SpawnProp with an unregistered PropDefinition, so they are tracked, cleaned up and
    /// removable like any prop, then get their class name and behaviour component here.
    /// </summary>
    public static class ToolEntities
    {
        public const string BalloonClass = "gmod_balloon";
        public const string LampClass = "gmod_lamp";

        /// <summary>
        /// A material on <paramref name="r"/> that belongs to this entity alone, safe to recolor without touching other
        /// objects and without leaking. The prop's main renderer already owns its material (EntityManager makes one per
        /// prop); a child part's shared material is cloned once and destroyed together with the entity.
        /// </summary>
        public static Material PaintMaterial(CmodEntity ent, Renderer r)
        {
            if (r == null) return null;
            var shared = r.sharedMaterial;
            if (shared == null) return null;
            if (ent == null || r == ent.Renderer) return shared;
            var owned = ent.GetComponent<OwnedMaterials>();
            if (owned == null) owned = ent.gameObject.AddComponent<OwnedMaterials>();
            if (owned.Set.Contains(shared)) return shared;
            var clone = new Material(shared) { name = shared.name + " (paint)" };
            r.sharedMaterial = clone;
            owned.Set.Add(clone);
            return clone;
        }

        // ---- Balloon ------------------------------------------------------------------------

        static readonly PropDefinition s_balloonDef = new PropDefinition
        {
            Id = "tool_balloon", DisplayName = "Balloon", Category = "Tools",
            Shape = PrimitiveType.Sphere, Scale = new Vector3(0.55f, 0.65f, 0.55f), Mass = 0.25f,
            Color = Color.red, Material = PhysicMaterialPreset.Rubber,
        };

        /// <summary>Spawns a floating balloon. force = upward lift in newtons (on top of cancelling its own weight).</summary>
        public static CmodEntity SpawnBalloon(Vector3 pos, Color color, float force)
        {
            var ent = EntityManager.SpawnProp(s_balloonDef, pos, Quaternion.identity, false);
            if (ent == null) return null;
            ent.ClassName = BalloonClass;
            if (ent.Renderer != null)
            {
                // EntityManager gives every prop its own material, so edit it in place (Renderer.material /
                // CmodEntity.Color would clone it and leak the copy).
                var m = PaintMaterial(ent, ent.Renderer);
                if (m != null)
                {
                    m.color = color;
                    // A bit of shine so it reads as a balloon.
                    if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.8f);
                }
            }
            if (ent.Body != null)
            {
                PhysicsCompat.SetDrag(ent.Body, 1.2f);
                PhysicsCompat.SetAngularDrag(ent.Body, 2f);
            }
            var b = ent.gameObject.AddComponent<BalloonEntity>();
            b.Entity = ent;
            b.Force = force;
            return ent;
        }

        // ---- Lamp -----------------------------------------------------------------------------

        static readonly PropDefinition s_lampDef = new PropDefinition
        {
            Id = "tool_lamp", DisplayName = "Lamp", Category = "Tools",
            Shape = PrimitiveType.Cube, Scale = new Vector3(0.22f, 0.22f, 0.34f), Mass = 3f,
            Color = new Color(0.25f, 0.25f, 0.27f), Material = PhysicMaterialPreset.Metal,
        };

        /// <summary>Spawns a lamp whose light shines along `rotation`'s forward axis.</summary>
        public static CmodEntity SpawnLamp(Vector3 pos, Quaternion rotation, Color lightColor, float brightness, float range, bool on = true)
        {
            var ent = EntityManager.SpawnProp(s_lampDef, pos, rotation, false);
            if (ent == null) return null;
            ent.ClassName = LampClass;
            var lamp = ent.gameObject.AddComponent<LampEntity>();
            lamp.Entity = ent;
            lamp.Build(lightColor, brightness, range, on);
            return ent;
        }

        // ---- Respawning (remover undo) ---------------------------------------------------------

        /// <summary>Everything needed to spawn a removed entity again.</summary>
        public sealed class Snapshot
        {
            public string ClassName, PropId;
            public Vector3 Position, Scale;
            public Quaternion Rotation;
            public bool HadBody, Frozen, UseGravity;
            public Vector3 Velocity, AngularVelocity;
            public Color[] RendererColors;
            public PhysMat[] ColliderMaterials;
            public readonly Dictionary<string, float> Values = new Dictionary<string, float>();
            public Color ExtraColor;
            /// <summary>The entity this snapshot was taken from (to map constraints on undo).</summary>
            public CmodEntity Original;
        }

        public static Snapshot Capture(CmodEntity e)
        {
            var s = new Snapshot
            {
                Original = e, ClassName = e.ClassName, PropId = e.PropId,
                Position = e.transform.position, Rotation = e.transform.rotation, Scale = e.transform.localScale,
                HadBody = e.Body != null,
            };
            if (e.Body != null)
            {
                s.Frozen = e.Body.isKinematic;
                s.UseGravity = e.Body.useGravity;
                if (!e.Body.isKinematic)
                {
                    s.Velocity = PhysicsCompat.GetVelocity(e.Body);
                    s.AngularVelocity = e.Body.angularVelocity;
                }
            }
            var rends = e.GetComponentsInChildren<MeshRenderer>(true);
            s.RendererColors = new Color[rends.Length];
            for (int i = 0; i < rends.Length; i++) s.RendererColors[i] = rends[i].sharedMaterial != null ? rends[i].sharedMaterial.color : Color.white;
            var cols = e.GetComponentsInChildren<Collider>(true);
            s.ColliderMaterials = new PhysMat[cols.Length];
            for (int i = 0; i < cols.Length; i++) s.ColliderMaterials[i] = cols[i].sharedMaterial;

            var balloon = e.GetComponent<BalloonEntity>();
            if (balloon != null) s.Values["force"] = balloon.Force;
            var lamp = e.GetComponent<LampEntity>();
            if (lamp != null)
            {
                s.Values["brightness"] = lamp.Brightness;
                s.Values["range"] = lamp.Range;
                s.Values["on"] = lamp.On ? 1f : 0f;
                s.ExtraColor = lamp.LightColor;
            }
            return s;
        }

        /// <summary>Spawns the entity again from a snapshot. Returns null when it can't (prop type unregistered, spawn blocked).</summary>
        public static CmodEntity Restore(Snapshot s)
        {
            if (s == null) return null;
            CmodEntity e = null;
            if (s.ClassName == BalloonClass)
            {
                var col = s.RendererColors != null && s.RendererColors.Length > 0 ? s.RendererColors[0] : Color.red;
                e = SpawnBalloon(s.Position, col, s.Values.TryGetValue("force", out var f) ? f : 300f);
            }
            else if (s.ClassName == LampClass)
            {
                e = SpawnLamp(s.Position, s.Rotation, s.ExtraColor,
                    s.Values.TryGetValue("brightness", out var br) ? br : 3f,
                    s.Values.TryGetValue("range", out var rg) ? rg : 20f,
                    !s.Values.TryGetValue("on", out var on) || on > 0.5f);
            }
            else
            {
                var def = PropRegistry.Get(s.PropId);
                if (def == null) { CmodLog.Warn($"Can't restore '{s.PropId}': prop type not registered"); return null; }
                e = EntityManager.SpawnProp(def, s.Position, s.Rotation, false);
            }
            if (e == null) return null;

            e.transform.SetPositionAndRotation(s.Position, s.Rotation);
            e.transform.localScale = s.Scale;

            var rends = e.GetComponentsInChildren<MeshRenderer>(true);
            if (s.RendererColors != null && rends.Length == s.RendererColors.Length)
                for (int i = 0; i < rends.Length; i++)
                {
                    // Effect parts (lamp lens, glow sprites) manage their own materials.
                    if (rends[i].gameObject != e.gameObject && (s.ClassName == LampClass || rends[i].GetComponent<GlowSprite>() != null)) continue;
                    var pm = PaintMaterial(e, rends[i]);
                    if (pm != null) pm.color = s.RendererColors[i];
                }
            var cols = e.GetComponentsInChildren<Collider>(true);
            if (s.ColliderMaterials != null && cols.Length == s.ColliderMaterials.Length)
                for (int i = 0; i < cols.Length; i++) cols[i].sharedMaterial = s.ColliderMaterials[i];

            if (e.Body != null && s.HadBody)
            {
                e.Body.useGravity = s.UseGravity;
                e.Frozen = s.Frozen;
                if (!s.Frozen)
                {
                    PhysicsCompat.SetVelocity(e.Body, s.Velocity);
                    e.Body.angularVelocity = s.AngularVelocity;
                }
            }
            return e;
        }
    }

    /// <summary>Lifts its rigidbody: cancels its own weight and pushes up with Force newtons.</summary>
    /// <summary>Materials a tool cloned for an entity's child parts; destroyed with the entity (Unity doesn't free them).</summary>
    public sealed class OwnedMaterials : MonoBehaviour
    {
        internal readonly HashSet<Material> Set = new HashSet<Material>();

        void OnDestroy()
        {
            foreach (var m in Set) if (m != null) Destroy(m);
            Set.Clear();
        }
    }

    public sealed class BalloonEntity : MonoBehaviour
    {
        public CmodEntity Entity;
        public float Force = 300f;
        /// <summary>Upward speed (m/s) at which the lift has faded to zero.</summary>
        public const float MaxRiseSpeed = 6f;
        bool _popped;

        void FixedUpdate()
        {
            var rb = Entity != null ? Entity.Body : null;
            if (rb == null || rb.isKinematic) return;
            var up = Physics.gravity.sqrMagnitude > 0.0001f ? -Physics.gravity.normalized : Vector3.up;
            if (rb.useGravity) rb.AddForce(-Physics.gravity * rb.mass, ForceMode.Force);
            // Full lift while held down by a load; fade it out as the balloon itself rises faster, so a free
            // balloon drifts up at a believable speed instead of rocketing away (and ropes don't get yanked).
            float rise = Vector3.Dot(PhysicsCompat.GetVelocity(rb), up);
            float k = Mathf.Clamp01(1f - rise / MaxRiseSpeed);
            rb.AddForce(up * (Force * k), ForceMode.Force);
        }

        /// <summary>Pops the balloon: a sound, a little burst, and the entity is removed.</summary>
        public void Pop()
        {
            if (_popped) return;
            _popped = true;
            var pos = transform.position;
            WeaponFx.PlayAt("balloon_pop", pos, 1f, UnityEngine.Random.Range(0.9f, 1.15f));
            var c = Entity != null ? Entity.Color : Color.white;
            c.a = 0.9f;
            WeaponFx.FlashSprite(pos, c, 1.1f, 0.15f);
            if (Entity != null) EntityManager.Remove(Entity);
            else Destroy(gameObject);
        }
    }

    /// <summary>A spotlight on a small housing with a glowing lens. R with the lamp tool toggles it.</summary>
    public sealed class LampEntity : MonoBehaviour
    {
        public CmodEntity Entity;
        public Light Light;
        public Color LightColor = Color.white;
        public float Brightness = 3f;
        public float Range = 20f;
        public bool On = true;

        Renderer _lens;
        Material _lensMat;
        GlowSprite _glow;

        internal void Build(Color color, float brightness, float range, bool on)
        {
            LightColor = color;
            Brightness = brightness;
            Range = range;

            // Children live under a scaled root, so local scales are in the root's normalized space.
            var lens = WeaponFx.MakePart("Lens", transform, PrimitiveType.Cylinder, new Vector3(0f, 0f, 0.5f),
                new Vector3(0.8f, 0.04f, 0.8f), WeaponFx.LitShared(Color.white), Quaternion.Euler(90f, 0f, 0f));
            lens.layer = gameObject.layer;
            _lens = lens.GetComponent<Renderer>();
            _lensMat = CmodMaterials.Lit(Color.white);
            _lens.sharedMaterial = _lensMat;

            var lightGo = new GameObject("Light");
            lightGo.transform.SetParent(transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 0f, 0.56f);
            Light = lightGo.AddComponent<Light>();
            Light.type = LightType.Spot;
            Light.spotAngle = 70f;
            Light.innerSpotAngle = 35f;
            Light.shadows = LightShadows.Hard;

            // The root is non-uniformly scaled; a camera-facing quad under it would shear. Put the glow under a
            // holder whose scale cancels the root's, so the glow's parent space is uniform.
            var holder = new GameObject("GlowHolder").transform;
            holder.SetParent(transform, false);
            holder.localPosition = new Vector3(0f, 0f, 0.56f);
            var rs = transform.localScale;
            holder.localScale = new Vector3(1f / Mathf.Max(0.0001f, rs.x), 1f / Mathf.Max(0.0001f, rs.y), 1f / Mathf.Max(0.0001f, rs.z));
            _glow = WeaponFx.MakeGlowSprite("Glow", holder, color, 0.35f);
            _glow.transform.localPosition = Vector3.zero;

            SetOn(on);
        }

        public void Apply()
        {
            if (Light == null) return;
            Light.color = LightColor;
            Light.intensity = Brightness;
            Light.range = Range;
            Light.enabled = On;
            if (_lensMat != null)
            {
                _lensMat.color = On ? Color.Lerp(LightColor, Color.white, 0.5f) : new Color(0.35f, 0.35f, 0.35f);
                if (On) CmodMaterials.MakeEmissive(_lensMat, LightColor * 1.5f);
                else _lensMat.DisableKeyword("_EMISSION");
            }
            if (_glow != null)
            {
                _glow.gameObject.SetActive(On);
                var gm = _glow.GetComponent<Renderer>().sharedMaterial;
                var gc = LightColor; gc.a = 0.7f;
                if (gm != null) gm.color = gc;
            }
        }

        public void SetOn(bool on)
        {
            On = on;
            Apply();
        }

        public void Toggle()
        {
            SetOn(!On);
            WeaponFx.PlayAt("lamp_toggle", transform.position, 0.8f, On ? 1.1f : 0.9f);
        }

        void OnDestroy()
        {
            if (_lensMat != null) Destroy(_lensMat);
            if (_glow != null)
            {
                var gm = _glow.GetComponent<Renderer>().sharedMaterial;
                if (gm != null) Destroy(gm);
            }
        }
    }
}
