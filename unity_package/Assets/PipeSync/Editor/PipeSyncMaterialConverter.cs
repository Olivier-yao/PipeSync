using System;
using System.IO;
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

        /// <summary>
        /// Vérifie sur le disque (pas via AssetDatabase, non fiable pendant un import) si le
        /// fichier .mat d'un matériau existe déjà.
        /// </summary>
        public static bool MateriauExisteSurDisque(string dossierMateriaux, string nomMateriau)
        {
            if (string.IsNullOrEmpty(nomMateriau))
            {
                return false;
            }
            string cheminAbsolu = CheminAbsolu($"{dossierMateriaux}/{nomMateriau}.mat");
            return File.Exists(cheminAbsolu);
        }

        private static string CheminAbsolu(string cheminAssets)
        {
            cheminAssets = cheminAssets.Replace("\\", "/");
            string cheminRelatif = cheminAssets.StartsWith("Assets/")
                ? cheminAssets.Substring("Assets/".Length)
                : cheminAssets;
            return Path.Combine(Application.dataPath, cheminRelatif);
        }

        /// <summary>
        /// Crée (si besoin) ou met à jour un matériau URP Lit. Appelle AssetDatabase.CreateAsset
        /// si le matériau n'existe pas encore : à n'utiliser QUE en dehors d'un import (jamais
        /// depuis OnPreprocessModel/OnPostprocessModel, Unity l'interdit). Pour l'usage pendant
        /// un import, voir <see cref="MateriauExisteSurDisque"/> pour vérifier avant d'appeler.
        /// </summary>
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

        private static void CreerDossiersRecursif(string cheminAssets)
        {
            // AssetDatabase.IsValidFolder/CreateFolder ne sont pas fiables quand on les
            // appelle pendant un import (OnPostprocessModel) : sur des imports successifs,
            // le dossier fraîchement créé n'est pas toujours "vu" tout de suite, ce qui
            // provoquait la création de dossiers en double ("Materials", "Materials 1", ...).
            // On passe donc par le système de fichiers directement, qui est immédiat.
            string cheminAbsolu = CheminAbsolu(cheminAssets);

            if (Directory.Exists(cheminAbsolu))
            {
                return;
            }

            Directory.CreateDirectory(cheminAbsolu);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }
    }
}
