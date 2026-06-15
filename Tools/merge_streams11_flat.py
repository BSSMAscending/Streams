"""
Merge Assets/ImportedStreams11 into standard Assets folders (single-project layout).
"""
from __future__ import annotations

import shutil
from pathlib import Path


def find_streams() -> Path:
    desktop = Path.home() / "Desktop"
    for unity_projects in desktop.rglob("UnityProjects"):
        cand = unity_projects / "Streams"
        if (cand / "Packages" / "manifest.json").is_file():
            return cand
    raise SystemExit("UnityProjects/Streams not found under Desktop")


def move_file_with_meta(src: Path, dest_dir: Path) -> None:
    if not src.is_file():
        return
    meta_src = src.parent / (src.name + ".meta")
    dest_dir.mkdir(parents=True, exist_ok=True)

    dest = dest_dir / src.name
    if dest.exists():
        stem, ext = src.stem, src.suffix
        dest = dest_dir / f"{stem}_streams11{ext}"
        n = 2
        while dest.exists():
            dest = dest_dir / f"{stem}_streams11_{n}{ext}"
            n += 1

    shutil.move(str(src), str(dest))
    meta_dest = dest.parent / (dest.name + ".meta")
    if meta_src.exists():
        shutil.move(str(meta_src), str(meta_dest))


def move_tree_flat(src_dir: Path, dest_dir: Path) -> None:
    if not src_dir.is_dir():
        return
    for p in sorted(src_dir.iterdir()):
        if p.is_file():
            move_file_with_meta(p, dest_dir)


def main() -> None:
    root = find_streams()
    imp = root / "Assets" / "ImportedStreams11"
    if not imp.is_dir():
        raise SystemExit(f"Missing {imp}")

    assets = root / "Assets"

    for name in ("InputSystem_Actions.inputactions", "InputSystem_Actions.inputactions.meta"):
        p = imp / name
        if p.is_file():
            p.unlink()

    for orphan in (imp / "TextMesh Pro.meta",):
        if orphan.is_file():
            orphan.unlink()

    mappings: list[tuple[str, str]] = [
        ("script", "Scripts"),
        ("sprite", "Sprites"),
        ("font", "Fonts"),
        ("Scenes", "Scenes"),
        ("prefab", "Prefabs"),
        ("ani", "Animations"),
        ("_Recovery", "_Recovery"),
    ]

    for sub, target in mappings:
        d = imp / sub
        if d.is_dir():
            move_tree_flat(d, assets / target)
            shutil.rmtree(d)
        meta = imp / (sub + ".meta")
        if meta.is_file():
            meta.unlink()

    materials = assets / "Materials"
    for p in sorted(imp.iterdir()):
        if not p.is_file():
            continue
        if p.suffix.lower() in (".png", ".jpg", ".jpeg", ".webp", ".fbx"):
            move_file_with_meta(p, assets / "Sprites")
        elif p.suffix.lower() == ".mat":
            move_file_with_meta(p, materials)
        elif p.suffix.lower() == ".cs":
            move_file_with_meta(p, assets / "Scripts")

    settings = imp / "Settings"
    if settings.is_dir():
        shutil.rmtree(settings)
    sm = imp / "Settings.meta"
    if sm.is_file():
        sm.unlink()

    if imp.exists():
        shutil.rmtree(imp)
    folder_meta = assets / "ImportedStreams11.meta"
    if folder_meta.is_file():
        folder_meta.unlink()
    print("Merged ImportedStreams11 into Assets; removed import folder.")


if __name__ == "__main__":
    main()
