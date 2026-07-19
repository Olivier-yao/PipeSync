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
    /// URP Lit générés à partir du fichier .pipesync.json.
    /// Règle absolue : ne jamais toucher aux fichiers .meta, ne jamais
    /// supprimer/recréer d'assets — seuls les FBX sont écrasés en place par
    /// le service, ce qui préserve le GUID et les références des prefabs.
    /// </summary>
    public class PipeSyncPostprocessor : AssetPostprocessor
    {
        // Unity interdit d'appeler AssetDatabase.CreateAsset pendant un import
        // (OnPreprocessModel/OnPostprocessModel). Quand un matériau manque encore sur le
        // disque, on mémorise l'asset ici et on le crée juste après l'import, via
        // EditorApplication.delayCall. Le remap vers ce matériau se fera alors à la
        // prochaine synchronisation naturelle (il sera déjà présent sur le disque).
        private static readonly HashSet<string> assetsEnAttenteDeMateriaux = new HashSet<string>();

        // Regroupe les sauvegardes de matériaux mis à jour pendant un import (une seule
        // AssetDatabase.SaveAssets() après coup, plutôt qu'une pendant l'import).
        private static bool sauvegardeMateriauxPlanifiee;

        private bool DansDossierSurveille(PipeSyncSettings settings)
        {
            string chemin = assetPath.Replace("\\", "/");
            string dossier = settings.dossierSurveille.Replace("\\", "/").TrimEnd('/');
            return chemin.StartsWith(dossier + "/");
        }

        private PipeSyncManifest LireManifest(out string dossierMateriaux)
        {
            dossierMateriaux = null;

            string dossier = Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
            string nomAsset = Path.GetFileNameWithoutExtension(assetPath);
            if (string.IsNullOrEmpty(dossier))
            {
                return null;
            }

            string cheminJson = $"{dossier}/{nomAsset}.pipesync.json";
            if (!File.Exists(cheminJson))
            {
                return null;
            }

            try
            {
                dossierMateriaux = $"{dossier}/Materials";
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
            if (!settings.activer || !DansDossierSurveille(settings))
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
            PipeSyncManifest manifest = LireManifest(out string dossierMateriaux);
            if (manifest?.materials == null)
            {
                return;
            }

            foreach (PipeSyncMaterialData donneesMateriau in manifest.materials)
            {
                if (string.IsNullOrEmpty(donneesMateriau.name) ||
                    !PipeSyncMaterialConverter.MateriauExisteSurDisque(dossierMateriaux, donneesMateriau.name))
                {
                    continue; // pas encore créé : voir OnPostprocessModel
                }

                Material materiau = AssetDatabase.LoadAssetAtPath<Material>($"{dossierMateriaux}/{donneesMateriau.name}.mat");
                if (materiau == null)
                {
                    continue;
                }

                var identifiant = new AssetImporter.SourceAssetIdentifier(typeof(Material), donneesMateriau.name);
                importeur.AddRemap(identifiant, materiau);
            }
        }

        private void OnPostprocessModel(GameObject racineImportee)
        {
            PipeSyncSettings settings = PipeSyncSettings.Charger();
            if (!settings.activer || !DansDossierSurveille(settings))
            {
                return;
            }

            PipeSyncManifest manifest = LireManifest(out string dossierMateriaux);
            if (manifest?.materials == null || manifest.materials.Length == 0)
            {
                return;
            }

            bool ilManqueUnMateriau = false;
            foreach (PipeSyncMaterialData donneesMateriau in manifest.materials)
            {
                if (!PipeSyncMaterialConverter.MateriauExisteSurDisque(dossierMateriaux, donneesMateriau.name))
                {
                    ilManqueUnMateriau = true;
                    break;
                }
            }

            string nomAsset = Path.GetFileNameWithoutExtension(assetPath);

            if (ilManqueUnMateriau)
            {
                PlanifierCreationMateriaux(assetPath, dossierMateriaux, manifest);
                return;
            }

            Debug.Log($"[PipeSync] '{nomAsset}' importé : {manifest.materials.Length} matériau(x) remappé(s).");
        }

        private static void PlanifierCreationMateriaux(string cheminAsset, string dossierMateriaux, PipeSyncManifest manifest)
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
                    PipeSyncMaterialConverter.CreerOuMettreAJourMateriau(donneesMateriau, dossierMateriaux);
                }
                AssetDatabase.SaveAssets();

                Debug.Log($"[PipeSync] Matériau(x) créé(s) pour '{Path.GetFileNameWithoutExtension(cheminAsset)}'. " +
                          "Ils seront remappés à la prochaine synchronisation (ou via Reimport manuel).");
            };
        }
    }
}
