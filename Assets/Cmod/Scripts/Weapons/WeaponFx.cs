using System;
using System.Collections.Generic;
using UnityEngine;

namespace Cmod
{
    /// <summary>
    /// Shared visual and audio effects for the built-in weapons and tools: procedural meshes and
    /// materials, synthesized sounds, tracers, muzzle-flash lights and pooled bullet holes.
    /// Everything is created from code (no assets). The effects live on one hidden runner object
    /// ("CmodWeaponFx") that animates them, so weapons (plain C# objects) never need coroutines.
    /// </summary>
    public static class WeaponFx
    {
        // =====================================================================================
        // Runner
        // =====================================================================================

        static WeaponFxRunner s_runner;

        /// <summary>The hidden MonoBehaviour that animates every effect. Created on first use.</summary>
        internal static WeaponFxRunner Runner
        {
            get
            {
                if (s_runner == null)
                {
                    var go = new GameObject("CmodWeaponFx");
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    s_runner = go.AddComponent<WeaponFxRunner>();
                }
                return s_runner;
            }
        }

        /// <summary>Layer used for effect and view-model objects so traces never see them (built-in "Ignore Raycast").</summary>
        public const int FxLayer = 2;

        // =====================================================================================
        // Meshes and materials
        // =====================================================================================

        static readonly Dictionary<PrimitiveType, Mesh> s_meshes = new Dictionary<PrimitiveType, Mesh>();
        static readonly Dictionary<Color, Material> s_litCache = new Dictionary<Color, Material>();
        static readonly Dictionary<Color, Material> s_glowCache = new Dictionary<Color, Material>();
        static Texture2D s_glowTex;
        static Texture2D s_holeTex;
        static Material s_holeMat;

        /// <summary>The built-in mesh of a primitive, without creating a collider-carrying GameObject every time.</summary>
        public static Mesh PrimitiveMesh(PrimitiveType type)
        {
            if (s_meshes.TryGetValue(type, out var m) && m != null) return m;
            var tmp = GameObject.CreatePrimitive(type);
            m = tmp.GetComponent<MeshFilter>().sharedMesh;
            // Destroy right away so the temporary object never renders or collides (the built-in mesh survives).
            UnityEngine.Object.DestroyImmediate(tmp);
            s_meshes[type] = m;
            return m;
        }

        /// <summary>A cached lit material for the color (shared, don't modify).</summary>
        public static Material LitShared(Color c)
        {
            if (s_litCache.TryGetValue(c, out var m) && m != null) return m;
            m = CmodMaterials.Lit(c);
            s_litCache[c] = m;
            return m;
        }

        /// <summary>A cached lit material that glows with its own color (view-model lights, screens).</summary>
        public static Material GlowShared(Color c)
        {
            if (s_glowCache.TryGetValue(c, out var m) && m != null) return m;
            m = CmodMaterials.Lit(c);
            CmodMaterials.MakeEmissive(m, c * 1.6f);
            s_glowCache[c] = m;
            return m;
        }

        /// <summary>Soft white radial gradient used for glow sprites and muzzle flashes.</summary>
        public static Texture2D GlowTexture
        {
            get
            {
                if (s_glowTex != null) return s_glowTex;
                const int n = 64;
                s_glowTex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "cmod_glow", wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - d);
                    a = a * a * (0.6f + 0.4f * Mathf.Clamp01(1f - d * 3f));   // bright core, soft falloff
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
                s_glowTex.SetPixels32(px);
                s_glowTex.Apply(false, true);
                return s_glowTex;
            }
        }

        /// <summary>A new unlit, alpha-blended material with the glow texture (each sprite gets its own so it can fade).</summary>
        public static Material NewGlowSpriteMaterial(Color c)
        {
            var m = CmodMaterials.Unlit(c);
            m.mainTexture = GlowTexture;
            return m;
        }

