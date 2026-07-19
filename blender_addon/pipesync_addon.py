"""
PipeSync — Add-on Blender
Exporte automatiquement la scène en FBX (+ un fichier .pipesync.json décrivant
les matériaux) à chaque sauvegarde du fichier .blend, pour synchronisation
avec un projet Unity via le service PipeSync.
"""

import bpy
import json
import shutil
from pathlib import Path
from datetime import datetime, timezone
from bpy.app.handlers import persistent

bl_info = {
    "name": "PipeSync",
    "author": "PipeSync",
    "version": (1, 0, 0),
    "blender": (4, 0, 0),
    "location": "Vue 3D > N-panel > PipeSync",
    "description": "Exporte automatiquement la scène en FBX vers Unity à la sauvegarde du .blend",
    "category": "Import-Export",
}

NOM_COLLECTION_EXPORT = "Export"


# ---------------------------------------------------------------------------
# Utilitaires d'extraction des matériaux (Principled BSDF -> JSON)
# ---------------------------------------------------------------------------

def _trouver_node_principled(materiau):
    """Retourne le node Principled BSDF d'un matériau, ou None si absent."""
    if not materiau or not materiau.use_nodes or not materiau.node_tree:
        return None
    for node in materiau.node_tree.nodes:
        if node.type == 'BSDF_PRINCIPLED':
            return node
    return None


def _image_connectee(entree):
    """
    Si l'entrée (socket) du node Principled est connectée à une texture,
    retourne l'image Blender correspondante (en traversant un éventuel
    node 'Normal Map' pour les normales). Retourne None sinon.
    """
    if not entree.is_linked:
        return None
    node_source = entree.links[0].from_node
    if node_source.type == 'NORMAL_MAP':
        entree_couleur = node_source.inputs.get('Color')
        if entree_couleur and entree_couleur.is_linked:
            node_source = entree_couleur.links[0].from_node
        else:
            return None
    if node_source.type == 'TEX_IMAGE' and node_source.image:
        return node_source.image
    return None


def _copier_texture(image, dossier_textures):
    """
    Copie le fichier source de l'image dans le sous-dossier Textures/ et
    retourne le chemin relatif (ex: "Textures/nom.png") à écrire dans le JSON.
    Retourne None si l'image n'a pas de fichier source exploitable (générée,
    packée sans chemin, etc.).
    """
    if not image or not image.filepath:
        return None
    chemin_source = Path(bpy.path.abspath(image.filepath))
    if not chemin_source.exists():
        return None
    dossier_textures.mkdir(parents=True, exist_ok=True)
    chemin_dest = dossier_textures / chemin_source.name
    shutil.copy2(chemin_source, chemin_dest)
    return f"Textures/{chemin_dest.name}"


def _entree_principled(principled, *noms):
    """Retourne la première entrée existante parmi plusieurs noms possibles
    (utile car Blender 4.x a renommé certaines entrées, ex: 'Emission' ->
    'Emission Color')."""
    for nom in noms:
        entree = principled.inputs.get(nom)
        if entree is not None:
            return entree
    return None


def _extraire_materiau(materiau, dossier_textures):
    """Construit le dictionnaire décrivant un matériau pour le JSON PipeSync."""
    principled = _trouver_node_principled(materiau)

    donnees = {
        "name": materiau.name,
        "base_color": [1.0, 1.0, 1.0, 1.0],
        "metallic": 0.0,
        "roughness": 0.5,
        "textures": {
            "base_color": None,
            "normal": None,
            "roughness": None,
            "metallic": None,
            "emission": None,
        },
    }

    if principled is None:
        return donnees

    entree_base_color = _entree_principled(principled, "Base Color")
    entree_metallic = _entree_principled(principled, "Metallic")
    entree_roughness = _entree_principled(principled, "Roughness")
    entree_normal = _entree_principled(principled, "Normal")
    entree_emission = _entree_principled(principled, "Emission Color", "Emission")

    if entree_base_color is not None:
        donnees["base_color"] = list(entree_base_color.default_value)
        image = _image_connectee(entree_base_color)
        donnees["textures"]["base_color"] = _copier_texture(image, dossier_textures)

    if entree_metallic is not None:
        donnees["metallic"] = entree_metallic.default_value
        image = _image_connectee(entree_metallic)
        donnees["textures"]["metallic"] = _copier_texture(image, dossier_textures)

    if entree_roughness is not None:
        donnees["roughness"] = entree_roughness.default_value
        image = _image_connectee(entree_roughness)
        donnees["textures"]["roughness"] = _copier_texture(image, dossier_textures)

    if entree_normal is not None:
        image = _image_connectee(entree_normal)
        donnees["textures"]["normal"] = _copier_texture(image, dossier_textures)

    if entree_emission is not None:
        image = _image_connectee(entree_emission)
        donnees["textures"]["emission"] = _copier_texture(image, dossier_textures)

    return donnees


# ---------------------------------------------------------------------------
# Sélection des objets à exporter et export FBX
# ---------------------------------------------------------------------------

def _objets_a_exporter(context):
    """
    Retourne les objets à exporter : ceux de la collection "Export" si elle
    existe, sinon tous les objets visibles de la scène.
    """
    if NOM_COLLECTION_EXPORT in bpy.data.collections:
        collection = bpy.data.collections[NOM_COLLECTION_EXPORT]
        return [obj for obj in collection.all_objects if obj.visible_get()]
    return [obj for obj in context.view_layer.objects if obj.visible_get()]


