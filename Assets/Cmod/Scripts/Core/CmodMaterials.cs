using UnityEngine;

namespace Cmod
{
    /// <summary>
    /// Runtime materials that also work in player builds (Shader.Find can return null in builds when
    /// nothing references the shader). Lit materials copy Unity's built-in default material, which
    /// builds always include. Built-in render pipeline.
    /// </summary>
    public static class CmodMaterials
    {
        static Material s_litBase;
        static Material s_unlitBase;

        /// <summary>A new lit (Standard shader) material with the given color.</summary>
        public static Material Lit(Color color)
        {
            if (s_litBase == null)
            {
                var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                s_litBase = new Material(tmp.GetComponent<Renderer>().sharedMaterial) { name = "CmodLitBase" };
                Object.Destroy(tmp);
            }
            var m = new Material(s_litBase) { color = color };
            return m;
        }

        /// <summary>A new unlit, vertex-colored, alpha-blended material (beams, tracers, glows, LineRenderers).</summary>
        public static Material Unlit(Color color)
        {
            if (s_unlitBase == null)
            {
                var sh = Shader.Find("Sprites/Default");
                if (sh == null) sh = Shader.Find("Unlit/Color");
                s_unlitBase = sh != null ? new Material(sh) : new Material(Lit(Color.white));
                s_unlitBase.name = "CmodUnlitBase";
            }
            return new Material(s_unlitBase) { color = color };
        }

        /// <summary>Makes a Standard material transparent (alpha blended). Color alpha controls opacity.</summary>
        public static void MakeTransparent(Material m)
        {
            m.SetFloat("_Mode", 3f);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.DisableKeyword("_ALPHATEST_ON");
            m.EnableKeyword("_ALPHABLEND_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = 3000;
        }

        /// <summary>Makes a Standard material glow with the color (emission).</summary>
        public static void MakeEmissive(Material m, Color emission)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission);
        }
    }
}