        /// <summary>Creates a child object with a mesh renderer (no collider), on the FX layer, without shadows.</summary>
        public static GameObject MakePart(string name, Transform parent, PrimitiveType shape, Vector3 localPos,
            Vector3 localScale, Material mat, Quaternion? localRot = null)
        {
            var go = new GameObject(name);
            go.layer = FxLayer;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot ?? Quaternion.identity;
            go.transform.localScale = localScale;
            go.AddComponent<MeshFilter>().sharedMesh = PrimitiveMesh(shape);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        /// <summary>Creates a camera-facing glow sprite (quad) as a child of parent, or at the root when parent is null.</summary>
        public static GlowSprite MakeGlowSprite(string name, Transform parent, Color c, float size)
        {
            var go = new GameObject(name);
            go.layer = FxLayer;
            if (parent != null) go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = PrimitiveMesh(PrimitiveType.Quad);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = NewGlowSpriteMaterial(c);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            var s = go.AddComponent<GlowSprite>();
            s.Size = size;
            return s;
        }

        /// <summary>Creates a LineRenderer with an unlit material. Positions are in world space.</summary>
        public static LineRenderer MakeLine(string name, Transform parent, Color c, float width, int points)
        {
            var go = new GameObject(name);
            go.layer = FxLayer;
            if (parent != null) go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.sharedMaterial = CmodMaterials.Unlit(Color.white);
            lr.startColor = lr.endColor = c;
            lr.startWidth = lr.endWidth = width;
            lr.positionCount = points;
            lr.numCapVertices = 2;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            return lr;
        }

        /// <summary>Destroys the GameObject and its private (non-shared) materials.</summary>
        public static void DestroyWithMaterials(GameObject go, bool includeShared = false)
        {
            if (go == null) return;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var m = r.sharedMaterial;
                if (m == null) continue;
                if (!includeShared && (s_litCache.ContainsValue(m) || s_glowCache.ContainsValue(m))) continue;
                UnityEngine.Object.Destroy(m);
            }
            UnityEngine.Object.Destroy(go);
        }

        // =====================================================================================
        // Sounds (synthesized at first use)
        // =====================================================================================

        const int SampleRate = 44100;
        static readonly Dictionary<string, AudioClip> s_clips = new Dictionary<string, AudioClip>();

        /// <summary>Names: toolgun_zap, tool_deny, pistol_shot, pistol_dry, pistol_reload, wrench_swing, wrench_hit,
        /// wrench_hit_world, physgun_freeze, physgun_unfreeze, physgun_grab, balloon_pop, weld, rope, remove, lamp_toggle.</summary>
        public static AudioClip Clip(string name)
        {
            if (s_clips.TryGetValue(name, out var c) && c != null) return c;
            c = Synthesize(name);
            if (c != null) s_clips[name] = c;
            return c;
        }

        /// <summary>Plays a sound non-positionally (the local player's own weapon).</summary>
        public static void Play(string name, float volume = 1f, float pitch = 1f)
        {
            var clip = Clip(name);
            if (clip != null) Runner.Play2D(clip, volume, pitch);
        }

        /// <summary>Plays a 3D sound at a world position.</summary>
        public static void PlayAt(string name, Vector3 pos, float volume = 1f, float pitch = 1f)
        {
            var clip = Clip(name);
            if (clip != null) Runner.Play3D(clip, pos, volume, pitch);
        }

