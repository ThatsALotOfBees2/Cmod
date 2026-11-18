using UnityEngine;

namespace Cmod
{
    /// <summary>
    /// The built-in spawnable props, registered with PropRegistry by CmodModules.Content_Register.
    ///
    /// Sizes are in metres and masses in kilograms, chosen to be believable for the real object
    /// (hollow crates and drums are light, ice and bowling balls are dense) while staying fun to throw around.
    /// Primitive reference sizes: Cube 1x1x1, Sphere diameter 1, Cylinder diameter 1 x height 2, Capsule 1 x 2.
    /// </summary>
    public static class DefaultProps
    {
        public const string Construction = "Construction";
        public const string Containers = "Containers";
        public const string Fun = "Fun";
        public const string Furniture = "Furniture";

        // A few reusable colors.
        static readonly Color Pine = new Color(0.78f, 0.62f, 0.40f);
        static readonly Color Oak = new Color(0.58f, 0.40f, 0.24f);
        static readonly Color DarkWood = new Color(0.36f, 0.24f, 0.15f);
        static readonly Color Steel = new Color(0.62f, 0.64f, 0.67f);
        static readonly Color Concrete = new Color(0.66f, 0.65f, 0.62f);
        static readonly Color Teal = new Color(0f, 0.5f, 0.5f);

        public static void Register()
        {
            // ---------------------------------------------------------------- Construction
            Box("plate_wood_1x1", "Wood Plate 1x1", Construction, new Vector3(1f, 0.05f, 1f), 30f, Pine, PhysicMaterialPreset.Wood);
            Box("plate_wood_2x2", "Wood Plate 2x2", Construction, new Vector3(2f, 0.05f, 2f), 110f, Pine, PhysicMaterialPreset.Wood);
            Box("plate_metal_2x1", "Metal Plate 2x1", Construction, new Vector3(2f, 0.03f, 1f), 160f, Steel, PhysicMaterialPreset.Metal);
            Box("beam_wood_2m", "Wood Beam 2m", Construction, new Vector3(0.2f, 0.2f, 2f), 48f, Oak, PhysicMaterialPreset.Wood);
            Box("beam_steel_4m", "Steel Beam 4m", Construction, new Vector3(0.15f, 0.3f, 4f), 180f, new Color(0.45f, 0.47f, 0.5f), PhysicMaterialPreset.Metal);
            Box("block_cinder", "Cinder Block", Construction, new Vector3(0.4f, 0.2f, 0.2f), 16f, Concrete, PhysicMaterialPreset.Default);
            Box("block_wood_cube", "Wood Cube 0.5m", Construction, new Vector3(0.5f, 0.5f, 0.5f), 70f, Pine, PhysicMaterialPreset.Wood);
            Add("pillar_concrete", "Concrete Pillar", Construction, PrimitiveType.Cylinder, new Vector3(0.35f, 1f, 0.35f), 230f, Concrete, PhysicMaterialPreset.Default);

            // ---------------------------------------------------------------- Containers
            Box("crate_small", "Wooden Crate", Containers, new Vector3(0.6f, 0.6f, 0.6f), 15f, Oak, PhysicMaterialPreset.Wood, CrateTrim);
            Box("crate_large", "Large Crate", Containers, new Vector3(1.2f, 1.2f, 1.2f), 45f, Oak, PhysicMaterialPreset.Wood, CrateTrim);
            Box("cardboard_box", "Cardboard Box", Containers, new Vector3(0.5f, 0.35f, 0.4f), 1.5f, new Color(0.72f, 0.56f, 0.36f), PhysicMaterialPreset.Default);
            Add("barrel_red", "Red Barrel", Containers, PrimitiveType.Cylinder, new Vector3(0.58f, 0.44f, 0.58f), 18f, new Color(0.72f, 0.12f, 0.1f), PhysicMaterialPreset.Metal, BarrelRings);
            Add("drum_blue", "Oil Drum", Containers, PrimitiveType.Cylinder, new Vector3(0.58f, 0.44f, 0.58f), 22f, new Color(0.15f, 0.3f, 0.62f), PhysicMaterialPreset.Metal, BarrelRings);
            Add("can_soda", "Soda Can", Containers, PrimitiveType.Cylinder, new Vector3(0.066f, 0.061f, 0.066f), 0.37f, new Color(0.85f, 0.1f, 0.15f), PhysicMaterialPreset.Metal);
            Add("can_paint", "Paint Can", Containers, PrimitiveType.Cylinder, new Vector3(0.17f, 0.095f, 0.17f), 4.5f, new Color(0.85f, 0.85f, 0.88f), PhysicMaterialPreset.Metal);

            // ---------------------------------------------------------------- Fun
            Add("ball_beach", "Beach Ball", Fun, PrimitiveType.Sphere, Vector3.one * 0.6f, 0.3f, new Color(1f, 0.85f, 0.2f), PhysicMaterialPreset.Rubber, ent =>
            {
                // Beach balls drift: lots of air drag.
                if (ent.Body != null) { PhysicsCompat.SetDrag(ent.Body, 0.6f); PhysicsCompat.SetAngularDrag(ent.Body, 0.4f); }
            });
            Add("ball_rubber", "Rubber Ball", Fun, PrimitiveType.Sphere, Vector3.one * 0.3f, 0.5f, new Color(0.9f, 0.2f, 0.55f), PhysicMaterialPreset.Rubber);
            Add("ball_soccer", "Soccer Ball", Fun, PrimitiveType.Sphere, Vector3.one * 0.22f, 0.43f, Color.white, PhysicMaterialPreset.Rubber);
            Add("ball_bowling", "Bowling Ball", Fun, PrimitiveType.Sphere, Vector3.one * 0.22f, 7f, new Color(0.1f, 0.12f, 0.3f), PhysicMaterialPreset.Metal);
            Add("ball_giant", "Giant Ball", Fun, PrimitiveType.Sphere, Vector3.one * 3f, 150f, Teal, PhysicMaterialPreset.Rubber);
            Box("ice_block", "Ice Block", Fun, new Vector3(0.6f, 0.6f, 0.6f), 198f, new Color(0.75f, 0.9f, 1f, 0.75f), PhysicMaterialPreset.Ice);
            Box("domino", "Giant Domino", Fun, new Vector3(0.5f, 1f, 0.12f), 18f, new Color(0.95f, 0.95f, 0.92f), PhysicMaterialPreset.Wood);
            Add("capsule_pill", "Big Pill", Fun, PrimitiveType.Capsule, new Vector3(0.4f, 0.4f, 0.4f), 6f, new Color(0.9f, 0.3f, 0.3f), PhysicMaterialPreset.Rubber);

            // ---------------------------------------------------------------- Furniture (compound props)
            Compound("furniture_table", "Table", 25f, Oak, BuildTable);
            Compound("furniture_chair", "Chair", 6f, Oak, BuildChair);
            Compound("furniture_shelf", "Shelf", 35f, DarkWood, BuildShelf);
            Compound("furniture_bench", "Bench", 18f, Pine, BuildBench);
        }

