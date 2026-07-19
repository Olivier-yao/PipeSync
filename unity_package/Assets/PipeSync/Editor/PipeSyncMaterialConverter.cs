using System;
using UnityEditor;
using UnityEngine;

namespace PipeSync.Editor
{
    /// <summary>Chemins de textures référencées par un matériau (relatifs au dossier d'export).</summary>
    [Serializable]
    public class PipeSyncTextures
    {
        public string base_color;
        public string normal;
        public string roughness;
        public string metallic;
        public string emission;
    }

    /// <summary>Description d'un matériau Principled BSDF, telle qu'écrite par l'add-on Blender.</summary>
    [Serializable]
    public class PipeSyncMaterialData
    {
        public string name;
        public float[] base_color;
        public float metallic;
        public float roughness;
        public PipeSyncTextures textures;
    }

    /// <summary>Contenu du fichier compagnon &lt;nom&gt;.pipesync.json.</summary>
    [Serializable]
    public class PipeSyncManifest
    {
        public string timestamp;
        public PipeSyncMaterialData[] materials;
    }

    /// <summary>
    /// Crée ou met à jour un matériau URP Lit à partir des données Principled BSDF.
    /// Phase 1 : seule la base color est appliquée (le mapping complet arrive en Phase 2).
    /// </summary>
    public static class PipeSyncMaterialConverter
    {
        private const string NOM_SHADER_URP_LIT = "Universal Render Pipeline/Lit";

        public static Material CreerOuMettreAJourMateriau(PipeSyncMaterialData donnees, string dossierMateriaux)
        {
            if (donnees == null || string.IsNullOrEmpty(donnees.name))
            {
                return null;
            }

            CreerDossiersRecursif(dossierMateriaux);
            string cheminMateriau = $"{dossierMateriaux}/{donnees.name}.mat";
            Material materiau = AssetDatabase.LoadAssetAtPath<Material>(cheminMateriau);

            if (materiau == null)
            {
                Shader shaderUrpLit = Shader.Find(NOM_SHADER_URP_LIT);
                if (shaderUrpLit == null)
                {
                    Debug.LogWarning(
                        $"[PipeSync] Shader '{NOM_SHADER_URP_LIT}' introuvable (le package URP est-il installé ?). " +
                        $"Matériau '{donnees.name}' non créé.");
                    return null;
                }

                materiau = new Material(shaderUrpLit);
                AssetDatabase.CreateAsset(materiau, cheminMateriau);
            }

            if (donnees.base_color != null && donnees.base_color.Length >= 4)
            {
                Color couleur = new Color(
                    donnees.base_color[0],
                    donnees.base_color[1],
                    donnees.base_color[2],
                    donnees.base_color[3]);
                materiau.SetColor("_BaseColor", couleur);
            }

            EditorUtility.SetDirty(materiau);
            return materiau;
        }

        private static void CreerDossiersRecursif(string chemin)
        {
            chemin = chemin.Replace("\\", "/");
            if (AssetDatabase.IsValidFolder(chemin))
            {
                return;
            }

            string[] segments = chemin.Split('/');
            string courant = segments[0]; // "Assets"
            for (int i = 1; i < segments.Length; i++)
            {
                string suivant = $"{courant}/{segments[i]}";
                if (!AssetDatabase.IsValidFolder(suivant))
                {
                    AssetDatabase.CreateFolder(courant, segments[i]);
                }
                courant = suivant;
            }
        }
    }
}