        static AudioClip Synthesize(string name)
        {
            var rng = new System.Random(name.GetHashCode());
            float Noise() => (float)(rng.NextDouble() * 2.0 - 1.0);
            float Sin(float t, float f) => Mathf.Sin(2f * Mathf.PI * f * t);

            switch (name)
            {
                case "toolgun_zap":
                    // Bright electric click with a short descending zap.
                    return Make(name, 0.16f, t =>
                    {
                        float click = Noise() * Mathf.Exp(-t * 400f);
                        float f = Mathf.Lerp(2600f, 900f, t / 0.16f);
                        float zap = (Sin(t, f) > 0 ? 1f : -1f) * 0.25f * Mathf.Exp(-t * 28f);
                        float buzz = Noise() * 0.15f * Mathf.Exp(-t * 40f);
                        return click * 0.8f + zap + buzz;
                    });
                case "tool_deny":
                    return Make(name, 0.18f, t =>
                    {
                        float env = t < 0.15f ? 1f : Mathf.Clamp01((0.18f - t) / 0.03f);
                        return ((Sin(t, 140f) > 0 ? 1f : -1f) * 0.18f + Sin(t, 210f) * 0.12f) * env;
                    });
                case "pistol_shot":
                    {
                        float lp = 0f;
                        return Make(name, 0.45f, t =>
                        {
                            float n = Noise();
                            lp += (n - lp) * 0.25f;                    // low-passed noise body
                            float crack = n * Mathf.Exp(-t * 90f);
                            float body = lp * 2.2f * Mathf.Exp(-t * 11f);
                            float thump = Sin(t, Mathf.Lerp(150f, 45f, Mathf.Clamp01(t * 8f))) * Mathf.Exp(-t * 18f);
                            return Mathf.Clamp(crack * 0.7f + body + thump * 0.9f, -1f, 1f);
                        });
                    }
                case "pistol_dry":
                    return Make(name, 0.05f, t => (Noise() * 0.5f + Sin(t, 3200f) * 0.4f) * Mathf.Exp(-t * 160f));
                case "pistol_reload":
                    // Magazine out ... magazine in ... slide.
                    return Make(name, 1.2f, t =>
                    {
                        float s = 0f;
                        s += Clack(t, 0.10f, 2400f, Noise);
                        s += Clack(t, 0.62f, 1800f, Noise) * 1.2f;
                        s += Clack(t, 0.95f, 2800f, Noise) * 0.9f;
                        s += Clack(t, 1.05f, 2100f, Noise) * 1.1f;
                        return s;
                    });
                case "wrench_swing":
                    {
                        float lp = 0f;
                        return Make(name, 0.28f, t =>
                        {
                            lp += (Noise() - lp) * Mathf.Lerp(0.05f, 0.3f, Mathf.Sin(t / 0.28f * Mathf.PI));
                            return lp * 1.4f * Mathf.Sin(t / 0.28f * Mathf.PI);
                        });
                    }
                case "wrench_hit":
                    // Metallic clank: inharmonic partials.
                    return Make(name, 0.5f, t =>
                    {
                        float e = Mathf.Exp(-t * 9f);
                        return (Sin(t, 523f) * 0.35f + Sin(t, 1307f) * 0.25f + Sin(t, 2213f) * 0.15f) * e
                               + Noise() * 0.6f * Mathf.Exp(-t * 120f);
                    });
                case "wrench_hit_world":
                    {
                        float lp = 0f;
                        return Make(name, 0.25f, t =>
                        {
                            lp += (Noise() - lp) * 0.2f;
                            return lp * 2f * Mathf.Exp(-t * 25f) + Sin(t, 180f) * 0.4f * Mathf.Exp(-t * 30f);
                        });
                    }
                case "physgun_grab":
                    return Make(name, 0.2f, t => Sin(t, Mathf.Lerp(300f, 900f, t / 0.2f)) * 0.25f * Mathf.Exp(-t * 10f));
                case "physgun_freeze":
                    return Make(name, 0.35f, t =>
                    {
                        float e = Mathf.Exp(-t * 9f);
                        return (Sin(t, 1760f) * 0.25f + Sin(t, 2637f) * 0.2f + Noise() * 0.1f * Mathf.Exp(-t * 60f)) * e;
                    });
                case "physgun_unfreeze":
                    return Make(name, 0.3f, t => (Sin(t, Mathf.Lerp(1400f, 600f, t / 0.3f)) * 0.25f) * Mathf.Exp(-t * 8f));
                case "balloon_pop":
                    return Make(name, 0.2f, t => Noise() * Mathf.Exp(-t * 45f) + Sin(t, 90f) * 0.5f * Mathf.Exp(-t * 40f));
                case "weld":
                    return Make(name, 0.3f, t => (Sin(t, 1900f) * 0.3f + Sin(t, 3100f) * 0.2f) * Mathf.Exp(-t * 16f) + Noise() * 0.3f * Mathf.Exp(-t * 200f));
                case "rope":
                    // Plucked string: decaying low partials with a slight pitch drop.
                    return Make(name, 0.45f, t =>
                    {
                        float f = Mathf.Lerp(220f, 180f, t / 0.45f);
                        return (Sin(t, f) * 0.4f + Sin(t, f * 2f) * 0.2f + Sin(t, f * 3f) * 0.08f) * Mathf.Exp(-t * 7f);
                    });
                case "remove":
                    {
                        float lp = 0f;
                        return Make(name, 0.3f, t =>
                        {
                            lp += (Noise() - lp) * 0.12f;
                            return lp * 2.4f * Mathf.Exp(-t * 14f) + Sin(t, Mathf.Lerp(500f, 120f, t / 0.3f)) * 0.2f * Mathf.Exp(-t * 12f);
                        });
                    }
                case "lamp_toggle":
                    return Make(name, 0.06f, t => (Noise() * 0.4f + Sin(t, 1500f) * 0.5f) * Mathf.Exp(-t * 120f));
                default:
                    CmodLog.Warn($"[weapons] unknown sound '{name}'");
                    return null;
            }
        }

