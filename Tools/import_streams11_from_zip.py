"""Extract streams11 Assets from %USERPROFILE%\\Downloads\\streams11v2.zip into Assets/ImportedStreams11.

Skips duplicate TextMesh Pro (project already has TMP).
After extracting, run merge_streams11_flat.py (same Tools folder) to merge into Scripts/, Sprites/, etc.
"""
import os
import shutil
import zipfile
from pathlib import Path


def find_streams_project() -> Path:
    desktop = Path.home() / "Desktop"
    for unity_projects in desktop.rglob("UnityProjects"):
        cand = unity_projects / "Streams"
        if (cand / "Packages" / "manifest.json").is_file() and (cand / "Assets").is_dir():
            return cand
    raise SystemExit("UnityProjects/Streams not found under Desktop")


def main() -> None:
    root = find_streams_project()
    zip_path = Path(os.environ["USERPROFILE"]) / "Downloads" / "streams11v2.zip"
    if not zip_path.is_file():
        raise SystemExit(f"Zip not found: {zip_path}")

    dest = root / "Assets" / "ImportedStreams11"
    assets_prefix = "streams11/Assets/"
    skip = "streams11/Assets/TextMesh Pro/"

    dest.mkdir(parents=True, exist_ok=True)
    count = 0
    with zipfile.ZipFile(zip_path, "r") as zf:
        for info in zf.infolist():
            name = info.filename
            if not name.startswith(assets_prefix) or name.startswith(skip):
                continue
            if "__MACOSX" in name:
                continue
            rel = name[len(assets_prefix) :]
            if not rel or rel.endswith("/"):
                continue
            if "/." in ("/" + rel.replace("\\", "/")):
                continue
            out = dest / rel.replace("/", os.sep)
            out.parent.mkdir(parents=True, exist_ok=True)
            with zf.open(info) as src, open(out, "wb") as dst:
                dst.write(src.read())
            count += 1

    print(f"Extracted {count} files to {dest}")

    tutorial = dest / "TutorialInfo"
    if tutorial.is_dir():
        shutil.rmtree(tutorial)
        print(f"Removed duplicate {tutorial} (URP Readme template)")
    for orphan in (dest / "TutorialInfo.meta", dest / "Readme.asset", dest / "Readme.asset.meta"):
        if orphan.is_file():
            orphan.unlink()
            print(f"Removed {orphan}")

    bad = root / "SharedProject" / "streams11v2"
    if bad.is_dir():
        shutil.rmtree(bad)
        print(f"Removed broken folder: {bad}")


if __name__ == "__main__":
    main()
