using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PipeSync.Editor
{
    /// <summary>
    /// Détecte les FBX importés/mis à jour par le service PipeSync, force les
    /// réglages d'import corrects et remappe les matériaux vers des matériaux
    /// URP Lit générés à partir du fichier .pipesync.json. Configure aussi
    /// automatiquement les réglages d'import des textures référencées (sRGB,
    /// Normal Map).
    /// Règle absolue : ne jamais toucher aux fichiers .meta, ne jamais
    /// supprimer/recréer d'assets — seuls les FBX sont écrasés en place par
    /// le service, ce qui préserve le GUID et les références des prefabs.
    /// </summary>
    public class PipeSyncPostprocessor : AssetPostprocessor
    {
        // Unity interdit d'appeler AssetDatabase.CreateAsset pendant un import
        // (OnPreprocessModel/OnPostprocessModel). Quand une ressource (matériau ou texture
        // Metallic/Smoothness générée) manque encore sur le disque, on mémorise l'asset ici et
        // on la crée juste après l'import, via EditorApplication.delayCall. Le remap se fera
        // alors à la prochaine synchronisation naturelle (elle sera déjà présente sur le disque).
        private static readonly HashSet<string> assetsEnAttenteDeMateriaux = new HashSet<string>();

        // Regroupe les sauvegardes de matériaux mis à jour pendant un import (une seule
        // AssetDatabase.SaveAssets() après coup, plutôt qu'une pendant l'import).
        private static bool sauvegardeMateriauxPlanifiee;

        private bool DansDossierSurveille(string chemin, PipeSyncSettings settings)
        {
            chemin = chemin.Replace("\\", "/");
            string dossier = settings.dossierSurveille.Replace("\\", "/").TrimEnd('/');
            return chemin.StartsWith(dossier + "/");
        }

        /// <summary>Lit le .pipesync.json à côté de assetPath. dossierAsset reçoit le dossier
        /// racine de l'asset (celui qui contient Materials/ et Textures/).</summary>
        private PipeSyncManifest LireManifest(out string dossierAsset)
        {
            dossierAsset = Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
            string nomAsset = Path.GetFileNameWithoutExtension(assetPath);
            if (string.IsNullOrEmpty(dossierAsset))
            {
                return null;
            }

            string cheminJson = $"{dossierAsset}/{nomAsset}.pipesync.json";
            if (!File.Exists(cheminJson))
            {
                return null;
            }

            try
            {
                return JsonUtility.FromJson<PipeSyncManifest>(File.ReadAllText(cheminJson));
            }
            catch (Exception erreur)
            {
                Debug.LogError($"[PipeSync] Impossible de lire {cheminJson} : {erreur.Message}");
                return null;
            }
        }

        private void OnPreprocessModel()
        {
            PipeSyncSettings settings = PipeSyncSettings.Charger();
            if (!settings.activer || !DansDossierSurveille(assetPath, settings))
            {
                return;
            }

            ModelImporter importeur = assetImporter as ModelImporter;
            if (importeur == null)
            {
                return;
            }

            importeur.globalScale = 1f;      // Scale Factor = 1
            importeur.useFileScale = false;  // Convert Units = off
            importeur.importBlendShapes = settings.importerBlendShapes;

            // Remappe vers les matériaux PipeSync déjà présents sur le disque (générés lors
            // d'une synchronisation précédente). C'est la voie "officielle" Unity pour
            // substituer des matériaux à l'import, contrairement à une réassignation manuelle
            // des Renderer après coup, qui peut faire échouer la vérification de déterminisme
            // de l'import ("Importer generated inconsistent result").
            PipeSyncManifest manifest = LireManifest(out string dossierAsset);
            if (manifest?.materials == null)
            {
                return;
            }

            bool auMoinsUnMateriauMisAJour = false;

            foreach (PipeSyncMaterialData donneesMateriau in manifest.materials)
            {
                if (!PipeSyncMaterialConverter.RessourcesPretes(donneesMateriau, dossierAsset))
                {
                    continue; // pas encore créé : voir OnPostprocessModel
                }

                // Toutes les ressources existent déjà : on ne crée rien (CreateAsset resterait
                // interdit ici), mais on met à jour les propriétés du matériau (couleur,
                // metallic, etc.) avec les dernières valeurs du JSON — sinon un changement dans
                // Blender ne serait jamais répercuté une fois le matériau créé une première fois.
                Material materiau = PipeSyncMaterialConverter.CreerOuMettreAJourMateriau(donneesMateriau, dossierAsset);
                if (materiau == null)
                {
                    continue;
                }

                auMoinsUnMateriauMisAJour = true;
                var identifiant = new AssetImporter.SourceAssetIdentifier(typeof(Material), donneesMateriau.name);
                importeur.AddRemap(identifiant, materiau);
            }

            if (auMoinsUnMateriauMisAJour)
            {
                PlanifierSauvegardeMateriaux();
            }
        }

        private static void PlanifierSauvegardeMateriaux()
        {
            if (sauvegardeMateriauxPlanifiee)
            {
                return;
            }
            sauvegardeMateriauxPlanifiee = true;

            EditorApplication.delayCall += () =>
            {
                sauvegardeMateriauxPlanifiee = false;
                AssetDatabase.SaveAssets();
            };
        }

        private void OnPostprocessModel(GameObject racineImportee)
        {
            PipeSyncSettings settings = PipeSyncSettings.Charger();
            if (!settings.activer || !DansDossierSurveille(assetPath, settings))
            {
                return;
            }

            PipeSyncManifest manifest = LireManifest(out string dossierAsset);
            if (manifest?.materials == null || manifest.materials.Length == 0)
            {
                return;
            }

            bool ilManqueDesRessources = false;
            foreach (PipeSyncMaterialData donneesMateriau in manifest.materials)
            {
                if (!PipeSyncMaterialConverter.RessourcesPretes(donneesMateriau, dossierAsset))
                {
                    ilManqueDesRessources = true;
                    break;
                }
            }

            string nomAsset = Path.GetFileNameWithoutExtension(assetPath);

            if (ilManqueDesRessources)
            {
                PlanifierCreationMateriaux(assetPath, dossierAsset, manifest);
                return;
            }

            Debug.Log($"[PipeSync] '{nomAsset}' importé : {manifest.materials.Length} matériau(x) remappé(s).");
        }

        private static void PlanifierCreationMateriaux(string cheminAsset, string dossierAsset, PipeSyncManifest manifest)
        {
            if (!assetsEnAttenteDeMateriaux.Add(cheminAsset))
            {
                return; // déjà planifié
            }

            EditorApplication.delayCall += () =>
            {
                assetsEnAttenteDeMateriaux.Remove(cheminAsset);

                foreach (PipeSyncMaterialData donneesMateriau in manifest.materials)
                {
                    PipeSyncMaterialConverter.CreerOuMettreAJourMateriau(donneesMateriau, dossierAsset);
                }
                AssetDatabase.SaveAssets();

                Debug.Log($"[PipeSync] Ressource(s) créée(s) pour '{Path.GetFileNameWithoutExtension(cheminAsset)}'. " +
                          "Elles seront remappées à la prochaine synchronisation (ou via Reimport manuel).");
            };
        }

        // ------------------------------------------------------------------
        // Réglages d'import automatiques des textures (sRGB, type Normal Map)
        // ------------------------------------------------------------------

        private void OnPreprocessTexture()
        {
            PipeSyncSettings settings = PipeSyncSettings.Charger();
            if (!settings.activer)
            {
                return;
            }

            string chemin = assetPath.Replace("\\", "/");
            const string suffixeTextures = "/Textures";
            int indexTextures = chemin.LastIndexOf(suffixeTextures + "/", StringComparison.Ordinal);
            if (indexTextures < 0)
            {
                return; // pas une texture copiée par PipeSync (pas dans un sous-dossier Textures/)
            }

            string dossierAsset = chemin.Substring(0, indexTextures);
            if (!DansDossierSurveille(dossierAsset, settings))
            {
                return;
            }

            string nomAsset = Path.GetFileName(dossierAsset);
            string cheminJson = $"{dossierAsset}/{nomAsset}.pipesync.json";
            if (!File.Exists(cheminJson))
            {
                return;
            }

            PipeSyncManifest manifest;
            try
            {
                manifest = JsonUtility.FromJson<PipeSyncManifest>(File.ReadAllText(cheminJson));
            }
            catch (Exception)
            {
                return;
            }

            if (manifest?.materials == null)
            {
                return;
            }

            TextureImporter importeurTexture = assetImporter as TextureImporter;
            if (importeurTexture == null)
            {
                return;
            }

            string nomFichier = Path.GetFileName(chemin);

            foreach (PipeSyncMaterialData donneesMateriau in manifest.materials)
            {
                if (donneesMateriau.textures == null)
                {
                    continue;
                }

                if (CorrespondNomFichier(donneesMateriau.textures.normal, nomFichier))
                {
                    importeurTexture.textureType = TextureImporterType.NormalMap;
                    return;
                }

                if (CorrespondNomFichier(donneesMateriau.textures.metallic, nomFichier) ||
                    CorrespondNomFichier(donneesMateriau.textures.roughness, nomFichier))
                {
                    // Ce sont des données (pas de la couleur) : la conversion sRGB fausserait
                    // les valeurs de metallic/roughness lues plus tard pour générer la texture
                    // Metallic/Smoothness combinée.
                    importeurTexture.sRGBTexture = false;
                    return;
                }

                // base_color / emission : on garde le comportement par défaut (sRGB = true),
                // ce sont des textures de couleur.
            }
        }

        private static bool CorrespondNomFichier(string cheminRelatif, string nomFichier)
        {
            return !string.IsNullOrEmpty(cheminRelatif) && Path.GetFileName(cheminRelatif) == nomFichier;
        }
    }
}
