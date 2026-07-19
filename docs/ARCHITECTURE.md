# Architecture PipeSync

```
[Add-on Blender (Python)] → [Dossier d'export surveillé] → [Service PipeSync (watcher)] → [Package Unity Editor (C#)]
```

## 1. Add-on Blender (`blender_addon/pipesync_addon.py`)

- Panneau N (`Vue 3D > N > PipeSync`) avec le dossier d'export, le toggle
  d'export auto et le bouton "Exporter maintenant".
- À la sauvegarde du `.blend` (`bpy.app.handlers.save_post`), si l'export
  auto est activé, lance `executer_export()`.
- `executer_export()` :
  1. Détermine les objets à exporter (collection `Export` si elle existe,
     sinon tous les objets visibles).
  2. Exporte un FBX (`<nom_du_blend>.fbx`) avec les réglages figés dans le
     cahier des charges (échelle, axes, modificateurs appliqués, etc.).
  3. Parcourt les matériaux utilisés, lit le node **Principled BSDF** de
     chacun, copie les textures connectées dans un sous-dossier
     `Textures/`, et écrit `<nom_du_blend>.pipesync.json` avec :
     - `base_color` (RGBA), `metallic`, `roughness`, `alpha` (valeurs
       scalaires, toujours présentes même si un canal est piloté par une
       texture) ;
     - `emission_color` (RGB) et `emission_strength` ;
     - `textures.{base_color,normal,roughness,metallic,emission}` : chemins
       relatifs (`Textures/xxx.png`) vers les textures connectées, ou `null`.

## 2. Service PipeSync (`service/pipesync_service.py`)

- Utilise `watchdog` pour surveiller le dossier d'export configuré dans
  `pipesync_config.json`.