        // ============================================================================================
        // Registration helpers
        // ============================================================================================

        static void Box(string id, string name, string cat, Vector3 size, float mass, Color color, PhysicMaterialPreset mat,
            System.Action<CmodEntity> onSpawned = null)
            => Add(id, name, cat, PrimitiveType.Cube, size, mass, color, mat, onSpawned);

        static void Add(string id, string name, string cat, PrimitiveType shape, Vector3 scale, float mass, Color color,
            PhysicMaterialPreset mat, System.Action<CmodEntity> onSpawned = null)
        {
            PropRegistry.Register(new PropDefinition
            {
                Id = id, DisplayName = name, Category = cat, Shape = shape, Scale = scale,
                Mass = mass, Color = color, Material = mat, OnSpawned = onSpawned,
            });
        }

        /// <summary>A furniture prop: the root is an invisible, collider-less transform with the Rigidbody,
        /// and `build` adds the visible parts (each with its own collider) as children.</summary>
        static void Compound(string id, string name, float mass, Color color, System.Action<CmodEntity, Color> build)
        {
            PropRegistry.Register(new PropDefinition
            {
                Id = id, DisplayName = name, Category = Furniture, Shape = PrimitiveType.Cube, Scale = Vector3.one,
                Mass = mass, Color = color, Material = PhysicMaterialPreset.Wood,
                OnSpawned = ent => { StripRoot(ent); build(ent, color); },
            });
        }

