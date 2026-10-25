using System;
using UnityEngine;

namespace Megaphonk.Helpers
{
    /// <summary>
    /// Materials seen through walls: drawn over everything (rift zones), or only where something hides them (teammates'
    /// silhouettes). Built on a shader that lets its depth test be changed, whichever the game includes.
    /// </summary>
    internal static class XRayMaterials
    {
        private const float ZTEST_GREATER = 5f; //Only behind something
        private const float ZTEST_ALWAYS = 8f;
        private const float BLEND_SRC_ALPHA = 5f;
        private const float BLEND_ONE_MINUS_SRC_ALPHA = 10f;
        private const int OVERLAY_QUEUE = 4000;

        private static Material onTop;
        private static bool isUnavailable;

        /// <summary>
        /// Vertex colored (LineRenderer colors), drawn over walls at any distance. Null if no suitable shader.
        /// </summary>
        public static Material OnTop()
        {
            if (onTop == null && !isUnavailable)
            {
                onTop = Create("Megaphonk_OnTop", ZTEST_ALWAYS, Color.white);
            }
            return onTop;
        }

        /// <summary>
        /// A flat color drawn only where the mesh is hidden (a silhouette behind walls). Null if no suitable shader.
        /// </summary>
        public static Material Occluded(Color color)
        {
            return isUnavailable ? null : Create("Megaphonk_XRay", ZTEST_GREATER, color);
        }

        private static Material Create(string name, float zTest, Color color)
        {
            try
            {
                Material material;
                var shader = Shader.Find("Hidden/Internal-Colored");
                if (shader != null)
                {
                    material = new Material(shader);
                    material.SetFloat("_ZTest", zTest);
                    material.SetFloat("_ZWrite", 0f);
                    material.SetFloat("_Cull", 0f);
                    material.SetFloat("_SrcBlend", BLEND_SRC_ALPHA);
                    material.SetFloat("_DstBlend", BLEND_ONE_MINUS_SRC_ALPHA);
                }
                else
                {
                    shader = Shader.Find("UI/Default");
                    if (shader == null)
                    {
                        isUnavailable = true;
                        Plugin.Log.LogWarning("No shader found for the through-walls markers");
                        return null;
                    }
                    material = new Material(shader);
                    material.SetFloat("unity_GUIZTestMode", zTest);
                }

                material.name = name;
                material.color = color;
                material.renderQueue = OVERLAY_QUEUE;
                return material;
            }
            catch (Exception ex)
            {
                isUnavailable = true;
                Plugin.Log.LogError($"Through-walls material failed: {ex}");
                return null;
            }
        }
    }
}