- Pour chaque paire `<nom>.fbx` / `<nom>.pipesync.json` détectée :
  1. Attend que les deux fichiers soient stables (plus en cours d'écriture).
  2. Copie le FBX, le JSON et les textures référencées vers
     `<ProjetUnity>/Assets/PipeSync/<nom>/` (écrasement en place : même
     chemin relatif à chaque fois, donc même `.meta`/GUID côté Unity).
  3. Archive une copie horodatée dans
     `<DossierExport>/.pipesync_versions/<nom>/<horodatage>/`, et purge les
     versions au-delà de `max_versions`.
- Au démarrage, traite aussi les fichiers déjà présents dans le dossier
  d'export (utile si le service est lancé après un export).

## 3. Package Unity Editor (`unity_package/Assets/PipeSync/Editor/`)

- `PipeSyncPostprocessor.cs` (`AssetPostprocessor`) :
  - `OnPreprocessModel` : force `Scale Factor = 1`, `Convert Units = off`,
    l'import des Blend Shapes selon les réglages PipeSync, et **remappe**
    (`ModelImporter.AddRemap`) les matériaux du FBX vers les matériaux URP
    Lit générés par PipeSync — l'API officielle d'Unity pour ce cas d'usage
    (plutôt qu'une réassignation manuelle des `Renderer`, qui peut faire
    échouer la vérification de déterminisme de l'import).
  - `OnPostprocessModel` : lit `<nom>.pipesync.json`. Si toutes les
    ressources (matériau, éventuelle texture Metallic/Smoothness combinée)
    existent déjà sur le disque, ne fait rien de plus (le remap a eu lieu
    dans `OnPreprocessModel`). Sinon, planifie leur création juste après
    l'import (voir "Pourquoi deux passes ?" ci-dessous).
  - `OnPreprocessTexture` : détecte, parmi les textures copiées par le
    service dans un sous-dossier `Textures/`, celles qui sont des normal
    maps (`Texture Type = Normal Map`) ou des maps metallic/roughness
    (`sRGB = false`, ce sont des données, pas de la couleur).
  - Ne s'applique qu'aux imports situés dans le dossier surveillé configuré,
    et seulement si PipeSync est activé.
- `PipeSyncMaterialConverter.cs` : logique de création/mise à jour des
  matériaux `.mat` (chargés depuis le disque s'ils existent déjà, pour ne
  pas casser les références de prefabs qui les utilisent) et mapping complet
  Principled BSDF → URP Lit (voir tableau ci-dessous).
- `PipeSyncSettingsWindow.cs` : fenêtre `Tools > PipeSync > Settings`
  (activer/désactiver, dossier surveillé, import des Blend Shapes),
  réglages stockés dans `EditorPrefs`.

### Mapping Principled BSDF → URP Lit (Phase 2)

| Blender | Unity URP Lit | Détail |
|---|---|---|
| Base Color (valeur ou texture) | `_BaseColor` / `_BaseMap` | Si une texture existe, `_BaseColor` passe à blanc (teinte neutre) et ne garde que l'alpha. |
| Metallic + Roughness (valeurs ou textures) | `_Metallic`, `_Smoothness`, ou `_MetallicGlossMap` | Voir "Convention Metallic/Smoothness" ci-dessous. |
| Normal Map (texture) | `_BumpMap` + mot-clé `_NORMALMAP` | Texture importée avec `Texture Type = Normal Map`. |
| Emission Color × Strength (+ texture) | `_EmissionColor`, `_EmissionMap` + mot-clé `_EMISSION` | `_EmissionColor = emission_color × emission_strength`. |
| Alpha (scalaire du Principled BSDF) | `Surface Type` Opaque/Transparent | Transparent si `alpha < 1` (voir simplification ci-dessous). |

**Convention choisie pour Metallic/Smoothness :** dès qu'une texture metallic
OU roughness existe, PipeSync génère une texture combinée
`<Materials>/<NomMatériau>_MetallicSmoothness.png` (canal R = metallic,
canal A = 1 − roughness), assignée à `_MetallicGlossMap` — c'est la
convention standard attendue par le shader URP Lit lui-même. Si aucune
texture n'est utilisée, les valeurs scalaires `_Metallic`/`_Smoothness` sont
appliquées directement, sans génération de texture. C'est l'approche la plus
simple qui reste correcte visuellement (alternative documentée : combiner
dans le canal alpha de la Base Map — non retenue pour ne pas coupler
transparence et données PBR sur la même texture).

**Simplification pour la transparence :** seule la valeur scalaire `alpha`
du Principled BSDF déclenche le passage en Surface Type Transparent — le
canal alpha d'une éventuelle texture Base Color n'est pas analysé pixel par
pixel (coût inutile pour un MVP ; à revoir si des textures avec alpha
variable en dépendent).

### Pourquoi deux passes d'import ?

Unity interdit d'appeler `AssetDatabase.CreateAsset` (créer un `.mat` ou une
texture générée) pendant un import (`OnPreprocessModel`/`OnPostprocessModel`).
Quand une ressource manque encore (première synchronisation d'un nouvel
asset, ou nouvelle texture metallic/roughness ajoutée dans Blender),
PipeSync la crée juste après l'import via `EditorApplication.delayCall`. Le
remap effectif vers cette ressource se fait alors à l'import suivant — la
prochaine sauvegarde Blender, ou un `Reimport` manuel sur le FBX concerné.

## 4. Confort (Phase 3)

- `pipesync_tray.py` : enveloppe `pipesync_service.py` dans une icône systray
  (`pystray`/`Pillow`). Extrait la logique de démarrage/arrêt du service dans
  une classe `ServicePipeSync` réutilisable (au lieu d'une boucle bloquante),
  pilotable depuis le menu de l'icône (Démarrer/Arrêter/Logs/Config/Quitter).
- `PipeSyncVersionsWindow.cs` (`Tools > PipeSync > Versions`) : lit
  directement `<DossierExport>/.pipesync_versions/<Nom>/` (chemin renseigné
  dans les réglages) et permet de recopier une version archivée vers
  `Assets/PipeSync/<Nom>/`, puis force un réimport. Ne modifie que la copie
  Unity — une future sauvegarde Blender réécrasera avec l'état du `.blend`.
- Notifications : `SceneView.ShowNotification` dans `PipeSyncPostprocessor`
  et `PipeSyncVersionsWindow`, en plus des logs `Debug.Log` existants.
- LOD automatiques (optionnels dans le cahier des charges) non construits.

## Pourquoi les GUID ne cassent pas

Unity associe un GUID à chaque asset via son fichier `.meta`. Ce GUID est ce
que référencent les prefabs (pas le nom de fichier). Tant que :

1. le service **écrase** le FBX au même chemin `Assets/PipeSync/<nom>/<nom>.fbx`
   (jamais de suppression/recréation), et
2. le postprocessor ne supprime jamais de `.meta`,

...le `.meta` — et donc le GUID — reste stable d'un export à l'autre, et les
prefabs qui référencent le modèle ou ses matériaux continuent de fonctionner
après une mise à jour.