        /// <summary>One short mechanical click/clack at time `at`.</summary>
        static float Clack(float t, float at, float freq, Func<float> noise)
        {
            float d = t - at;
            if (d < 0f || d > 0.08f) return 0f;
            return (noise() * 0.5f + Mathf.Sin(2f * Mathf.PI * freq * d) * 0.5f) * Mathf.Exp(-d * 90f) * 0.7f;
        }

        static AudioClip Make(string name, float seconds, Func<float, float> sample)
        {
            int len = Mathf.Max(1, Mathf.RoundToInt(seconds * SampleRate));
            var data = new float[len];
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)SampleRate;
                float v = sample(t);
                // 3ms fade in/out to avoid clicks at the ends.
                float edge = Mathf.Min(1f, Mathf.Min(t, seconds - t) / 0.003f);
                data[i] = Mathf.Clamp(v * edge, -1f, 1f) * 0.8f;
            }
            var clip = AudioClip.Create("cmod_" + name, len, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        // =====================================================================================
        // Tracers, flashes and bullet holes
        // =====================================================================================

        /// <summary>A short-lived beam from a to b that fades out.</summary>
        public static void Tracer(Vector3 a, Vector3 b, Color c, float width, float life)
            => Runner.SpawnTracer(a, b, c, width, life);

        /// <summary>A brief point light (muzzle flash, toolgun flash).</summary>
        public static void FlashLight(Vector3 pos, Color c, float intensity, float range, float life)
            => Runner.SpawnLight(pos, c, intensity, range, life);

        /// <summary>A brief glow sprite at a point (impact spark, muzzle flash).</summary>
        public static void FlashSprite(Vector3 pos, Color c, float size, float life)
            => Runner.SpawnFlashSprite(pos, c, size, life);

        /// <summary>A small dark decal on the surface. Pooled (max 64); it follows moving props.</summary>
        public static void BulletHole(RaycastHit hit)
            => Runner.SpawnBulletHole(hit);

        internal static Material HoleMaterial
        {
            get
            {
                if (s_holeMat != null) return s_holeMat;
                if (s_holeTex == null)
                {
                    const int n = 32;
                    s_holeTex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "cmod_bullethole", wrapMode = TextureWrapMode.Clamp };
                    var px = new Color32[n * n];
                    var rng = new System.Random(64);
                    for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                        float d = Mathf.Sqrt(dx * dx + dy * dy) + (float)rng.NextDouble() * 0.12f;
                        byte a = d < 0.35f ? (byte)255 : d < 0.9f ? (byte)(Mathf.Clamp01((0.9f - d) / 0.55f) * 170f) : (byte)0;
                        byte v = d < 0.35f ? (byte)8 : (byte)40;
                        px[y * n + x] = new Color32(v, v, v, a);
                    }
                    s_holeTex.SetPixels32(px);
                    s_holeTex.Apply(false, true);
                }
                s_holeMat = CmodMaterials.Unlit(Color.white);
                s_holeMat.mainTexture = s_holeTex;
                return s_holeMat;
            }
        }
    }

    /// <summary>Faces the main camera every frame. Size is the world-space diameter.</summary>
    public sealed class GlowSprite : MonoBehaviour
    {
        public float Size = 0.2f;

        void LateUpdate()
        {
            var cam = CmodPlayer.Local != null && CmodPlayer.Local.Cam != null ? CmodPlayer.Local.Cam : Camera.main;
            if (cam == null) return;
            var t = transform;
            t.rotation = Quaternion.LookRotation(t.position - cam.transform.position, cam.transform.up);
            var parentScale = t.parent != null ? t.parent.lossyScale : Vector3.one;
            float ps = Mathf.Max(0.0001f, (Mathf.Abs(parentScale.x) + Mathf.Abs(parentScale.y) + Mathf.Abs(parentScale.z)) / 3f);
            t.localScale = Vector3.one * (Size / ps);
        }
    }

    /// <summary>Animates the pooled effects. Lives on the hidden "CmodWeaponFx" object.</summary>
    internal sealed class WeaponFxRunner : MonoBehaviour
    {
        // ---- Audio ---------------------------------------------------------------------------
        AudioSource[] _src2D;
        AudioSource[] _src3D;
        int _next2D, _next3D;

        void Awake()
        {
            _src2D = new AudioSource[6];
            for (int i = 0; i < _src2D.Length; i++)
            {
                var s = gameObject.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 0f;
                _src2D[i] = s;
            }
            _src3D = new AudioSource[10];
            for (int i = 0; i < _src3D.Length; i++)
            {
                var go = new GameObject("Sfx3D");
                go.transform.SetParent(transform, false);
                var s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 1f;
                s.rolloffMode = AudioRolloffMode.Linear;
                s.minDistance = 2f;
                s.maxDistance = 60f;
                s.dopplerLevel = 0f;
                _src3D[i] = s;
            }
        }

        AudioSource Pick(AudioSource[] pool, ref int next)
        {
            for (int i = 0; i < pool.Length; i++)
            {
                int k = (next + i) % pool.Length;
                if (!pool[k].isPlaying) { next = (k + 1) % pool.Length; return pool[k]; }
            }
            var s = pool[next];
            next = (next + 1) % pool.Length;
            return s;
        }

        internal void Play2D(AudioClip clip, float volume, float pitch)
        {
            var s = Pick(_src2D, ref _next2D);
            s.Stop();
            s.clip = clip;
            s.volume = Mathf.Clamp01(volume);
            s.pitch = pitch;
            s.Play();
        }

        internal void Play3D(AudioClip clip, Vector3 pos, float volume, float pitch)
        {
            var s = Pick(_src3D, ref _next3D);
            s.Stop();
            s.transform.position = pos;
            s.clip = clip;
            s.volume = Mathf.Clamp01(volume);
            s.pitch = pitch;
            s.Play();
        }

        // ---- Tracers ---------------------------------------------------------------------------
        sealed class TracerFx { public LineRenderer Line; public float Born, Life; public Color Color; public float Width; }
        readonly List<TracerFx> _tracers = new List<TracerFx>();

        internal void SpawnTracer(Vector3 a, Vector3 b, Color c, float width, float life)
        {
            TracerFx t = null;
            foreach (var x in _tracers) if (x.Line != null && !x.Line.enabled) { t = x; break; }
            if (t == null)
            {
                if (_tracers.Count >= 16) t = _tracers[0];
                else
                {
                    t = new TracerFx { Line = WeaponFx.MakeLine("Tracer", transform, c, width, 2) };
                    _tracers.Add(t);
                }
            }
            t.Line.SetPosition(0, a);
            t.Line.SetPosition(1, b);
            t.Born = Time.time;
            t.Life = Mathf.Max(0.01f, life);
            t.Color = c;
            t.Width = width;
            t.Line.enabled = true;
        }

        // ---- Flash lights and sprites -------------------------------------------------------
        sealed class LightFx { public Light Light; public float Born, Life, Intensity; }
        readonly List<LightFx> _lights = new List<LightFx>();
        sealed class SpriteFx { public GlowSprite Sprite; public Material Mat; public float Born, Life, Size; public Color Color; }
        readonly List<SpriteFx> _sprites = new List<SpriteFx>();

        internal void SpawnLight(Vector3 pos, Color c, float intensity, float range, float life)
        {
            LightFx l = null;
            foreach (var x in _lights) if (x.Light != null && !x.Light.enabled) { l = x; break; }
            if (l == null)
            {
                if (_lights.Count >= 4) l = _lights[0];
                else
                {
                    var go = new GameObject("FlashLight");
                    go.transform.SetParent(transform, false);
                    var light = go.AddComponent<Light>();
                    light.type = LightType.Point;
                    light.shadows = LightShadows.None;
                    l = new LightFx { Light = light };
                    _lights.Add(l);
                }
            }
            l.Light.transform.position = pos;
            l.Light.color = c;
            l.Light.range = range;
            l.Light.intensity = intensity;
            l.Intensity = intensity;
            l.Born = Time.time;
            l.Life = Mathf.Max(0.01f, life);
            l.Light.enabled = true;
        }

        internal void SpawnFlashSprite(Vector3 pos, Color c, float size, float life)
        {
            SpriteFx s = null;
            foreach (var x in _sprites) if (x.Sprite != null && !x.Sprite.gameObject.activeSelf) { s = x; break; }
            if (s == null)
            {
                if (_sprites.Count >= 12) s = _sprites[0];
                else
                {
                    var gs = WeaponFx.MakeGlowSprite("Flash", transform, c, size);
                    s = new SpriteFx { Sprite = gs, Mat = gs.GetComponent<Renderer>().sharedMaterial };
                    _sprites.Add(s);
                }
            }
            s.Sprite.transform.position = pos;
            s.Born = Time.time;
            s.Life = Mathf.Max(0.01f, life);
            s.Size = size;
            s.Color = c;
            s.Sprite.Size = size;
            s.Mat.color = c;
            s.Sprite.gameObject.SetActive(true);
        }

        // ---- Bullet holes -----------------------------------------------------------------------
        const int MaxHoles = 64;
        sealed class Hole { public Transform T; public Transform Target; public Vector3 LocalPos; public Quaternion LocalRot; public bool HasTarget; }
        readonly Hole[] _holes = new Hole[MaxHoles];
        int _nextHole;

        internal void SpawnBulletHole(RaycastHit hit)
        {
            var h = _holes[_nextHole];
            if (h == null || h.T == null)
            {
                var go = new GameObject("BulletHole");
                go.layer = WeaponFx.FxLayer;
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = WeaponFx.PrimitiveMesh(PrimitiveType.Quad);
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = WeaponFx.HoleMaterial;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                h = new Hole { T = go.transform };
                _holes[_nextHole] = h;
            }
            _nextHole = (_nextHole + 1) % MaxHoles;

            // The quad faces -Z, so look along -normal to face out of the surface; random spin.
            var rot = Quaternion.LookRotation(-hit.normal) * Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(0f, 360f));
            // Sprites/Default has no depth offset, and depth precision falls off with distance (near clip 0.03 m),
            // so lift the hole further off the surface the farther it is from the camera to avoid z-fighting.
            var cam = CmodPlayer.Local != null && CmodPlayer.Local.Cam != null ? CmodPlayer.Local.Cam : Camera.main;
            float dist = cam != null ? Vector3.Distance(cam.transform.position, hit.point) : 10f;
            var pos = hit.point + hit.normal * (0.004f + 0.0003f * dist);
            h.T.SetPositionAndRotation(pos, rot);
            h.T.localScale = Vector3.one * UnityEngine.Random.Range(0.07f, 0.1f);
            h.T.gameObject.SetActive(true);

            // Follow moving things (props). World geometry without a rigidbody stays put.
            var target = hit.rigidbody != null ? hit.rigidbody.transform : null;
            h.HasTarget = target != null;
            h.Target = target;
            if (target != null)
            {
                h.LocalPos = target.InverseTransformPoint(pos);
                h.LocalRot = Quaternion.Inverse(target.rotation) * rot;
            }
        }

        // ---- Update ------------------------------------------------------------------------------
        void LateUpdate()
        {
            try { Animate(); }
            catch (Exception ex) { CmodLog.Error($"[weaponfx] {ex.GetType().Name}: {ex.Message}"); }
        }

        void Animate()
        {
            float now = Time.time;

            foreach (var t in _tracers)
            {
                if (t.Line == null || !t.Line.enabled) continue;
                float k = (now - t.Born) / t.Life;
                if (k >= 1f) { t.Line.enabled = false; continue; }
                var c = t.Color; c.a *= 1f - k;
                t.Line.startColor = c;
                c.a *= 0.4f;
                t.Line.endColor = c;
                t.Line.startWidth = t.Line.endWidth = t.Width * (1f - k * 0.5f);
            }

            foreach (var l in _lights)
            {
                if (l.Light == null || !l.Light.enabled) continue;
                float k = (now - l.Born) / l.Life;
                if (k >= 1f) { l.Light.enabled = false; continue; }
                l.Light.intensity = l.Intensity * (1f - k) * (1f - k);
            }

            foreach (var s in _sprites)
            {
                if (s.Sprite == null || !s.Sprite.gameObject.activeSelf) continue;
                float k = (now - s.Born) / s.Life;
                if (k >= 1f) { s.Sprite.gameObject.SetActive(false); continue; }
                var c = s.Color; c.a *= 1f - k;
                s.Mat.color = c;
                s.Sprite.Size = s.Size * (1f + k * 0.5f);
            }

            for (int i = 0; i < MaxHoles; i++)
            {
                var h = _holes[i];
                if (h == null || h.T == null || !h.HasTarget) continue;
                if (h.Target == null) { h.HasTarget = false; h.T.gameObject.SetActive(false); continue; }   // prop removed
                h.T.SetPositionAndRotation(h.Target.TransformPoint(h.LocalPos), h.Target.rotation * h.LocalRot);
            }
        }
    }
}