        /// <summary>Removes the root primitive's mesh and collider so only the child parts remain.</summary>
        static void StripRoot(CmodEntity ent)
        {
            var go = ent.gameObject;
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                if (mr.sharedMaterial != null) Object.Destroy(mr.sharedMaterial);
                Object.DestroyImmediate(mr);
            }
            var mf = go.GetComponent<MeshFilter>();
            if (mf != null) Object.DestroyImmediate(mf);
            ent.Renderer = null;
        }

        /// <summary>Adds a box part (with a BoxCollider) as a child, in the root's local space.</summary>
        static MeshRenderer Part(CmodEntity ent, string name, Vector3 localPos, Vector3 size, Color color)
        {
            var p = GameObject.CreatePrimitive(PrimitiveType.Cube);
            p.name = name;
            p.transform.SetParent(ent.transform, false);
            p.transform.localPosition = localPos;
            p.transform.localRotation = Quaternion.identity;
            p.transform.localScale = size;
            var r = p.GetComponent<MeshRenderer>();
            var m = CmodMaterials.Lit(color);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.2f);
            r.sharedMaterial = m;
            ent.OwnMaterial(m);
            // The first part becomes the entity's main renderer (used by CmodEntity.Color).
            if (ent.Renderer == null) ent.Renderer = r;
            return r;
        }

        /// <summary>The color darkened by factor f, fully opaque.</summary>
        static Color Shade(Color c, float f) => new Color(c.r * f, c.g * f, c.b * f, 1f);

        /// <summary>Adds a visual-only detail (no collider) as a child.</summary>
        static void Detail(CmodEntity ent, PrimitiveType shape, string name, Vector3 localPos, Vector3 localScale, Color color)
        {
            var p = GameObject.CreatePrimitive(shape);
            p.name = name;
            var c = p.GetComponent<Collider>();
            if (c != null) Object.DestroyImmediate(c);
            p.transform.SetParent(ent.transform, false);
            p.transform.localPosition = localPos;
            p.transform.localRotation = Quaternion.identity;
            p.transform.localScale = localScale;
            var m = CmodMaterials.Lit(color);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.3f);
            p.GetComponent<MeshRenderer>().sharedMaterial = m;
            ent.OwnMaterial(m);
        }

        // ============================================================================================
        // Decorations for simple props (visual children only, so they don't change the physics)
        // ============================================================================================

        /// <summary>Darker edge slats on crates. Works in the crate's unit-cube local space.</summary>
        static void CrateTrim(CmodEntity ent)
        {
            var c = ent.Renderer != null ? Shade(ent.Renderer.sharedMaterial.color, 0.7f) : DarkWood;
            const float t = 0.08f, o = 0.5f - t * 0.5f + 0.004f, l = 1.01f;
            // Four vertical corner posts and the top/bottom frame on the front and back faces.
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
                Detail(ent, PrimitiveType.Cube, "post", new Vector3(sx * o, 0f, sz * o), new Vector3(t, l, t), c);
            for (int sy = -1; sy <= 1; sy += 2)
            for (int sz = -1; sz <= 1; sz += 2)
                Detail(ent, PrimitiveType.Cube, "rail", new Vector3(0f, sy * o, sz * o), new Vector3(l, t, t), c);
        }

        /// <summary>Two raised rings around a drum. Cylinder local space: height -1..1, radius 0.5.</summary>
        static void BarrelRings(CmodEntity ent)
        {
            var c = ent.Renderer != null ? Shade(ent.Renderer.sharedMaterial.color, 0.75f) : Steel;
            Detail(ent, PrimitiveType.Cylinder, "ring", new Vector3(0f, 0.35f, 0f), new Vector3(1.03f, 0.03f, 1.03f), c);
            Detail(ent, PrimitiveType.Cylinder, "ring", new Vector3(0f, -0.35f, 0f), new Vector3(1.03f, 0.03f, 1.03f), c);
        }

        // ============================================================================================
        // Furniture builders. The root's origin is the centre of the piece; +Z is the front.
        // ============================================================================================

        static void BuildTable(CmodEntity ent, Color wood)
        {
            // 1.6 x 0.9 top at 0.75 m, legs 6 cm square. Origin at mid-height.
            const float h = 0.75f, top = 0.04f, w = 1.6f, d = 0.9f, leg = 0.06f;
            float y0 = -h * 0.5f;
            Part(ent, "top", new Vector3(0f, y0 + h - top * 0.5f, 0f), new Vector3(w, top, d), wood);
            float lx = w * 0.5f - leg, lz = d * 0.5f - leg, lh = h - top;
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
                Part(ent, "leg", new Vector3(sx * lx, y0 + lh * 0.5f, sz * lz), new Vector3(leg, lh, leg), Shade(wood, 0.85f));
            // Apron under the top for stiffness (visual + collision).
            Part(ent, "apron", new Vector3(0f, y0 + lh - 0.05f, lz), new Vector3(w - leg * 2f, 0.08f, 0.025f), Shade(wood, 0.85f));
            Part(ent, "apron", new Vector3(0f, y0 + lh - 0.05f, -lz), new Vector3(w - leg * 2f, 0.08f, 0.025f), Shade(wood, 0.85f));
        }

        static void BuildChair(CmodEntity ent, Color wood)
        {
            // Seat 0.45 x 0.45 at 0.45 m, back up to 0.9 m.
            const float seatH = 0.45f, seat = 0.45f, thick = 0.04f, leg = 0.04f, backH = 0.45f;
            float y0 = -(seatH + backH) * 0.5f;
            Part(ent, "seat", new Vector3(0f, y0 + seatH - thick * 0.5f, 0f), new Vector3(seat, thick, seat), wood);
            float o = seat * 0.5f - leg * 0.5f, lh = seatH - thick;
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
                Part(ent, "leg", new Vector3(sx * o, y0 + lh * 0.5f, sz * o), new Vector3(leg, lh, leg), Shade(wood, 0.85f));
            // Back on the -Z side (the front faces +Z).
            Part(ent, "back", new Vector3(0f, y0 + seatH + backH * 0.5f, -o), new Vector3(seat, backH, 0.03f), wood);
        }

        static void BuildShelf(CmodEntity ent, Color wood)
        {
            // Bookshelf 0.9 wide, 1.8 high, 0.35 deep with 5 boards.
            const float w = 0.9f, h = 1.8f, d = 0.35f, t = 0.025f;
            Part(ent, "side", new Vector3(-(w - t) * 0.5f, 0f, 0f), new Vector3(t, h, d), wood);
            Part(ent, "side", new Vector3((w - t) * 0.5f, 0f, 0f), new Vector3(t, h, d), wood);
            Part(ent, "back", new Vector3(0f, 0f, -(d - t * 0.4f) * 0.5f), new Vector3(w - t * 2f, h, t * 0.4f), Shade(wood, 0.8f));
            const int boards = 5;
            for (int i = 0; i < boards; i++)
            {
                float y = -h * 0.5f + t * 0.5f + i * ((h - t) / (boards - 1));
                Part(ent, "board", new Vector3(0f, y, 0f), new Vector3(w - t * 2f, t, d), wood);
            }
            // A few "books" for character (visual only).
            var colors = new[] { new Color(0.6f, 0.1f, 0.1f), new Color(0.1f, 0.3f, 0.55f), new Color(0.15f, 0.45f, 0.2f), new Color(0.8f, 0.7f, 0.2f) };
            float shelfY = -h * 0.5f + t + ((h - t) / (boards - 1)) * 2f;
            float x = -w * 0.5f + t + 0.03f;
            for (int i = 0; i < 9; i++)
            {
                float bw = 0.035f + (i % 3) * 0.01f, bh = 0.2f + (i % 4) * 0.03f;
                Detail(ent, PrimitiveType.Cube, "book", new Vector3(x + bw * 0.5f, shelfY + bh * 0.5f, 0.02f), new Vector3(bw, bh, d * 0.7f), colors[i % colors.Length]);
                x += bw + 0.004f;
            }
        }

        static void BuildBench(CmodEntity ent, Color wood)
        {
            // Park bench seat: 1.5 long, 0.4 deep, 0.45 high, two slab legs.
            const float len = 1.5f, d = 0.4f, h = 0.45f, t = 0.05f;
            float y0 = -h * 0.5f;
            Part(ent, "seat", new Vector3(0f, y0 + h - t * 0.5f, 0f), new Vector3(len, t, d), wood);
            Part(ent, "leg", new Vector3(-(len * 0.5f - 0.15f), y0 + (h - t) * 0.5f, 0f), new Vector3(0.06f, h - t, d * 0.9f), Shade(wood, 0.8f));
            Part(ent, "leg", new Vector3(len * 0.5f - 0.15f, y0 + (h - t) * 0.5f, 0f), new Vector3(0.06f, h - t, d * 0.9f), Shade(wood, 0.8f));
        }
    }
}
