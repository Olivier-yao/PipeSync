# PipeSync

Synchronisation automatique Blender → Unity : à chaque sauvegarde d'un
`.blend`, l'asset (FBX + matériaux) apparaît dans le projet Unity, à la
bonne échelle, sans casser les prefabs existants.

**Phase 1 (MVP)** validée : export automatique, synchronisation FBX,
échelle, versioning, prefabs stables.
**Phase 2** ajoutée : mapping complet des matériaux (textures Base Map,
Normal Map, Metallic/Roughness, Emission, transparence).

Ce guide suppose que vous n'avez **jamais** installé d'add-on Blender ni de
package Unity — chaque étape est détaillée.

## Prérequis

- **Blender 4.x**
- **Unity 2022.3 LTS** ou plus récent, avec le pipeline **URP** (Universal
  Render Pipeline) déjà configuré sur le projet
- **Python 3.9+** installé sur Windows (pour lancer le service PipeSync).
  Vérifiez avec `python --version` dans un terminal. Si la commande échoue,
  installez Python depuis [python.org](https://www.python.org/downloads/)
  en cochant "Add python.exe to PATH" pendant l'installation.

Il y a 3 composants à installer, dans cet ordre : l'add-on Blender, le
service, puis le package Unity.

---

## 1. Installer l'add-on Blender

1. Ouvrez Blender.
2. Menu **Edit > Preferences...** (ou **Édition > Préférences...**).
3. Dans la fenêtre de préférences, cliquez sur l'onglet **Add-ons**.
4. En haut à droite, cliquez sur **Install...** (ou l'icône flèche vers le
   bas selon la version).
5. Naviguez jusqu'au fichier
   [`blender_addon/pipesync_addon.py`](blender_addon/pipesync_addon.py) de
   ce dépôt, sélectionnez-le, cliquez sur **Install Add-on**.
6. L'add-on **PipeSync** apparaît dans la liste. Cochez la case à gauche de
   son nom pour l'activer.
7. Fermez la fenêtre de préférences.

### Utiliser le panneau PipeSync

1. Dans la vue 3D, ouvrez le panneau latéral (touche **N** si ce n'est pas
   déjà visible).
2. Un onglet **PipeSync** apparaît sur le bord droit de la vue 3D — cliquez
   dessus.
3. Renseignez le **dossier d'export** : un dossier local quelconque, par
   exemple `C:\PipeSync\export`. Ce sera le point de rendez-vous avec le
   service (étape 2).
4. Cochez **Export auto à la sauvegarde** si vous voulez que chaque `Ctrl+S`
   déclenche un export automatique. Sinon, utilisez le bouton **Exporter
   maintenant** quand vous le souhaitez.
5. Sauvegardez votre fichier `.blend` (`Ctrl+S`) au moins une fois — l'export
   a besoin que le fichier ait déjà un emplacement sur le disque.

*Astuce :* si vous créez une **collection** nommée exactement `Export`,
seuls les objets de cette collection seront exportés. Sinon, tous les objets
visibles de la scène le sont.

### Tester ce composant seul

- Sauvegardez le fichier `.blend` (ou cliquez sur **Exporter maintenant**).
- Vérifiez dans le dossier d'export que deux fichiers sont apparus :
  `<nom_du_blend>.fbx` et `<nom_du_blend>.pipesync.json` (et un sous-dossier
  `Textures/` si votre scène utilise des textures).
- Ouvrez le `.pipesync.json` dans un éditeur de texte : vous devez y voir la
  liste de vos matériaux avec leurs couleurs/valeurs.

---

## 2. Installer et lancer le service PipeSync

1. Ouvrez un terminal (PowerShell) dans le dossier `service/` de ce dépôt.
2. Installez la dépendance nécessaire :
   ```
   pip install -r requirements.txt
   ```
3. Copiez `pipesync_config.json` (ou modifiez-le directement) et adaptez les
   deux chemins :
   ```json
   {
     "export_dir": "C:/PipeSync/export",
     "unity_project_dir": "C:/Chemin/Vers/MonProjetUnity",
     "max_versions": 20
   }
   ```
   - `export_dir` : **le même dossier** que celui renseigné dans le panneau
     Blender.
   - `unity_project_dir` : le dossier racine de votre projet Unity (celui
     qui contient le dossier `Assets/`).
4. Lancez le service :
   ```
   python pipesync_service.py
   ```
5. Le service affiche des logs dans la console et reste actif. Laissez cette
   fenêtre ouverte pendant que vous travaillez dans Blender. `Ctrl+C` pour
   l'arrêter.

### Tester ce composant seul

- Avec le service lancé, sauvegardez à nouveau votre fichier `.blend` (ou
  cliquez sur "Exporter maintenant").
- Dans la console du service, vous devez voir des lignes du type
  `Traitement de l'asset : ...` puis `Copié vers le projet Unity : ...`.
- Vérifiez que le dossier
  `<ProjetUnity>/Assets/PipeSync/<nom_du_blend>/` contient bien le FBX (et
  les textures).
- Vérifiez qu'un dossier horodaté est apparu sous
  `<DossierExport>/.pipesync_versions/<nom_du_blend>/`.

---

## 3. Installer le package Unity

1. Ouvrez votre projet Unity (2022.3 LTS ou plus récent, URP).
2. Dans l'explorateur de fichiers Windows, copiez le contenu du dossier
   [`unity_package/Assets/PipeSync`](unity_package/Assets/PipeSync) de ce
   dépôt vers `<VotreProjetUnity>/Assets/PipeSync`.
   - Résultat attendu : `Assets/PipeSync/Editor/PipeSyncPostprocessor.cs`
     (et les 2 autres scripts `.cs`) existent dans votre projet.
3. Revenez dans Unity : il recompile automatiquement les scripts (une barre
   de progression apparaît brièvement).
4. Menu **Tools > PipeSync > Settings** : une fenêtre s'ouvre.
   - **Activer PipeSync** : coché par défaut.
   - **Dossier surveillé** : `Assets/PipeSync` par défaut — c'est le dossier
     dans lequel le service dépose les assets (correspond au sous-dossier
     `Assets/PipeSync/<nom>/` créé automatiquement par le service). Vous
     pouvez changer ce chemin avec **Parcourir...** si besoin.

### Tester ce composant seul

- Avec un FBX déjà copié dans `Assets/PipeSync/<nom>/` par le service (étape
  2), dans Unity, sélectionnez ce FBX dans la fenêtre **Project**.
- Dans l'**Inspector**, onglet **Model** : `Scale Factor` doit être `1`.
- Un dossier `Assets/PipeSync/<nom>/Materials/` doit contenir un matériau
  URP Lit par matériau Blender, avec la bonne couleur de base.
- Glissez le modèle dans la scène : ses matériaux doivent apparaître
  correctement.

---

## Test de bout en bout (les 3 composants ensemble)

1. Le service PipeSync tourne (étape 2), Unity est ouvert avec le package
   installé (étape 3).
2. Dans Blender, créez un cube, positionnez-le, sauvegardez le `.blend`
   avec l'export auto activé.
3. En moins de 10 secondes, le cube doit apparaître dans
   `Assets/PipeSync/<nom_du_blend>/` dans Unity.
4. Glissez ce cube dans une scène, créez un **Prefab** à partir de lui.
5. Modifiez la scène Blender (déplacez le cube, changez sa couleur),
   sauvegardez à nouveau.
6. Le prefab dans Unity doit se mettre à jour **sans se casser** (pas de
   perte de référence, pas de doublon d'asset).
7. Vérifiez qu'un nouveau dossier horodaté est apparu sous
   `.pipesync_versions/personnage/` dans le dossier d'export.

### Checklist de validation Phase 1

- [ ] Un cube sauvegardé dans Blender apparaît dans Unity en moins de 10 s.
- [ ] Un cube de 1 m dans Blender = 1 unité Unity (pas de mise à l'échelle
      surprise).
- [ ] Un prefab utilisant l'asset ne casse pas après une mise à jour.
- [ ] Les versions s'archivent correctement dans `.pipesync_versions/`.

**Ne passez pas à la Phase 2 (conversion complète des matériaux : normal
map, roughness/metallic, emission, transparence) avant d'avoir validé
chacun de ces points.**

---

## Dépannage

| Problème | Piste |
|---|---|
| Rien ne se passe à la sauvegarde | Vérifiez que le toggle "Export auto" est coché et que le fichier `.blend` a déjà été sauvegardé une première fois. |
| Le service ne détecte rien | Vérifiez que `export_dir` est identique des deux côtés (add-on et `pipesync_config.json`). |
| `pip install` échoue | Vérifiez que `python` et `pip` sont bien dans le PATH (`python --version`). |
| Le matériau URP Lit n'apparaît pas | Vérifiez que le package URP est bien installé et actif sur le projet Unity (`Window > Package Manager`). |
| Le prefab perd sa référence | Assurez-vous que rien ne supprime manuellement le dossier `Assets/PipeSync/<nom>/` entre deux syncs — seul le service doit y écrire, en écrasant en place. |

## Structure du dépôt

```
pipesync/
├── blender_addon/
│   └── pipesync_addon.py
├── service/
│   ├── pipesync_service.py
│   ├── pipesync_config.json
│   └── requirements.txt
├── unity_package/
│   └── Assets/PipeSync/Editor/
│       ├── PipeSyncPostprocessor.cs
│       ├── PipeSyncMaterialConverter.cs
│       └── PipeSyncSettingsWindow.cs
├── README.md
└── docs/
    └── ARCHITECTURE.md
```
