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

        private const string CLE_ACTIVER = "PipeSync.Activer";
        private const string CLE_DOSSIER = "PipeSync.DossierSurveille";
        private const string CLE_BLENDSHAPES = "PipeSync.ImporterBlendShapes";

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
            };
        }

        public void Sauvegarder()
        {
            EditorPrefs.SetBool(CleProjet(CLE_ACTIVER), activer);
            EditorPrefs.SetString(CleProjet(CLE_DOSSIER), dossierSurveille);
            EditorPrefs.SetBool(CleProjet(CLE_BLENDSHAPES), importerBlendShapes);
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
