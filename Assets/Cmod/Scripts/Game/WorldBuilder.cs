using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Cmod
{
    /// <summary>
    /// Builds the map "cm_flatgrass98" entirely from code: a 600 x 600 m grid floor, a handful of
    /// static structures to play on, a sun with soft shadows, a procedural sky, ambient light and fog.
    ///
    /// Layout (the player spawns at the origin looking down +Z). Everything is at least 50 m from the spawn:
    ///   +Z  70 m   the teal "Cmod 98" monolith (the first thing you see)
    ///   +X  70 m   stepped platforms (0.8 m steps, each one is a jump)
    ///   -X  70 m   a long 15 degree ramp up to a deck (good for rolling things down)
    ///   +X/-Z      a staircase up to a platform
    ///   -X/-Z      a lookout tower with a staircase, a railed deck at 12 m and a little roof
    ///   edges      a low teal wall around the whole map
    ///
    /// Every structure is a box with a generated mesh whose UVs are in world metres, so the
    /// Win98-style bevel tiles line up across neighbouring blocks and never stretch.
    /// </summary>
    public static class WorldBuilder
    {
        public const string MapName = "cm_flatgrass98";
        public const float MapSize = 600f;

        /// <summary>The root of all map geometry (null before Build / after Teardown).</summary>
        public static GameObject Root { get; private set; }
        /// <summary>The directional sun light.</summary>
        public static Light Sun { get; private set; }
        /// <summary>The player spawn pose returned by the last Build.</summary>
        public static Pose SpawnPose { get; private set; }
        /// <summary>True when the procedural skybox shader was found; otherwise cameras use SkyFallbackColor.</summary>
        public static bool HasSkybox { get; private set; }
        public static readonly Color SkyFallbackColor = new Color(0.53f, 0.68f, 0.86f);

        static readonly Color FogColor = new Color(0.70f, 0.77f, 0.85f);

        // Materials shared by the map geometry (created per Build, destroyed on Teardown).
        static readonly List<Object> s_owned = new List<Object>();
        static Material s_matGround, s_matPanel, s_matTeal, s_matNavy, s_matConcrete, s_matWood;

        // =======================================================================================
        // Build / teardown
        // =======================================================================================

        /// <summary>Builds the map and returns the player spawn pose.</summary>
        public static Pose Build()
        {
            if (Root != null) Teardown();

            Root = new GameObject("World (" + MapName + ")");
            CreateMaterials();

            BuildGround();
            BuildEdgeWalls();
            BuildSpawnPad();
            BuildMonolith(new Vector3(0f, 0f, 70f));
            BuildSteppedPlatforms(new Vector3(70f, 0f, 14f));
            BuildRamp(new Vector3(-70f, 0f, 0f));
            BuildStairs(new Vector3(48f, 0f, -62f));
            BuildTower(new Vector3(-62f, 0f, -58f));

            SetupLighting();

            // Feet on the spawn pad, facing +Z (towards the monolith).
            SpawnPose = new Pose(new Vector3(0f, 0.12f, 0f), Quaternion.identity);
            CmodLog.Info($"Map {MapName} built.");
            return SpawnPose;
        }

        /// <summary>Destroys the map geometry and its generated textures, meshes and materials.</summary>
        public static void Teardown()
        {
            if (Root != null) Object.Destroy(Root);
            Root = null;
            Sun = null;
            foreach (var o in s_owned) if (o != null) Object.Destroy(o);
            s_owned.Clear();
            RestoreSceneLights();
            RestoreQualitySettings();
        }

        // =======================================================================================
        // Materials and procedural textures
        // =======================================================================================

        static void CreateMaterials()
        {
            var grid = Own(MakeGridTexture());
            var bevel = Own(MakeBevelTexture());

            // Ground: UVs are metres, one texture tile = 8 m (accent lines every 8 m).
            s_matGround = Own(CmodMaterials.Lit(Color.white));
            s_matGround.name = "cm_ground";
            s_matGround.mainTexture = grid;
            s_matGround.mainTextureScale = Vector2.one / 8f;
            SetGloss(s_matGround, 0.08f);

            s_matPanel = TiledMat("cm_panel", bevel, new Color(0.93f, 0.93f, 0.93f), 1f);
            s_matTeal = TiledMat("cm_teal", bevel, new Color(0.05f, 0.62f, 0.62f), 1f);
            s_matNavy = TiledMat("cm_navy", bevel, new Color(0.12f, 0.16f, 0.62f), 1f);
            s_matConcrete = TiledMat("cm_concrete", bevel, new Color(0.78f, 0.78f, 0.76f), 2f);
            s_matWood = TiledMat("cm_wood", bevel, new Color(0.80f, 0.62f, 0.40f), 0.5f);
        }

        /// <summary>A lit material using `tex`, one tile every `tileMeters` metres.</summary>
        static Material TiledMat(string name, Texture2D tex, Color tint, float tileMeters)
        {
            var m = Own(CmodMaterials.Lit(tint));
            m.name = name;
            m.mainTexture = tex;
            m.mainTextureScale = Vector2.one / tileMeters;
            SetGloss(m, 0.12f);
            return m;
        }

        static void SetGloss(Material m, float g)
        {
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", g);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
        }

        /// <summary>
        /// 512 px = 8 m floor tile: 1 m checker squares in two light grays, thin seams between them,
        /// and a teal accent line along the tile edge (so every 8 m in the world).
        /// </summary>
        static Texture2D MakeGridTexture()
        {
            const int size = 512, ppm = size / 8; // pixels per metre
            var light = new Color32(206, 206, 206, 255);
            var dark = new Color32(188, 188, 188, 255);
            var seam = new Color32(150, 150, 150, 255);
            var accent = new Color32(0, 128, 128, 255);
            var accentEdge = new Color32(60, 150, 150, 255);

            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int cx = x / ppm, cy = y / ppm;
                var c = ((cx + cy) & 1) == 0 ? light : dark;
                int mx = x % ppm, my = y % ppm;
                if (mx == 0 || my == 0) c = seam;                       // 1 m seams
                // 8 m accent: 2 px each side of the tile edge (wraps, so 4 px wide in the world).
                bool ax = x < 2 || x >= size - 2, ay = y < 2 || y >= size - 2;
                if (ax || ay) c = accent;
                else if (x < 3 || x >= size - 3 || y < 3 || y >= size - 3) c = accentEdge;
                px[y * size + x] = c;
            }
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = "cm_grid", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 9,
            };
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        /// <summary>
        /// 64 px tile that looks like a raised Win98 button face: bright top/left edges,
        /// dark bottom/right edges. Grayscale, so the material color tints it.
        /// </summary>
        static Texture2D MakeBevelTexture()
        {
            const int size = 64;
            var face = new Color32(212, 212, 212, 255);
            var hi = new Color32(255, 255, 255, 255);
            var lite = new Color32(236, 236, 236, 255);
            var shadow = new Color32(150, 150, 150, 255);
            var dark = new Color32(100, 100, 100, 255);

            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // Texture y = 0 is the bottom row.
                Color32 c = face;
                int top = size - 1 - y;
                if (x == size - 1 || y == 0) c = dark;                       // outer bottom/right
                else if (x == 0 || top == 0) c = hi;                         // outer top/left
                else if (x == size - 2 || y == 1) c = shadow;                // inner bottom/right
                else if (x == 1 || top == 1) c = lite;                       // inner top/left
                px[y * size + x] = c;
            }
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = "cm_bevel", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, anisoLevel = 4,
            };
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        // =======================================================================================
        // Geometry helpers
        // =======================================================================================

        /// <summary>
        /// A static box with a BoxCollider and a generated mesh whose UVs are in metres
        /// (measured from the world origin for unrotated boxes, so adjacent blocks' textures line up).
        /// </summary>
        static GameObject Block(string name, Vector3 center, Vector3 size, Material mat, Quaternion? rotation = null, Transform parent = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent != null ? parent : Root.transform, false);
            var rot = rotation ?? Quaternion.identity;
            go.transform.SetPositionAndRotation(center, rot);

            // World-aligned UVs only make sense without rotation; rotated boxes use local coordinates.
            var uvOrigin = rotation.HasValue ? Vector3.zero : center;
            go.AddComponent<MeshFilter>().sharedMesh = Own(BoxMesh(size, uvOrigin));
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.On;
            r.receiveShadows = true;
            go.AddComponent<BoxCollider>().size = size;
            return go;
        }

        /// <summary>Box mesh centred on the origin, 24 vertices, UVs = metres along each face.</summary>
        static Mesh BoxMesh(Vector3 size, Vector3 uvOrigin)
        {
            var h = size * 0.5f;
            var verts = new Vector3[24];
            var norms = new Vector3[24];
            var uvs = new Vector2[24];
            var tris = new int[36];
            var normals = new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            for (int f = 0; f < 6; f++)
            {
                var n = normals[f];
                // v = "up" as seen by someone looking at the face; u = their "right" (Unity is left-handed).
                var v = Mathf.Abs(n.y) > 0.5f ? Vector3.forward : Vector3.up;
                var u = Vector3.Cross(v, -n);
                var c = Vector3.Scale(n, h);
                var hu = Vector3.Scale(u, h).magnitude;
                var hv = Vector3.Scale(v, h).magnitude;
                var corners = new[]
                {
                    c - u * hu - v * hv, c - u * hu + v * hv, c + u * hu + v * hv, c + u * hu - v * hv,
                };
                for (int i = 0; i < 4; i++)
                {
                    int k = f * 4 + i;
                    verts[k] = corners[i];
                    norms[k] = n;
                    var w = corners[i] + uvOrigin;
                    uvs[k] = new Vector2(Vector3.Dot(w, u), Vector3.Dot(w, v));
                }
                int t = f * 6, b = f * 4;
                tris[t] = b; tris[t + 1] = b + 1; tris[t + 2] = b + 2;
                tris[t + 3] = b; tris[t + 4] = b + 2; tris[t + 5] = b + 3;
            }
            var mesh = new Mesh { name = "cm_box" };
            mesh.vertices = verts;
            mesh.normals = norms;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        static GameObject Group(string name, Vector3 pos)
        {
            var g = new GameObject(name);
            g.transform.SetParent(Root.transform, false);
            g.transform.position = pos;
            return g;
        }

        static T Own<T>(T obj) where T : Object
        {
            s_owned.Add(obj);
            return obj;
        }

        // =======================================================================================
        // Structures
        // =======================================================================================

        static void BuildGround()
        {
            var g = Block("Ground", new Vector3(0f, -1f, 0f), new Vector3(MapSize, 2f, MapSize), s_matGround);
            // A flat floor never casts a useful shadow; only receive.
            g.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        static void BuildEdgeWalls()
        {
            var parent = Group("Edge walls", Vector3.zero).transform;
            const float h = 1.5f, t = 1f;
            float half = MapSize * 0.5f;
            Block("Wall N", new Vector3(0f, h * 0.5f, half - t * 0.5f), new Vector3(MapSize, h, t), s_matTeal, null, parent);
            Block("Wall S", new Vector3(0f, h * 0.5f, -half + t * 0.5f), new Vector3(MapSize, h, t), s_matTeal, null, parent);
            Block("Wall E", new Vector3(half - t * 0.5f, h * 0.5f, 0f), new Vector3(t, h, MapSize - t * 2f), s_matTeal, null, parent);
            Block("Wall W", new Vector3(-half + t * 0.5f, h * 0.5f, 0f), new Vector3(t, h, MapSize - t * 2f), s_matTeal, null, parent);
        }

        static void BuildSpawnPad()
        {
            // A thin teal pad (6 cm, well under the player's step height) marking the spawn.
            var parent = Group("Spawn pad", Vector3.zero).transform;
            Block("Pad", new Vector3(0f, 0.03f, 0f), new Vector3(3f, 0.06f, 3f), s_matTeal, null, parent);
            Block("Pad inset", new Vector3(0f, 0.035f, 0f), new Vector3(2f, 0.06f, 2f), s_matNavy, null, parent);
        }

        /// <summary>The teal "Cmod 98" monolith, its face towards the spawn (-Z side).</summary>
        static void BuildMonolith(Vector3 at)
        {
            var parent = Group("Monolith", at).transform;
            const float w = 6f, h = 14f, d = 1.2f, baseH = 0.5f;
            Block("Base", at + new Vector3(0f, baseH * 0.5f, 0f), new Vector3(w + 3f, baseH, d + 3f), s_matPanel, null, parent);
            Block("Slab", at + new Vector3(0f, baseH + h * 0.5f, 0f), new Vector3(w, h, d), s_matTeal, null, parent);
            Block("Cap", at + new Vector3(0f, baseH + h + 0.15f, 0f), new Vector3(w + 0.4f, 0.3f, d + 0.4f), s_matNavy, null, parent);

            var logo = Own(MakeLogoTexture());
            var mat = Own(CmodMaterials.Lit(Color.white));
            mat.name = "cm_logo";
            mat.mainTexture = logo;
            SetGloss(mat, 0.2f);
            // Glow a little so the logo stays readable on the shaded side.
            if (mat.HasProperty("_EmissionMap")) mat.SetTexture("_EmissionMap", logo);
            CmodMaterials.MakeEmissive(mat, new Color(0.35f, 0.35f, 0.35f));

            float logoW = w - 0.6f, logoH = logoW * logo.height / logo.width;
            float y = baseH + h - logoH * 0.5f - 1.2f;
            // Front (facing -Z, the spawn) and back.
            LogoQuad(parent, mat, at + new Vector3(0f, y, -d * 0.5f - 0.01f), Quaternion.identity, logoW, logoH);
            LogoQuad(parent, mat, at + new Vector3(0f, y, d * 0.5f + 0.01f), Quaternion.Euler(0f, 180f, 0f), logoW, logoH);
        }

        static void LogoQuad(Transform parent, Material mat, Vector3 pos, Quaternion rot, float w, float h)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = "Logo";
            var col = q.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
            q.transform.SetParent(parent, false);
            q.transform.SetPositionAndRotation(pos, rot);
            q.transform.localScale = new Vector3(w, h, 1f);
            var r = q.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
        }

        /// <summary>Stepped platforms: four 8x8 m blocks, each 0.8 m higher (one jump) than the last.</summary>
        static void BuildSteppedPlatforms(Vector3 at)
        {
            var parent = Group("Platforms", at).transform;
            const float step = 0.8f, size = 8f;
            for (int i = 0; i < 4; i++)
            {
                float hgt = step * (i + 1);
                var c = at + new Vector3(0f, hgt * 0.5f, i * size);
                Block("Platform " + (i + 1), c, new Vector3(size, hgt, size), i % 2 == 0 ? s_matPanel : s_matConcrete, null, parent);
                // Navy trim strip along the top front edge.
                Block("Trim", at + new Vector3(0f, hgt + 0.05f, i * size - size * 0.5f + 0.15f), new Vector3(size, 0.1f, 0.3f), s_matNavy, null, parent);
            }
            // A big top deck with a teal rail at the far end.
            float topH = step * 4f;
            Block("Rail", at + new Vector3(0f, topH + 0.5f, 3 * size + size * 0.5f - 0.2f), new Vector3(size, 1f, 0.4f), s_matTeal, null, parent);
        }

        /// <summary>A long 15 degree ramp up to a deck on pillars. Rises along +Z.</summary>
        static void BuildRamp(Vector3 at)
        {
            var parent = Group("Ramp", at).transform;
            const float angle = 15f, length = 22f, width = 6f, thick = 0.5f;
            float rad = angle * Mathf.Deg2Rad;
            float rise = Mathf.Sin(rad) * length, run = Mathf.Cos(rad) * length;
            var rot = Quaternion.Euler(-angle, 0f, 0f); // tilt so +Z goes up

            // Place the slab so its top surface starts at ground level at z = at.z and reaches `rise` at z = at.z + run.
            var topMid = at + new Vector3(0f, rise * 0.5f, run * 0.5f);
            var center = topMid - (rot * Vector3.up) * (thick * 0.5f);
            Block("Slope", center, new Vector3(width, thick, length), s_matConcrete, rot, parent);

            // Deck at the top.
            const float deck = 8f, deckT = 0.5f;
            var deckCenter = at + new Vector3(0f, rise - deckT * 0.5f, run + deck * 0.5f - 0.05f);
            Block("Deck", deckCenter, new Vector3(deck, deckT, deck), s_matPanel, null, parent);
            // Pillars under the deck and the high end of the slope.
            float pillarH = rise - deckT;
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = 0; sz <= 1; sz++)
            {
                var p = at + new Vector3(sx * (deck * 0.5f - 0.5f), pillarH * 0.5f, run + 0.5f + sz * (deck - 1f));
                Block("Pillar", p, new Vector3(0.6f, pillarH, 0.6f), s_matNavy, null, parent);
            }
            // Side rails on the deck (open towards the slope so things can roll back down).
            Block("Rail", deckCenter + new Vector3(-deck * 0.5f + 0.15f, deckT * 0.5f + 0.4f, 0f), new Vector3(0.3f, 0.8f, deck), s_matTeal, null, parent);
            Block("Rail", deckCenter + new Vector3(deck * 0.5f - 0.15f, deckT * 0.5f + 0.4f, 0f), new Vector3(0.3f, 0.8f, deck), s_matTeal, null, parent);
            Block("Rail", deckCenter + new Vector3(0f, deckT * 0.5f + 0.4f, deck * 0.5f - 0.15f), new Vector3(deck, 0.8f, 0.3f), s_matTeal, null, parent);
        }

        /// <summary>A wide solid staircase (0.25 m rise, 0.4 m run) up to a 3 m platform. Rises along +Z.</summary>
        static void BuildStairs(Vector3 at)
        {
            var parent = Group("Stairs", at).transform;
            const int steps = 12;
            const float rise = 0.25f, run = 0.4f, width = 4f;
            for (int i = 0; i < steps; i++)
            {
                float hgt = rise * (i + 1);
                Block("Step " + (i + 1), at + new Vector3(0f, hgt * 0.5f, i * run + run * 0.5f), new Vector3(width, hgt, run),
                    i % 2 == 0 ? s_matPanel : s_matConcrete, null, parent);
            }
            float top = rise * steps;
            const float plat = 8f;
            Block("Landing", at + new Vector3(0f, top * 0.5f, steps * run + plat * 0.5f), new Vector3(plat, top, plat), s_matTeal, null, parent);
            Block("Rail", at + new Vector3(0f, top + 0.45f, steps * run + plat - 0.15f), new Vector3(plat, 0.9f, 0.3f), s_matNavy, null, parent);
        }

        /// <summary>
        /// A lookout tower: four pillars, a 7 x 7 m railed deck at 12 m with a small roof, reached by
        /// a straight staircase (0.3 m rise, 0.42 m run) that arrives at the deck's south side.
        /// </summary>
        static void BuildTower(Vector3 at)
        {
            var parent = Group("Tower", at).transform;
            const float deckY = 12f, deck = 7f, deckT = 0.4f, pillar = 0.7f;
            float half = deck * 0.5f;

            // Pillars and cross braces.
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
                Block("Pillar", at + new Vector3(sx * (half - pillar * 0.5f), deckY * 0.5f, sz * (half - pillar * 0.5f)),
                    new Vector3(pillar, deckY, pillar), s_matConcrete, null, parent);
            for (int level = 1; level <= 2; level++)
            {
                float y = deckY * level / 3f;
                Block("Brace", at + new Vector3(0f, y, -half + pillar * 0.5f), new Vector3(deck, 0.3f, 0.3f), s_matNavy, null, parent);
                Block("Brace", at + new Vector3(0f, y, half - pillar * 0.5f), new Vector3(deck, 0.3f, 0.3f), s_matNavy, null, parent);
                Block("Brace", at + new Vector3(-half + pillar * 0.5f, y, 0f), new Vector3(0.3f, 0.3f, deck), s_matNavy, null, parent);
                Block("Brace", at + new Vector3(half - pillar * 0.5f, y, 0f), new Vector3(0.3f, 0.3f, deck), s_matNavy, null, parent);
            }

            // Deck.
            var deckTop = at + new Vector3(0f, deckY, 0f);
            Block("Deck", deckTop - new Vector3(0f, deckT * 0.5f, 0f), new Vector3(deck, deckT, deck), s_matWood, null, parent);

            // Railings: full on N/E/W, south side has an opening where the stairs arrive.
            const float railH = 1.05f, railT = 0.15f;
            const float stairW = 2.4f;
            float ry = deckY + railH * 0.5f;
            Block("Rail N", at + new Vector3(0f, ry, half - railT * 0.5f), new Vector3(deck, railH, railT), s_matTeal, null, parent);
            Block("Rail E", at + new Vector3(half - railT * 0.5f, ry, 0f), new Vector3(railT, railH, deck), s_matTeal, null, parent);
            Block("Rail W", at + new Vector3(-half + railT * 0.5f, ry, 0f), new Vector3(railT, railH, deck), s_matTeal, null, parent);
            float sideLen = (deck - stairW) * 0.5f;
            Block("Rail S", at + new Vector3(-half + sideLen * 0.5f, ry, -half + railT * 0.5f), new Vector3(sideLen, railH, railT), s_matTeal, null, parent);
            Block("Rail S", at + new Vector3(half - sideLen * 0.5f, ry, -half + railT * 0.5f), new Vector3(sideLen, railH, railT), s_matTeal, null, parent);

            // Roof on four posts.
            const float postH = 2.6f;
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
                Block("Roof post", at + new Vector3(sx * (half - 0.2f), deckY + postH * 0.5f, sz * (half - 0.2f)),
                    new Vector3(0.2f, postH, 0.2f), s_matNavy, null, parent);
            Block("Roof", at + new Vector3(0f, deckY + postH + 0.15f, 0f), new Vector3(deck + 1f, 0.3f, deck + 1f), s_matTeal, null, parent);
            Block("Roof top", at + new Vector3(0f, deckY + postH + 0.45f, 0f), new Vector3(deck - 1.5f, 0.3f, deck - 1.5f), s_matNavy, null, parent);

            // Staircase: floating treads rising along +Z, ending flush with the deck's south edge.
            const float rise = 0.3f, run = 0.42f, tread = 0.12f;
            int steps = Mathf.RoundToInt(deckY / rise);
            float startZ = -half - steps * run;
            for (int i = 0; i < steps; i++)
            {
                // The last tread ends flush with the deck's edge at deck height.
                float topY = Mathf.Min(rise * (i + 1), deckY);
                var c = at + new Vector3(0f, topY - tread * 0.5f, startZ + i * run + run * 0.5f);
                Block("Tread", c, new Vector3(stairW, tread, run + 0.04f), s_matWood, null, parent);
            }
            // Stringers along both sides (sloped beams), outside the walking width.
            float length = Mathf.Sqrt(deckY * deckY + (steps * run) * (steps * run));
            float pitch = Mathf.Atan2(deckY, steps * run) * Mathf.Rad2Deg;
            var srot = Quaternion.Euler(-pitch, 0f, 0f);
            var mid = at + new Vector3(0f, deckY * 0.5f - 0.15f, startZ + steps * run * 0.5f);
            Block("Stringer", mid + new Vector3(-(stairW * 0.5f + 0.1f), 0f, 0f), new Vector3(0.2f, 0.35f, length), s_matNavy, srot, parent);
            Block("Stringer", mid + new Vector3(stairW * 0.5f + 0.1f, 0f, 0f), new Vector3(0.2f, 0.35f, length), s_matNavy, srot, parent);
            // Handrails on the stringers.
            Block("Handrail", mid + new Vector3(-(stairW * 0.5f + 0.1f), 0.95f, 0f), new Vector3(0.08f, 0.08f, length), s_matTeal, srot, parent);
            Block("Handrail", mid + new Vector3(stairW * 0.5f + 0.1f, 0.95f, 0f), new Vector3(0.08f, 0.08f, length), s_matTeal, srot, parent);
            // Support posts under the stairs every few metres.
            for (float z = startZ + 2f; z < -half - 0.5f; z += 3f)
            {
                float yTop = (z - startZ) / (steps * run) * deckY - tread; // just under the tread above
                if (yTop < 0.3f) continue;
                Block("Stair post", at + new Vector3(0f, yTop * 0.5f, z), new Vector3(0.3f, yTop, 0.3f), s_matConcrete, null, parent);
            }
        }

        // =======================================================================================
        // Logo texture: "Cmod 98" in a 5x7 pixel font on a teal Win98-bevelled plate
        // =======================================================================================

        // 5x7 glyphs, top row first. '#' = lit pixel.
        static readonly Dictionary<char, string[]> s_glyphs = new Dictionary<char, string[]>
        {
            ['C'] = new[] { ".###.", "#...#", "#....", "#....", "#....", "#...#", ".###." },
            ['m'] = new[] { ".....", ".....", "##.#.", "#.#.#", "#.#.#", "#.#.#", "#.#.#" },
            ['o'] = new[] { ".....", ".....", ".###.", "#...#", "#...#", "#...#", ".###." },
            ['d'] = new[] { "....#", "....#", ".####", "#...#", "#...#", "#...#", ".####" },
            ['9'] = new[] { ".###.", "#...#", "#...#", ".####", "....#", "...#.", ".##.." },
            ['8'] = new[] { ".###.", "#...#", "#...#", ".###.", "#...#", "#...#", ".###." },
        };

        static Texture2D MakeLogoTexture()
        {
            const string text = "Cmod 98";
            const int scale = 8, pad = 4, glyphW = 5, glyphH = 7, gap = 1, spaceW = 3;

            int cols = 0;
            foreach (var ch in text) cols += (ch == ' ' ? spaceW : glyphW) + gap;
            cols -= gap;
            int lw = cols + pad * 2, lh = glyphH + pad * 2; // logical pixels
            int w = lw * scale, h = lh * scale;

            var bg = new Color32(0, 128, 128, 255);
            var hi = new Color32(96, 200, 200, 255);
            var lo = new Color32(0, 64, 64, 255);
            var ink = new Color32(255, 255, 255, 255);
            var shadow = new Color32(0, 40, 60, 255);

            var logical = new Color32[lw * lh];
            for (int y = 0; y < lh; y++)
            for (int x = 0; x < lw; x++)
            {
                int top = lh - 1 - y;
                var c = bg;
                if (x == lw - 1 || y == 0) c = lo;          // raised bevel: dark bottom/right
                else if (x == 0 || top == 0) c = hi;        // light top/left
                logical[y * lw + x] = c;
            }

            // Draw the glyphs with a 1 px drop shadow (down-right).
            int pen = pad;
            foreach (var ch in text)
            {
                if (ch == ' ') { pen += spaceW + gap; continue; }
                if (s_glyphs.TryGetValue(ch, out var rows))
                {
                    for (int pass = 0; pass < 2; pass++)
                    for (int gy = 0; gy < glyphH; gy++)
                    for (int gx = 0; gx < glyphW; gx++)
                    {
                        if (rows[gy][gx] != '#') continue;
                        int lx = pen + gx + (pass == 0 ? 1 : 0);
                        int ly = (lh - 1 - pad - gy) - (pass == 0 ? 1 : 0);
                        if (lx < 0 || lx >= lw || ly < 0 || ly >= lh) continue;
                        logical[ly * lw + lx] = pass == 0 ? shadow : ink;
                    }
                }
                pen += glyphW + gap;
            }

            // Upscale so bilinear filtering and mipmaps keep the pixels crisp up close and smooth far away.
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                px[y * w + x] = logical[(y / scale) * lw + (x / scale)];

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true)
            {
                name = "cm_logo", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, anisoLevel = 4,
            };
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        // =======================================================================================
        // Lighting, sky and fog
        // =======================================================================================

        // Settings changed by SetupLighting, restored on Teardown / quit.
        static readonly List<Light> s_disabledLights = new List<Light>();
        static bool s_qualitySaved;
        static float s_savedShadowDistance;
        static int s_savedShadowCascades;

        static void RestoreQualitySettings()
        {
            if (!s_qualitySaved) return;
            s_qualitySaved = false;
            Application.quitting -= RestoreQualitySettings;
            QualitySettings.shadowDistance = s_savedShadowDistance;
            QualitySettings.shadowCascades = s_savedShadowCascades;
        }

        static void RestoreSceneLights()
        {
            foreach (var l in s_disabledLights) if (l != null) l.enabled = true;
            s_disabledLights.Clear();
        }

        static void SetupLighting()
        {
            // Turn off directional lights already in the scene (e.g. the default scene's "Directional Light"),
            // so there is only one sun. They are re-enabled on Teardown.
            foreach (var l in PhysicsCompat.FindAll<Light>())
            {
                if (l == null || !l.enabled || l.type != LightType.Directional) continue;
                if (Root != null && l.transform.IsChildOf(Root.transform)) continue;
                l.enabled = false;
                s_disabledLights.Add(l);
            }

            // Sun: comes from behind the spawn so the monolith's face is lit. Soft shadows.
            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(Root.transform, false);
            sunGo.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            Sun = sunGo.AddComponent<Light>();
            Sun.type = LightType.Directional;
            Sun.color = new Color(1f, 0.96f, 0.88f);
            Sun.intensity = 1.15f;
            Sun.shadows = LightShadows.Soft;
            Sun.shadowStrength = 0.8f;
            Sun.shadowBias = 0.04f;
            Sun.shadowNormalBias = 0.35f;
            RenderSettings.sun = Sun;

            // Shadows that reach far enough to see the structures' shadows from the spawn.
            // Only when the active quality level has shadows on (a level that disables them is respected),
            // and the originals are restored on Teardown/quit so the project's quality asset is not edited.
            if (QualitySettings.shadows != ShadowQuality.Disable)
            {
                if (!s_qualitySaved)
                {
                    s_qualitySaved = true;
                    s_savedShadowDistance = QualitySettings.shadowDistance;
                    s_savedShadowCascades = QualitySettings.shadowCascades;
                    Application.quitting -= RestoreQualitySettings;
                    Application.quitting += RestoreQualitySettings;
                }
                if (QualitySettings.shadowDistance < 150f) QualitySettings.shadowDistance = 150f;
                if (QualitySettings.shadowCascades < 2) QualitySettings.shadowCascades = 2;
            }

            // Sky. Shader.Find is fine here (it's not a lit material); fall back to a clear color when stripped.
            var skyShader = Shader.Find("Skybox/Procedural");
            HasSkybox = skyShader != null;
            if (HasSkybox)
            {
                var sky = Own(new Material(skyShader) { name = "cm_sky" });
                TrySet(sky, "_SunDisk", 2f);            // high quality sun disk
                TrySet(sky, "_SunSize", 0.035f);
                TrySet(sky, "_SunSizeConvergence", 6f);
                TrySet(sky, "_AtmosphereThickness", 0.85f);
                TrySetColor(sky, "_SkyTint", new Color(0.46f, 0.56f, 0.72f));
                TrySetColor(sky, "_GroundColor", new Color(0.55f, 0.58f, 0.6f));
                TrySet(sky, "_Exposure", 1.2f);
                RenderSettings.skybox = sky;
            }
            else
            {
                RenderSettings.skybox = null;
                CmodLog.Warn("Skybox/Procedural shader not found; using a plain sky color.");
            }
            ApplySkyToCameras();

            // Ambient: a three-colour gradient is predictable (no baked GI needed).
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.58f, 0.64f, 0.74f);
            RenderSettings.ambientEquatorColor = new Color(0.50f, 0.52f, 0.54f);
            RenderSettings.ambientGroundColor = new Color(0.30f, 0.30f, 0.30f);
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = 0.6f;

            // Gentle distance fog that hides the map edge.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = FogColor;
            RenderSettings.fogStartDistance = 140f;
            RenderSettings.fogEndDistance = 650f;

            try { DynamicGI.UpdateEnvironment(); }
            catch (System.Exception ex) { CmodLog.Warn("DynamicGI.UpdateEnvironment failed: " + ex.Message); }
        }

        /// <summary>Makes every camera in the scene show the sky (skybox, or the fallback color).</summary>
        public static void ApplySkyToCameras()
        {
            foreach (var cam in Camera.allCameras) ApplySky(cam);
        }

        /// <summary>Sets one camera's clear mode for this map's sky.</summary>
        public static void ApplySky(Camera cam)
        {
            if (cam == null) return;
            if (HasSkybox) cam.clearFlags = CameraClearFlags.Skybox;
            else
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = SkyFallbackColor;
            }
        }

        static void TrySet(Material m, string prop, float v) { if (m.HasProperty(prop)) m.SetFloat(prop, v); }
        static void TrySetColor(Material m, string prop, Color c) { if (m.HasProperty(prop)) m.SetColor(prop, c); }
    }
}
