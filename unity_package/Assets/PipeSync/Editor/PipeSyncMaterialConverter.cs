using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace PipeSync.Editor
{
    /// <summary>Chemins de textures référencées par un matériau (relatifs au dossier de l'asset).</summary>
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
        public float roughness = 0.5f;
        public float alpha = 1f;
        public float[] emission_color;
        public float emission_strength;
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
    /// Crée ou met à jour un matériau URP Lit à partir des données Principled BSDF : base
    /// color (+texture), metallic/roughness (+textures combinées en Metallic/Smoothness),
    /// normal map, emission (+texture), et transparence si alpha &lt; 1.
    ///
    /// Convention choisie pour Metallic/Roughness (documentée dans docs/ARCHITECTURE.md) :
    /// dès qu'une texture metallic OU roughness existe, on génère une texture combinée
    /// (canal R = metallic, canal A = 1 - roughness) assignée à _MetallicGlossMap, qui est
    /// la convention standard attendue par le shader URP Lit. C'est l'approche la plus simple
    /// qui reste correcte visuellement, plutôt que de gérer plusieurs cas spéciaux.
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
            return File.Exists(CheminAbsolu($"{dossierMateriaux}/{nomMateriau}.mat"));
        }

        /// <summary>
        /// Vrai si toutes les ressources nécessaires à ce matériau (fichier .mat, et texture
        /// Metallic/Smoothness combinée si besoin) existent déjà sur le disque. À utiliser
        /// pendant un import pour savoir s'il faut différer la création (voir
        /// PipeSyncPostprocessor).
        /// </summary>
        public static bool RessourcesPretes(PipeSyncMaterialData donnees, string dossierAsset)
        {
            if (donnees == null || string.IsNullOrEmpty(donnees.name))
            {
                return false;
            }

            string dossierMateriaux = $"{dossierAsset}/Materials";
            if (!MateriauExisteSurDisque(dossierMateriaux, donnees.name))
            {
                return false;
            }

            if (NecessiteTextureMetallicSmoothness(donnees))
            {
                string cheminCombinee = CheminAbsolu(CheminTextureMetallicSmoothness(dossierMateriaux, donnees.name));
                if (!File.Exists(cheminCombinee))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool NecessiteTextureMetallicSmoothness(PipeSyncMaterialData donnees)
        {
            return !string.IsNullOrEmpty(donnees.textures?.metallic) || !string.IsNullOrEmpty(donnees.textures?.roughness);
        }

        private static string CheminTextureMetallicSmoothness(string dossierMateriaux, string nomMateriau)
        {
            return $"{dossierMateriaux}/{nomMateriau}_MetallicSmoothness.png";
        }

        private static string CheminAbsolu(string cheminAssets)
        {
            cheminAssets = cheminAssets.Replace("\\", "/");
            string cheminRelatif = cheminAssets.StartsWith("Assets/")
                ? cheminAssets.Substring("Assets/".Length)
                : cheminAssets;
            return Path.Combine(Application.dataPath, cheminRelatif);
        }

        private static Texture2D ChargerTexture(string dossierAsset, string cheminRelatif)
        {
            if (string.IsNullOrEmpty(cheminRelatif))
            {
                return null;
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>($"{dossierAsset}/{cheminRelatif}");
        }

        /// <summary>
        /// Crée (si besoin) ou met à jour un matériau URP Lit avec toutes ses propriétés.
        /// Appelle AssetDatabase.CreateAsset/ImportAsset si des ressources manquent encore :
        /// à n'utiliser QUE en dehors d'un import (jamais depuis OnPreprocessModel/
        /// OnPostprocessModel tant que RessourcesPretes n'a pas confirmé leur présence).
        /// </summary>
        public static Material CreerOuMettreAJourMateriau(PipeSyncMaterialData donnees, string dossierAsset)
        {
            if (donnees == null || string.IsNullOrEmpty(donnees.name))
            {
                return null;
            }

            string dossierMateriaux = $"{dossierAsset}/Materials";
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

            AppliquerBaseColorEtAlpha(materiau, donnees, dossierAsset);
            AppliquerMetallicEtSmoothness(materiau, donnees, dossierAsset, dossierMateriaux);
            AppliquerNormalMap(materiau, donnees, dossierAsset);
            AppliquerEmission(materiau, donnees, dossierAsset);

            EditorUtility.SetDirty(materiau);
            return materiau;
        }

        private static Color CouleurBase(PipeSyncMaterialData donnees)
        {
            if (donnees.base_color != null && donnees.base_color.Length >= 3)
            {
                return new Color(donnees.base_color[0], donnees.base_color[1], donnees.base_color[2], 1f);
            }
            return Color.white;
        }

        private static void AppliquerBaseColorEtAlpha(Material materiau, PipeSyncMaterialData donnees, string dossierAsset)
        {
            Texture2D baseMap = ChargerTexture(dossierAsset, donnees.textures?.base_color);
            Color baseRgb = CouleurBase(donnees);

            // Quand une texture pilote la couleur, _BaseColor sert de teinte : on la met à
            // blanc pour ne pas altérer la texture, en ne gardant que l'alpha (transparence).
            Color couleur = baseMap != null
                ? new Color(1f, 1f, 1f, donnees.alpha)
                : new Color(baseRgb.r, baseRgb.g, baseRgb.b, donnees.alpha);

            materiau.SetColor("_BaseColor", couleur);
            materiau.SetTexture("_BaseMap", baseMap);

            AppliquerSurfaceType(materiau, donnees.alpha < 0.999f);
        }

        // Simplification documentée : on se base uniquement sur la valeur scalaire "Alpha" du
        // Principled BSDF pour décider Opaque/Transparent, pas sur une analyse pixel par pixel
        // du canal alpha d'une éventuelle texture Base Color (plus coûteux, hors scope MVP).
        private static void AppliquerSurfaceType(Material materiau, bool transparent)
        {
            if (transparent)
            {
                materiau.SetOverrideTag("RenderType", "Transparent");
                materiau.SetFloat("_Surface", 1f);
                materiau.SetFloat("_Blend", 0f); // Alpha blend
                materiau.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                materiau.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                materiau.SetInt("_ZWrite", 0);
                materiau.DisableKeyword("_ALPHATEST_ON");
                materiau.EnableKeyword("_ALPHABLEND_ON");
                materiau.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                materiau.renderQueue = (int)RenderQueue.Transparent;
            }
            else
            {
                materiau.SetOverrideTag("RenderType", "Opaque");
                materiau.SetFloat("_Surface", 0f);
                materiau.SetInt("_SrcBlend", (int)BlendMode.One);
                materiau.SetInt("_DstBlend", (int)BlendMode.Zero);
                materiau.SetInt("_ZWrite", 1);
                materiau.DisableKeyword("_ALPHATEST_ON");
                materiau.DisableKeyword("_ALPHABLEND_ON");
                materiau.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                materiau.renderQueue = -1; // render queue par défaut du shader
            }
        }

        private static void AppliquerMetallicEtSmoothness(Material materiau, PipeSyncMaterialData donnees, string dossierAsset, string dossierMateriaux)
        {
            if (!NecessiteTextureMetallicSmoothness(donnees))
            {
                materiau.SetTexture("_MetallicGlossMap", null);
                materiau.DisableKeyword("_METALLICSPECGLOSSMAP");
                materiau.SetFloat("_Metallic", donnees.metallic);
                materiau.SetFloat("_Smoothness", 1f - donnees.roughness);
                return;
            }

            Texture2D combinee = ChargerOuGenererMetallicSmoothness(donnees, dossierAsset, dossierMateriaux);
            if (combinee == null)
            {
                // Secours si la génération échoue : valeurs scalaires malgré tout.
                materiau.SetTexture("_MetallicGlossMap", null);
                materiau.DisableKeyword("_METALLICSPECGLOSSMAP");
                materiau.SetFloat("_Metallic", donnees.metallic);
                materiau.SetFloat("_Smoothness", 1f - donnees.roughness);
                return;
            }

            materiau.SetTexture("_MetallicGlossMap", combinee);
            materiau.EnableKeyword("_METALLICSPECGLOSSMAP");
            materiau.SetFloat("_Metallic", 1f);
            materiau.SetFloat("_Smoothness", 1f);
            materiau.SetFloat("_GlossMapScale", 1f);
        }

        private static Texture2D ChargerOuGenererMetallicSmoothness(PipeSyncMaterialData donnees, string dossierAsset, string dossierMateriaux)
        {
            string cheminRelatif = CheminTextureMetallicSmoothness(dossierMateriaux, donnees.name);

            Texture2D existante = AssetDatabase.LoadAssetAtPath<Texture2D>(cheminRelatif);
            if (existante != null)
            {
                return existante;
            }

            Texture2D texMetallic = ChargerTexture(dossierAsset, donnees.textures?.metallic);
            Texture2D texRoughness = ChargerTexture(dossierAsset, donnees.textures?.roughness);
            RendreLisible(texMetallic);
            RendreLisible(texRoughness);

            int largeur = texMetallic != null ? texMetallic.width : (texRoughness != null ? texRoughness.width : 4);
            int hauteur = texMetallic != null ? texMetallic.height : (texRoughness != null ? texRoughness.height : 4);

            var pixels = new Color[largeur * hauteur];
            for (int y = 0; y < hauteur; y++)
            {
                float v = hauteur > 1 ? (float)y / (hauteur - 1) : 0f;
                for (int x = 0; x < largeur; x++)
                {
                    float u = largeur > 1 ? (float)x / (largeur - 1) : 0f;
                    float metallic = texMetallic != null ? texMetallic.GetPixelBilinear(u, v).r : donnees.metallic;
                    float roughness = texRoughness != null ? texRoughness.GetPixelBilinear(u, v).r : donnees.roughness;
                    pixels[y * largeur + x] = new Color(metallic, metallic, metallic, 1f - roughness);
                }
            }

            var texture = new Texture2D(largeur, hauteur, TextureFormat.RGBA32, false);
            texture.SetPixels(pixels);
            texture.Apply();
            byte[] png = texture.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(texture);

            File.WriteAllBytes(CheminAbsolu(cheminRelatif), png);
            AssetDatabase.ImportAsset(cheminRelatif, ImportAssetOptions.ForceUpdate);

            if (AssetImporter.GetAtPath(cheminRelatif) is TextureImporter importeur)
            {
                importeur.sRGBTexture = false;
                importeur.alphaSource = TextureImporterAlphaSource.FromInput;
                importeur.alphaIsTransparency = false;
                importeur.textureType = TextureImporterType.Default;
                importeur.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(cheminRelatif);
        }

        private static void RendreLisible(Texture2D texture)
        {
            if (texture == null)
            {
                return;
            }
            string chemin = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(chemin))
            {
                return;
            }
            if (AssetImporter.GetAtPath(chemin) is TextureImporter importeur && !importeur.isReadable)
            {
                importeur.isReadable = true;
                importeur.SaveAndReimport();
            }
        }

        private static void AppliquerNormalMap(Material materiau, PipeSyncMaterialData donnees, string dossierAsset)
        {
            Texture2D normalMap = ChargerTexture(dossierAsset, donnees.textures?.normal);
            materiau.SetTexture("_BumpMap", normalMap);

            if (normalMap != null)
            {
                materiau.EnableKeyword("_NORMALMAP");
            }
            else
            {
                materiau.DisableKeyword("_NORMALMAP");
            }
        }

        private static void AppliquerEmission(Material materiau, PipeSyncMaterialData donnees, string dossierAsset)
        {
            Texture2D emissionMap = ChargerTexture(dossierAsset, donnees.textures?.emission);
            Color couleurEmission = donnees.emission_color != null && donnees.emission_color.Length >= 3
                ? new Color(
                    donnees.emission_color[0] * donnees.emission_strength,
                    donnees.emission_color[1] * donnees.emission_strength,
                    donnees.emission_color[2] * donnees.emission_strength)
                : Color.black;

            materiau.SetTexture("_EmissionMap", emissionMap);
            materiau.SetColor("_EmissionColor", couleurEmission);

            bool emissionActive = emissionMap != null || couleurEmission.maxColorComponent > 0f;
            if (emissionActive)
            {
                materiau.EnableKeyword("_EMISSION");
                materiau.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                materiau.DisableKeyword("_EMISSION");
                materiau.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }
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
