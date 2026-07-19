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
     `Textures/`, et écrit `<nom_du_blend>.pipesync.json` avec les valeurs
     (base color, metallic, roughness) et les chemins de texture.

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
    et l'import des Blend Shapes selon les réglages PipeSync.
  - `OnPostprocessModel` : lit `<nom>.pipesync.json` à côté du FBX importé,
    crée/­met à jour un matériau URP Lit par entrée (Phase 1 : base color
    uniquement — le mapping complet arrive en Phase 2), et réassigne ces
    matériaux aux `Renderer` du modèle importé en comparant les noms.
  - Ne s'applique qu'aux imports situés dans le dossier surveillé configuré,
    et seulement si PipeSync est activé.
- `PipeSyncMaterialConverter.cs` : logique de création/mise à jour des
  matériaux `.mat` (chargés depuis le disque s'ils existent déjà, pour ne
  pas casser les références de prefabs qui les utilisent).
- `PipeSyncSettingsWindow.cs` : fenêtre `Tools > PipeSync > Settings`
  (activer/désactiver, dossier surveillé, import des Blend Shapes),
  réglages stockés dans `EditorPrefs`.

## Pourquoi les GUID ne cassent pas

Unity associe un GUID à chaque asset via son fichier `.meta`. Ce GUID est ce
que référencent les prefabs (pas le nom de fichier). Tant que :

1. le service **écrase** le FBX au même chemin `Assets/PipeSync/<nom>/<nom>.fbx`
   (jamais de suppression/recréation), et
2. le postprocessor ne supprime jamais de `.meta`,

...le `.meta` — et donc le GUID — reste stable d'un export à l'autre, et les
prefabs qui référencent le modèle ou ses matériaux continuent de fonctionner
après une mise à jour.
