"""
PipeSync — Service local
Surveille le dossier d'export Blender. Dès qu'un FBX et son fichier
compagnon .pipesync.json sont présents et stables, il les copie (avec les
textures) dans <ProjetUnity>/Assets/PipeSync/<NomAsset>/, et archive une
copie horodatée pour permettre un retour en arrière plus tard.
"""

import argparse
import json
import logging
import shutil
import sys
import time
from pathlib import Path

from watchdog.events import FileSystemEventHandler
from watchdog.observers import Observer

logger = logging.getLogger("pipesync")

DELAI_STABILITE_SEC = 0.4  # temps d'attente pour vérifier qu'un fichier n'est plus en cours d'écriture
NB_VERSIONS_PAR_DEFAUT = 20

# Un seul enregistrement/export Blender déclenche souvent plusieurs événements filesystem
# (plusieurs notifications pour une même écriture, un événement par fichier fbx/json...).
# On retient la dernière date de modification déjà traitée par asset pour ignorer les
# événements redondants, plutôt que d'archiver une nouvelle version à chaque fois.
_dernier_mtime_traite = {}


def configurer_logs():
    logging.basicConfig(
        level=logging.INFO,
        format="%(asctime)s [PipeSync] %(levelname)s: %(message)s",
        datefmt="%H:%M:%S",
    )


def charger_config(chemin_config: Path) -> dict:
    if not chemin_config.exists():
        raise FileNotFoundError(
            f"Fichier de config introuvable : {chemin_config}. "
            f"Copiez pipesync_config.json et adaptez les chemins."
        )
    with open(chemin_config, "r", encoding="utf-8") as f:
        config = json.load(f)

    for cle in ("export_dir", "unity_project_dir"):
        if cle not in config:
            raise ValueError(f"Clé manquante dans la config : {cle}")

    config["export_dir"] = str(Path(config["export_dir"]).expanduser().resolve())
    config["unity_project_dir"] = str(Path(config["unity_project_dir"]).expanduser().resolve())
    config.setdefault("max_versions", NB_VERSIONS_PAR_DEFAUT)
    return config


def _fichier_est_stable(chemin: Path) -> bool:
    """Retourne True si la taille du fichier ne change plus (écriture terminée)."""
    try:
        taille_avant = chemin.stat().st_size
        time.sleep(DELAI_STABILITE_SEC)
        taille_apres = chemin.stat().st_size
        return taille_avant == taille_apres
    except FileNotFoundError:
        return False


def _chemins_textures_referencees(donnees_json: dict) -> list:
    """Extrait les chemins relatifs de textures (ex: 'Textures/x.png') cités dans le JSON."""
    chemins = []
    for materiau in donnees_json.get("materials", []):
        for chemin_relatif in materiau.get("textures", {}).values():
            if chemin_relatif:
                chemins.append(chemin_relatif)
    return chemins


def _copier_asset_vers_dossier(nom_asset: str, chemin_fbx: Path, chemin_json: Path,
                                dossier_export: Path, dossier_dest: Path) -> None:
    """Copie le FBX, le JSON et les textures référencées dans dossier_dest."""
    dossier_dest.mkdir(parents=True, exist_ok=True)

    # Écrase le FBX en place (même nom, même chemin relatif) pour préserver le
    # .meta / GUID Unity existant et ne pas casser les prefabs qui l'utilisent.
    shutil.copy2(chemin_fbx, dossier_dest / chemin_fbx.name)
    shutil.copy2(chemin_json, dossier_dest / chemin_json.name)

    with open(chemin_json, "r", encoding="utf-8") as f:
        donnees_json = json.load(f)

    for chemin_relatif in _chemins_textures_referencees(donnees_json):
        source = dossier_export / chemin_relatif
        if not source.exists():
            logger.warning("Texture référencée introuvable, ignorée : %s", source)
            continue
        dest = dossier_dest / chemin_relatif
        dest.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, dest)


def _archiver_version(nom_asset: str, chemin_fbx: Path, chemin_json: Path,
                       dossier_export: Path, max_versions: int) -> None:
    """Archive une copie horodatée de l'asset et purge les versions les plus anciennes."""
    horodatage = time.strftime("%Y%m%d-%H%M%S")
    dossier_version = dossier_export / ".pipesync_versions" / nom_asset / horodatage
    _copier_asset_vers_dossier(nom_asset, chemin_fbx, chemin_json, dossier_export, dossier_version)

    dossier_asset_versions = dossier_export / ".pipesync_versions" / nom_asset
    versions = sorted(
        (d for d in dossier_asset_versions.iterdir() if d.is_dir()),
        key=lambda d: d.name,
    )
    while len(versions) > max_versions:
        plus_ancienne = versions.pop(0)
        shutil.rmtree(plus_ancienne, ignore_errors=True)
        logger.info("Ancienne version purgée : %s", plus_ancienne)


