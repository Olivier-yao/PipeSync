using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PipeSync.Editor
{
    /// <summary>
    /// Fenêtre Tools > PipeSync > Versions : liste les versions archivées par le
    /// service (dans "<DossierExport>/.pipesync_versions/<Nom>/") et permet de
    /// restaurer l'une d'elles dans Assets/PipeSync/<Nom>/.
    /// Note : restaurer ne modifie que la copie côté Unity — si vous ressauvegardez
    /// depuis Blender ensuite, le service écrasera à nouveau avec l'état courant du
    /// fichier .blend.
    /// </summary>
    public class PipeSyncVersionsWindow : EditorWindow
    {
        private PipeSyncSettings settings;
        private string[] nomsAssets = Array.Empty<string>();
        private int indexAssetSelectionne = -1;
        private Vector2 scroll;

        [MenuItem("Tools/PipeSync/Versions")]
        public static void Ouvrir()
        {
            var fenetre = GetWindow<PipeSyncVersionsWindow>("PipeSync Versions");
            fenetre.minSize = new Vector2(420, 300);
            fenetre.RafraichirListeAssets();
        }

        private void OnEnable()
        {
            RafraichirListeAssets();
        }

        private string DossierVersions => $"{settings.dossierExport.Replace("\\", "/").TrimEnd('/')}/.pipesync_versions";

        private void RafraichirListeAssets()
        {
            settings = PipeSyncSettings.Charger();

            if (string.IsNullOrEmpty(settings.dossierExport) || !Directory.Exists(DossierVersions))
            {
                nomsAssets = Array.Empty<string>();
                indexAssetSelectionne = -1;
                return;
            }

            nomsAssets = Directory.GetDirectories(DossierVersions)
                .Select(Path.GetFileName)
                .OrderBy(nom => nom)
                .ToArray();

            if (indexAssetSelectionne >= nomsAssets.Length)
            {
                indexAssetSelectionne = nomsAssets.Length > 0 ? 0 : -1;
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Versions PipeSync", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            if (settings == null || string.IsNullOrEmpty(settings.dossierExport))
            {
                EditorGUILayout.HelpBox(
                    "Renseignez d'abord le \"Dossier d'export Blender\" dans Tools > PipeSync > Settings.",
                    MessageType.Warning);
                if (GUILayout.Button("Ouvrir les réglages"))
                {
                    PipeSyncSettingsWindow.Ouvrir();
                }
                return;
            }

            if (GUILayout.Button("Rafraîchir", GUILayout.Width(100)))
            {
                RafraichirListeAssets();
            }

            if (nomsAssets.Length == 0)
            {
                EditorGUILayout.HelpBox("Aucun asset versionné trouvé dans " + DossierVersions, MessageType.Info);
                return;
            }

            EditorGUILayout.Space();
            indexAssetSelectionne = EditorGUILayout.Popup("Asset", indexAssetSelectionne, nomsAssets);

            if (indexAssetSelectionne < 0 || indexAssetSelectionne >= nomsAssets.Length)
            {
                return;
            }

            string nomAsset = nomsAssets[indexAssetSelectionne];
            string dossierAssetVersions = $"{DossierVersions}/{nomAsset}";

            string[] versions = Directory.GetDirectories(dossierAssetVersions)
                .Select(Path.GetFileName)
                .OrderByDescending(nom => nom)
                .ToArray();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"{versions.Length} version(s) archivée(s)", EditorStyles.miniBoldLabel);

            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (string horodatage in versions)
            {
                EditorGUILayout.BeginHorizontal("box");
                EditorGUILayout.LabelField(FormaterHorodatage(horodatage));
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Restaurer", GUILayout.Width(90)))
                {
                    bool confirme = EditorUtility.DisplayDialog(
                        "Restaurer cette version ?",
                        $"Remplacer l'asset actuel '{nomAsset}' par la version du {FormaterHorodatage(horodatage)} ?\n\n" +
                        $"Ceci écrase le FBX et les textures actuels dans {settings.dossierSurveille}/{nomAsset}/.",
                        "Restaurer", "Annuler");
                    if (confirme)
                    {
                        Restaurer(nomAsset, horodatage);
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }

        private static string FormaterHorodatage(string horodatage)
        {
            // Format écrit par le service Python : yyyyMMdd-HHmmss
            if (DateTime.TryParseExact(horodatage, "yyyyMMdd-HHmmss", null,
                    System.Globalization.DateTimeStyles.None, out DateTime date))
            {
                return date.ToString("dd/MM/yyyy HH:mm:ss");
            }
            return horodatage;
        }

        private void Restaurer(string nomAsset, string horodatage)
        {
            string dossierVersionAbsolu = $"{DossierVersions}/{nomAsset}/{horodatage}";
            string dossierDestinationAssets = $"{settings.dossierSurveille}/{nomAsset}";

            try
            {
                CopierRecursivement(dossierVersionAbsolu, dossierDestinationAssets);
            }
            catch (Exception erreur)
            {
                Debug.LogError($"[PipeSync] Échec de la restauration de '{nomAsset}' : {erreur.Message}");
                return;
            }

            AssetDatabase.Refresh();

            string cheminFbx = $"{dossierDestinationAssets}/{nomAsset}.fbx";
            AssetDatabase.ImportAsset(cheminFbx, ImportAssetOptions.ForceUpdate);

            Debug.Log($"[PipeSync] '{nomAsset}' restauré à la version du {FormaterHorodatage(horodatage)}.");
            if (SceneView.lastActiveSceneView != null)
            {
                SceneView.lastActiveSceneView.ShowNotification(new GUIContent($"PipeSync : '{nomAsset}' restauré"));
            }
        }

        /// <summary>Copie tous les fichiers d'un dossier absolu vers un dossier Assets/ (créé si besoin).</summary>
        private static void CopierRecursivement(string dossierSourceAbsolu, string dossierDestAssets)
        {
            string racineProjet = Path.GetDirectoryName(Application.dataPath);
            string destAbsolue = Path.Combine(racineProjet, dossierDestAssets.Replace("/", "\\"));

            Directory.CreateDirectory(destAbsolue);

            foreach (string fichier in Directory.GetFiles(dossierSourceAbsolu, "*", SearchOption.AllDirectories))
            {
                string relatif = fichier.Substring(dossierSourceAbsolu.Length).TrimStart('\\', '/');
                string destinationFichier = Path.Combine(destAbsolue, relatif);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationFichier));
                File.Copy(fichier, destinationFichier, overwrite: true);
            }
        }
    }
}
