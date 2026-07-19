"""
PipeSync — Interface systray du service
Fait tourner le service PipeSync (voir pipesync_service.py) en tâche de fond
avec une icône dans la barre des tâches Windows : Démarrer/Arrêter, Voir les
logs, Ouvrir la config, Quitter. Évite de devoir garder une fenêtre de
terminal ouverte en permanence.
"""

import logging
import subprocess
import sys
from pathlib import Path

from PIL import Image, ImageDraw
import pystray

from pipesync_service import ServicePipeSync, configurer_logs, logger

CHEMIN_CONFIG_DEFAUT = Path(__file__).parent / "pipesync_config.json"
CHEMIN_LOG = Path(__file__).parent / "pipesync.log"


def _configurer_logs_fichier() -> None:
    """En plus de la console, écrit les logs dans un fichier consultable via le menu."""
    configurer_logs()
    gestionnaire_fichier = logging.FileHandler(CHEMIN_LOG, encoding="utf-8")
    gestionnaire_fichier.setFormatter(
        logging.Formatter("%(asctime)s [PipeSync] %(levelname)s: %(message)s", "%H:%M:%S")
    )
    logging.getLogger("pipesync").addHandler(gestionnaire_fichier)


def _creer_image_icone(actif: bool) -> Image.Image:
    """Génère une icône simple (rond vert = actif, gris = arrêté), sans dépendance à un fichier .ico."""
    taille = 64
    image = Image.new("RGBA", (taille, taille), (0, 0, 0, 0))
    dessin = ImageDraw.Draw(image)
    couleur = (46, 160, 67, 255) if actif else (140, 140, 140, 255)
    marge = 6
    dessin.ellipse((marge, marge, taille - marge, taille - marge), fill=couleur)
    return image


class ApplicationTray:
    def __init__(self, chemin_config: Path):
        self.service = ServicePipeSync(chemin_config)
        self.icone = pystray.Icon(
            "pipesync",
            icon=_creer_image_icone(False),
            title="PipeSync (arrêté)",
            menu=self._construire_menu(),
        )

    def _construire_menu(self) -> pystray.Menu:
        return pystray.Menu(
            pystray.MenuItem("Démarrer", self._demarrer, enabled=lambda item: not self.service.actif),
            pystray.MenuItem("Arrêter", self._arreter, enabled=lambda item: self.service.actif),
            pystray.Menu.SEPARATOR,
            pystray.MenuItem("Voir les logs", self._voir_logs),
            pystray.MenuItem("Ouvrir la config", self._ouvrir_config),
            pystray.Menu.SEPARATOR,
            pystray.MenuItem("Quitter", self._quitter),
        )

    def _rafraichir_icone(self) -> None:
        self.icone.icon = _creer_image_icone(self.service.actif)
        self.icone.title = f"PipeSync ({'actif' if self.service.actif else 'arrêté'})"

    def _demarrer(self, icone=None, item=None) -> None:
        try:
            self.service.demarrer()
        except Exception as erreur:
            logger.error("Impossible de démarrer le service : %s", erreur)
        self._rafraichir_icone()

    def _arreter(self, icone=None, item=None) -> None:
        self.service.arreter()
        self._rafraichir_icone()

    def _voir_logs(self, icone=None, item=None) -> None:
        CHEMIN_LOG.touch(exist_ok=True)
        subprocess.Popen(["notepad.exe", str(CHEMIN_LOG)])

    def _ouvrir_config(self, icone=None, item=None) -> None:
        subprocess.Popen(["notepad.exe", str(self.service.chemin_config)])

    def _quitter(self, icone=None, item=None) -> None:
        self._arreter()
        self.icone.stop()

    def executer(self) -> None:
        _configurer_logs_fichier()
        self._demarrer()
        self.icone.run()


def main() -> None:
    try:
        ApplicationTray(CHEMIN_CONFIG_DEFAUT).executer()
    except Exception as erreur:
        print(f"[PipeSync] Erreur fatale : {erreur}", file=sys.stderr)
        sys.exit(1)


if __name__ == "__main__":
    main()