def traiter_asset(nom_asset: str, config: dict) -> None:
    dossier_export = Path(config["export_dir"])
    chemin_fbx = dossier_export / f"{nom_asset}.fbx"
    chemin_json = dossier_export / f"{nom_asset}.pipesync.json"

    if not chemin_fbx.exists() or not chemin_json.exists():
        return  # on attend que les deux fichiers soient présents

    if not _fichier_est_stable(chemin_fbx) or not _fichier_est_stable(chemin_json):
        logger.info("Fichiers de '%s' encore en cours d'écriture, on réessaiera.", nom_asset)
        return

    logger.info("Traitement de l'asset : %s", nom_asset)

    dossier_dest = Path(config["unity_project_dir"]) / "Assets" / "PipeSync" / nom_asset
    try:
        _copier_asset_vers_dossier(nom_asset, chemin_fbx, chemin_json, dossier_export, dossier_dest)
        logger.info("Copié vers le projet Unity : %s", dossier_dest)
    except Exception as erreur:
        logger.error("Échec de la copie vers Unity pour '%s' : %s", nom_asset, erreur)
        return

    try:
        _archiver_version(nom_asset, chemin_fbx, chemin_json, dossier_export, config["max_versions"])
        logger.info("Version archivée pour : %s", nom_asset)
    except Exception as erreur:
        logger.error("Échec de l'archivage de version pour '%s' : %s", nom_asset, erreur)


def _nom_asset_depuis_chemin(chemin: Path) -> str:
    nom = chemin.name
    if nom.endswith(".pipesync.json"):
        return nom[: -len(".pipesync.json")]
    if nom.endswith(".fbx"):
        return chemin.stem
    return chemin.stem


class GestionnaireEvenements(FileSystemEventHandler):
    def __init__(self, config: dict):
        self.config = config

    def _est_fichier_pertinent(self, chemin_str: str) -> bool:
        return chemin_str.endswith(".fbx") or chemin_str.endswith(".pipesync.json")

    def on_created(self, event):
        self._gerer(event)

    def on_modified(self, event):
        self._gerer(event)

    def on_moved(self, event):
        if self._est_fichier_pertinent(event.dest_path):
            nom_asset = _nom_asset_depuis_chemin(Path(event.dest_path))
            traiter_asset(nom_asset, self.config)

    def _gerer(self, event):
        if event.is_directory:
            return
        if not self._est_fichier_pertinent(event.src_path):
            return
        nom_asset = _nom_asset_depuis_chemin(Path(event.src_path))
        traiter_asset(nom_asset, self.config)


def synchroniser_assets_existants(config: dict) -> None:
    """Au démarrage, traite les paires FBX/JSON déjà présentes dans le dossier d'export."""
    dossier_export = Path(config["export_dir"])
    for chemin_json in dossier_export.glob("*.pipesync.json"):
        nom_asset = _nom_asset_depuis_chemin(chemin_json)
        traiter_asset(nom_asset, config)


def lancer_service(chemin_config: Path) -> None:
    configurer_logs()
    config = charger_config(chemin_config)

    dossier_export = Path(config["export_dir"])
    dossier_export.mkdir(parents=True, exist_ok=True)

    logger.info("Dossier d'export surveillé : %s", dossier_export)
    logger.info("Projet Unity cible : %s", config["unity_project_dir"])
    logger.info("Versions conservées par asset : %s", config["max_versions"])

    synchroniser_assets_existants(config)

    gestionnaire = GestionnaireEvenements(config)
    observateur = Observer()
    observateur.schedule(gestionnaire, str(dossier_export), recursive=False)
    observateur.start()

    logger.info("Service PipeSync démarré. Ctrl+C pour arrêter.")
    try:
        while True:
            time.sleep(1)
    except KeyboardInterrupt:
        logger.info("Arrêt du service demandé par l'utilisateur.")
    finally:
        observateur.stop()
        observateur.join()


def main():
    parser = argparse.ArgumentParser(description="Service PipeSync : synchronise Blender vers Unity.")
    parser.add_argument(
        "--config",
        type=Path,
        default=Path(__file__).parent / "pipesync_config.json",
        help="Chemin du fichier de configuration JSON (défaut : pipesync_config.json à côté du script).",
    )
    args = parser.parse_args()

    try:
        lancer_service(args.config)
    except Exception as erreur:
        print(f"[PipeSync] Erreur fatale : {erreur}", file=sys.stderr)
        sys.exit(1)


if __name__ == "__main__":
    main()
