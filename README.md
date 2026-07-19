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

- [x] Un cube sauvegardé dans Blender apparaît dans Unity en moins de 10 s
      (un focus sur la fenêtre Unity peut être nécessaire pour déclencher le
      rafraîchissement — comportement natif de l'Editor Unity).
- [x] Un cube de 1 m dans Blender = 1 unité Unity (pas de mise à l'échelle
      surprise).
- [x] Un prefab utilisant l'asset ne casse pas après une mise à jour.
- [x] Les versions s'archivent correctement dans `.pipesync_versions/`.

---

## Phase 2 — Conversion de matériaux complète

Ajoute le mapping complet Principled BSDF → URP Lit (voir le détail et les
choix documentés dans [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)) :

| Blender | Unity URP Lit |
|---|---|
| Base Color (texture) | Base Map |
| Metallic + Roughness (valeurs ou textures) | Metallic / Smoothness (texture combinée générée si besoin) |
| Normal Map | Normal Map (+ import automatique en `Texture Type = Normal Map`) |
| Emission Color × Strength (+ texture) | Emission (+ mot-clé `_EMISSION`) |
| Alpha < 1 | Surface Type = Transparent |

Aucune installation supplémentaire n'est nécessaire : il suffit de recopier
les fichiers `.cs` mis à jour dans
[unity_package/Assets/PipeSync/Editor/](unity_package/Assets/PipeSync/Editor/)
vers votre projet Unity, en écrasant les anciens (mêmes noms de fichiers).

### Tester la Phase 2

1. Dans Blender, sur un objet déjà synchronisé, connectez une **texture** à
   Base Color (et/ou Normal, Roughness, Metallic, Emission) de son
   Principled BSDF, et sauvegardez.
2. Comme pour un nouveau matériau, la **première** synchronisation crée les
   ressources manquantes (matériau, éventuelle texture combinée
   Metallic/Smoothness) sans les appliquer immédiatement (voir "Pourquoi
   deux passes d'import ?" dans `docs/ARCHITECTURE.md`) — sauvegardez une
   seconde fois (ou faites un **Reimport** manuel du FBX dans Unity) pour
   voir le résultat appliqué.
3. Vérifiez dans l'Inspector du matériau généré : la texture apparaît dans
   **Base Map**, un dossier `Materials/<Nom>_MetallicSmoothness.png` est
   apparu si vous utilisiez une texture Metallic ou Roughness.
4. Pour la transparence : mettez l'entrée **Alpha** du Principled BSDF
   en dessous de `1`, sauvegardez (×2 comme ci-dessus), et vérifiez que
   `Surface Type` passe à **Transparent** sur le matériau dans Unity.

### Checklist de validation Phase 2

- [x] Une texture Base Color apparaît sur le matériau dans Unity.
- [x] Une Normal Map est importée en `Texture Type = Normal Map` et donne du
      relief visible sur le modèle.
- [x] Metallic/Roughness (texture ou valeurs) donnent un rendu cohérent
      (reflets nets = lisse/métallique, reflets diffus = rugueux).
- [x] Emission fait briller l'objet (couleur × intensité, ou texture).
- [x] Un alpha < 1 rend l'objet transparent dans la Scene view.

---

## Phase 3 — Confort

Ajoute une interface systray pour le service (plus besoin de garder un
terminal ouvert), une fenêtre de gestion des versions dans Unity, et des
notifications visuelles. Les LOD automatiques (optionnels dans le cahier des
charges) n'ont pas été construits.

### Interface systray du service

Remplace `pipesync_service.py` par [`pipesync_tray.py`](service/pipesync_tray.py)
pour un usage au quotidien : icône dans la barre des tâches Windows au lieu
d'une fenêtre de terminal à garder ouverte.

1. Installez les nouvelles dépendances :
   ```
   pip install -r requirements.txt
   ```
2. Lancez :
   ```
   python pipesync_tray.py
   ```
3. Une icône apparaît dans la barre des tâches (zone de notification, cliquez
   sur la flèche `^` si elle est masquée). Clic droit dessus :
   - **Démarrer** / **Arrêter** : contrôle la surveillance du dossier d'export.
   - **Voir les logs** : ouvre `pipesync.log` (créé à côté du script) dans le
     Bloc-notes.
   - **Ouvrir la config** : ouvre `pipesync_config.json` dans le Bloc-notes.
   - **Quitter** : arrête le service et ferme l'icône.

Le script `pipesync_service.py` original reste utilisable tel quel en ligne
de commande si vous préférez (ex. pour un lancement scripté sans interface).

### Fenêtre de versions dans Unity

1. D'abord, renseignez le **dossier d'export Blender** dans
   `Tools > PipeSync > Settings` (le même chemin que `export_dir` dans
   `pipesync_config.json`, ex. `C:/PipeSync/export`) — ce nouveau champ
   permet à Unity de retrouver l'historique `.pipesync_versions/`.
2. Menu **Tools > PipeSync > Versions**.
3. Choisissez un asset dans la liste déroulante : les versions archivées
   s'affichent, les plus récentes en premier.
4. Cliquez sur **Restaurer** en face d'une version pour remettre l'asset
   (FBX + textures) dans cet état dans `Assets/PipeSync/<Nom>/`. Une
   confirmation est demandée avant d'écraser la version actuelle.

*Important :* restaurer ne modifie que la copie côté Unity. Si vous
ressauvegardez ensuite depuis Blender, le service écrasera à nouveau avec
l'état courant du fichier `.blend` — restaurer sert à revenir en arrière
ponctuellement dans Unity, pas à modifier l'historique côté Blender.

### Notifications

Un petit toast ("PipeSync : '<Nom>' mis à jour") apparaît dans la Scene view
d'Unity à chaque synchronisation réussie (et à chaque restauration de
version), en plus du log dans la Console.

### Checklist de validation Phase 3

- [x] L'icône systray apparaît, reflète l'état actif/arrêté, et le menu
      (Démarrer/Arrêter/Logs/Config/Quitter) fonctionne.
- [x] `Tools > PipeSync > Versions` liste les versions archivées et
      **Restaurer** remet correctement un asset dans un état antérieur.
- [x] Un toast apparaît dans la Scene view à chaque synchronisation.

---

## Dépannage

| Problème | Piste |
|---|---|
| Rien ne se passe à la sauvegarde | Vérifiez que le toggle "Export auto" est coché et que le fichier `.blend` a déjà été sauvegardé une première fois. |
| Le service ne détecte rien | Vérifiez que `export_dir` est identique des deux côtés (add-on et `pipesync_config.json`). |
| `pip install` échoue | Vérifiez que `python` et `pip` sont bien dans le PATH (`python --version`). |
| Le matériau URP Lit n'apparaît pas | Vérifiez que le package URP est bien installé et actif sur le projet Unity (`Window > Package Manager`). |
| Le prefab perd sa référence | Assurez-vous que rien ne supprime manuellement le dossier `Assets/PipeSync/<nom>/` entre deux syncs — seul le service doit y écrire, en écrasant en place. |
| Une texture/couleur ne se met pas à jour après une nouvelle sauvegarde Blender | Sur un asset **tout juste** modifié (nouvelle texture jamais vue), la première synchronisation crée seulement la ressource ; il faut une deuxième sauvegarde (ou un Reimport manuel) pour qu'elle soit appliquée — voir "Pourquoi deux passes d'import ?" dans `docs/ARCHITECTURE.md`. |
| Le matériau n'a pas de relief malgré une Normal Map | Vérifiez dans l'Inspector de la texture que `Texture Type = Normal Map` est bien réglé — si l'import a eu lieu avant la mise à jour du script, faites un Reimport de la texture. |
| L'icône systray n'apparaît pas | Vérifiez qu'aucune erreur ne s'affiche dans le terminal ayant lancé `pipesync_tray.py` ; l'icône peut être masquée dans la zone de notification Windows, cliquez sur la flèche `^` pour l'afficher. |
| La fenêtre Versions dit "Aucun asset versionné trouvé" | Vérifiez que le champ "Dossier d'export Blender" dans `Tools > PipeSync > Settings` correspond exactement à `export_dir` de `pipesync_config.json`, et qu'au moins une sauvegarde a déjà été archivée. |

## Structure du dépôt

```
pipesync/
├── blender_addon/
│   └── pipesync_addon.py
├── service/
│   ├── pipesync_service.py
│   ├── pipesync_tray.py
│   ├── pipesync_config.json
│   └── requirements.txt
├── unity_package/
│   └── Assets/PipeSync/Editor/
│       ├── PipeSyncPostprocessor.cs
│       ├── PipeSyncMaterialConverter.cs
│       ├── PipeSyncSettingsWindow.cs
│       └── PipeSyncVersionsWindow.cs
├── README.md
└── docs/
    └── ARCHITECTURE.md
```