def _materiaux_utilises(objets):
    """Retourne la liste des matériaux (uniques, dans l'ordre) utilisés par les objets."""
    materiaux = []
    noms_vus = set()
    for obj in objets:
        if not hasattr(obj, "material_slots"):
            continue
        for slot in obj.material_slots:
            mat = slot.material
            if mat and mat.name not in noms_vus:
                noms_vus.add(mat.name)
                materiaux.append(mat)
    return materiaux


def executer_export(context):
    """
    Exporte la scène (ou la collection "Export") en FBX vers le dossier
    d'export configuré, et écrit le fichier compagnon <nom>.pipesync.json.
    Retourne le chemin du fichier FBX généré.
    """
    if not bpy.data.filepath:
        raise RuntimeError("Le fichier .blend doit être sauvegardé au moins une fois avant l'export PipeSync.")

    settings = context.scene.pipesync_settings
    if not settings.export_dir:
        raise RuntimeError("Aucun dossier d'export configuré dans le panneau PipeSync.")

    dossier_export = Path(bpy.path.abspath(settings.export_dir))
    dossier_export.mkdir(parents=True, exist_ok=True)

    nom_base = Path(bpy.data.filepath).stem
    chemin_fbx = dossier_export / f"{nom_base}.fbx"
    dossier_textures = dossier_export / "Textures"

    kwargs_export = dict(
        filepath=str(chemin_fbx),
        check_existing=False,
        use_selection=False,
        apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z',
        axis_up='Y',
        bake_space_transform=True,
        use_mesh_modifiers=True,
        path_mode='COPY',
        embed_textures=False,
    )
    if NOM_COLLECTION_EXPORT in bpy.data.collections:
        kwargs_export["collection"] = NOM_COLLECTION_EXPORT

    bpy.ops.export_scene.fbx(**kwargs_export)

    objets = _objets_a_exporter(context)
    materiaux = _materiaux_utilises(objets)
    donnees_materiaux = [_extraire_materiau(mat, dossier_textures) for mat in materiaux]

    donnees_json = {
        "timestamp": datetime.now(timezone.utc).isoformat(),
        "materials": donnees_materiaux,
    }

    chemin_json = dossier_export / f"{nom_base}.pipesync.json"
    with open(chemin_json, "w", encoding="utf-8") as f:
        json.dump(donnees_json, f, ensure_ascii=False, indent=2)

    return chemin_fbx


# ---------------------------------------------------------------------------
# Handler de sauvegarde
# ---------------------------------------------------------------------------

@persistent
def _handler_apres_sauvegarde(dummy):
    scene = bpy.context.scene
    settings = getattr(scene, "pipesync_settings", None)
    if settings is None or not settings.auto_export:
        return
    try:
        executer_export(bpy.context)
        print("[PipeSync] Export automatique réussi.")
    except Exception as erreur:
        print(f"[PipeSync] Échec de l'export automatique : {erreur}")


# ---------------------------------------------------------------------------
# Propriétés, opérateur et panneau UI
# ---------------------------------------------------------------------------

class PIPESYNC_Settings(bpy.types.PropertyGroup):
    export_dir: bpy.props.StringProperty(
        name="Dossier d'export",
        description="Dossier où PipeSync exporte le FBX et le fichier .pipesync.json",
        subtype='DIR_PATH',
        default="//pipesync_export/",
    )
    auto_export: bpy.props.BoolProperty(
        name="Export auto à la sauvegarde",
        description="Exporter automatiquement vers PipeSync à chaque sauvegarde du fichier .blend",
        default=False,
    )


class PIPESYNC_OT_export_now(bpy.types.Operator):
    bl_idname = "pipesync.export_now"
    bl_label = "Exporter maintenant"
    bl_description = "Lance immédiatement un export PipeSync (FBX + matériaux)"

    def execute(self, context):
        try:
            chemin_fbx = executer_export(context)
        except Exception as erreur:
            self.report({'ERROR'}, f"PipeSync : {erreur}")
            return {'CANCELLED'}
        self.report({'INFO'}, f"PipeSync : export réussi vers {chemin_fbx}")
        return {'FINISHED'}


class PIPESYNC_PT_panel(bpy.types.Panel):
    bl_label = "PipeSync"
    bl_idname = "PIPESYNC_PT_panel"
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = "PipeSync"

    def draw(self, context):
        layout = self.layout
        settings = context.scene.pipesync_settings

        layout.prop(settings, "export_dir")
        layout.prop(settings, "auto_export")
        layout.operator("pipesync.export_now", icon='EXPORT')

        if NOM_COLLECTION_EXPORT in bpy.data.collections:
            layout.label(text="Collection 'Export' détectée", icon='INFO')
        else:
            layout.label(text="Export : tous les objets visibles", icon='INFO')


# ---------------------------------------------------------------------------
# Enregistrement de l'add-on
# ---------------------------------------------------------------------------

CLASSES = (
    PIPESYNC_Settings,
    PIPESYNC_OT_export_now,
    PIPESYNC_PT_panel,
)


def register():
    for cls in CLASSES:
        bpy.utils.register_class(cls)
    bpy.types.Scene.pipesync_settings = bpy.props.PointerProperty(type=PIPESYNC_Settings)

    if _handler_apres_sauvegarde not in bpy.app.handlers.save_post:
        bpy.app.handlers.save_post.append(_handler_apres_sauvegarde)


def unregister():
    if _handler_apres_sauvegarde in bpy.app.handlers.save_post:
        bpy.app.handlers.save_post.remove(_handler_apres_sauvegarde)

    del bpy.types.Scene.pipesync_settings
    for cls in reversed(CLASSES):
        bpy.utils.unregister_class(cls)


if __name__ == "__main__":
    register()
