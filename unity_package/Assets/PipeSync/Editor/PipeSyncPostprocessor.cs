using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PipeSync.Editor
{
    /// <summary>
    /// Détecte les FBX importés/mis à jour par le service PipeSync, force les
    /// réglages d'import corrects et convertit les matériaux en URP Lit.
    /// Règle absolue : ne jamais toucher aux fichiers .meta, ne jamais
    /// supprimer/recréer d'assets — seuls les FBX sont écrasés en place par
    /// le service, ce qui préserve le GUID et les références des prefabs.
    /// </summary>
    public class PipeSyncPostprocessor : AssetPostprocessor
    {
        private bool DansDossierSurveille(PipeSyncSettings settings)
        {
            string chemin = assetPath.Replace("\\", "/");
            string dossier = settings.dossierSurveille.Replace("\\", "/").TrimEnd('/');
            return chemin.StartsWith(dossier + "/");
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
        }

        private void OnPostprocessModel(GameObject racineImportee)
        {
            PipeSyncSettings settings = PipeSyncSettings.Charger();
            if (!settings.activer || !DansDossierSurveille(settings))
            {
                return;
            }

            string dossier = Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
            string nomAsset = Path.GetFileNameWithoutExtension(assetPath);
            if (string.IsNullOrEmpty(dossier))
            {
                return;
            }

            string cheminJson = $"{dossier}/{nomAsset}.pipesync.json";
            if (!File.Exists(cheminJson))
            {
                Debug.LogWarning($"[PipeSync] Aucun fichier {nomAsset}.pipesync.json trouvé à côté du FBX, matériaux non convertis.");
                return;
            }

            PipeSyncManifest manifest;
            try
            {
                manifest = JsonUtility.FromJson<PipeSyncManifest>(File.ReadAllText(cheminJson));
            }
            catch (Exception erreur)
            {
                Debug.LogError($"[PipeSync] Impossible de lire {cheminJson} : {erreur.Message}");
                return;
            }

            if (manifest?.materials == null)
            {
                return;
            }

            string dossierMateriaux = $"{dossier}/Materials";
            Renderer[] renderers = racineImportee.GetComponentsInChildren<Renderer>(true);

            foreach (PipeSyncMaterialData donneesMateriau in manifest.materials)
            {
                Material materiau = PipeSyncMaterialConverter.CreerOuMettreAJourMateriau(donneesMateriau, dossierMateriaux);
                if (materiau == null)
                {
                    continue;
                }

                AssignerMateriau(renderers, donneesMateriau.name, materiau);
            }

            Debug.Log($"[PipeSync] '{nomAsset}' importé : {manifest.materials.Length} matériau(x) synchronisé(s).");
        }

        private static void AssignerMateriau(Renderer[] renderers, string nomMateriau, Material materiau)
        {
            foreach (Renderer renderer in renderers)
            {
                Material[] partages = renderer.sharedMaterials;
                bool modifie = false;

                for (int i = 0; i < partages.Length; i++)
                {
                    string nomActuel = partages[i] != null ? partages[i].name.Replace(" (Instance)", "") : null;
                    if (nomActuel == nomMateriau)
                    {
                        partages[i] = materiau;
                        modifie = true;
                    }
                }

                if (modifie)
                {
                    renderer.sharedMaterials = partages;
                }
            }
        }
    }
}
