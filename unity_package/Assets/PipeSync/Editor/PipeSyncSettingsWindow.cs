using UnityEditor;
using UnityEngine;

namespace PipeSync.Editor
{
    /// <summary>
    /// Réglages PipeSync stockés dans EditorPrefs (scopés par projet via le
    /// hash du dossier Assets, pour éviter les collisions entre projets).
    /// </summary>
    public class PipeSyncSettings
    {
        public bool activer = true;
        public string dossierSurveille = "Assets/PipeSync";
        public bool importerBlendShapes = true;
        // Dossier d'export Blender (sur le disque, en dehors du projet Unity) : le même que
        // "export_dir" dans pipesync_config.json. Sert à localiser .pipesync_versions/ pour
        // la fenêtre Tools > PipeSync > Versions.
        public string dossierExport = "";

        private const string CLE_ACTIVER = "PipeSync.Activer";
        private const string CLE_DOSSIER = "PipeSync.DossierSurveille";
        private const string CLE_BLENDSHAPES = "PipeSync.ImporterBlendShapes";
        private const string CLE_DOSSIER_EXPORT = "PipeSync.DossierExport";

        private static string CleProjet(string cleBase)
        {
            return $"{cleBase}.{Application.dataPath.GetHashCode()}";
        }

        public static PipeSyncSettings Charger()
        {
            return new PipeSyncSettings
            {
                activer = EditorPrefs.GetBool(CleProjet(CLE_ACTIVER), true),
                dossierSurveille = EditorPrefs.GetString(CleProjet(CLE_DOSSIER), "Assets/PipeSync"),
                importerBlendShapes = EditorPrefs.GetBool(CleProjet(CLE_BLENDSHAPES), true),
                dossierExport = EditorPrefs.GetString(CleProjet(CLE_DOSSIER_EXPORT), ""),
            };
        }

        public void Sauvegarder()
        {
            EditorPrefs.SetBool(CleProjet(CLE_ACTIVER), activer);
            EditorPrefs.SetString(CleProjet(CLE_DOSSIER), dossierSurveille);
            EditorPrefs.SetBool(CleProjet(CLE_BLENDSHAPES), importerBlendShapes);
            EditorPrefs.SetString(CleProjet(CLE_DOSSIER_EXPORT), dossierExport);
        }
    }

    /// <summary>Fenêtre Tools > PipeSync > Settings.</summary>
    public class PipeSyncSettingsWindow : EditorWindow
    {
        private PipeSyncSettings settings;

        [MenuItem("Tools/PipeSync/Settings")]
        public static void Ouvrir()
        {
            var fenetre = GetWindow<PipeSyncSettingsWindow>("PipeSync");
            fenetre.settings = PipeSyncSettings.Charger();
            fenetre.minSize = new Vector2(360, 160);
        }

        private void OnEnable()
        {
            if (settings == null)
            {
                settings = PipeSyncSettings.Charger();
            }
        }

        private void OnGUI()
        {
            if (settings == null)
            {
                settings = PipeSyncSettings.Charger();
            }

            EditorGUILayout.LabelField("PipeSync", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            EditorGUI.BeginChangeCheck();

            settings.activer = EditorGUILayout.Toggle("Activer PipeSync", settings.activer);

            EditorGUILayout.BeginHorizontal();
            settings.dossierSurveille = EditorGUILayout.TextField("Dossier surveillé", settings.dossierSurveille);
            if (GUILayout.Button("Parcourir...", GUILayout.Width(90)))
            {
                string chemin = EditorUtility.OpenFolderPanel("Choisir le dossier PipeSync", Application.dataPath, "");
                if (!string.IsNullOrEmpty(chemin))
                {
                    string cheminNormalise = chemin.Replace("\\", "/");
                    string dataPathNormalise = Application.dataPath.Replace("\\", "/");
                    if (cheminNormalise.StartsWith(dataPathNormalise))
                    {
                        settings.dossierSurveille = "Assets" + cheminNormalise.Substring(dataPathNormalise.Length);
                    }
                    else
                    {
                        Debug.LogWarning("[PipeSync] Le dossier choisi doit être à l'intérieur du dossier Assets du projet.");
                    }
                }
            }
            EditorGUILayout.EndHorizontal();

            settings.importerBlendShapes = EditorGUILayout.Toggle("Importer les Blend Shapes", settings.importerBlendShapes);

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            settings.dossierExport = EditorGUILayout.TextField(
                new GUIContent("Dossier d'export Blender", "Le même dossier que 'export_dir' dans pipesync_config.json. Utilisé par Tools > PipeSync > Versions pour retrouver l'historique."),
                settings.dossierExport);
            if (GUILayout.Button("Parcourir...", GUILayout.Width(90)))
            {
                string chemin = EditorUtility.OpenFolderPanel("Choisir le dossier d'export Blender", settings.dossierExport, "");
                if (!string.IsNullOrEmpty(chemin))
                {
                    settings.dossierExport = chemin.Replace("\\", "/");
                }
            }
            EditorGUILayout.EndHorizontal();

            if (EditorGUI.EndChangeCheck())
            {
                settings.Sauvegarder();
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "PipeSync applique automatiquement les réglages d'import (échelle, axes) " +
                "et convertit les matériaux en URP Lit pour les modèles importés dans le dossier surveillé.",
                MessageType.Info);
        }
    }
}
