#!/usr/bin/env python3
"""Generate or update the Jellyfin plugin repository manifest."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import xml.etree.ElementTree as ET
from datetime import datetime, timezone
from pathlib import Path


SCALAR_PATTERN = re.compile(r"^(?P<key>[A-Za-z][A-Za-z0-9_]*):\s*(?P<value>.*)$")


def parse_scalar(value: str) -> str:
    value = value.strip()
    if len(value) >= 2 and value[0] == value[-1] and value[0] in ("'", '"'):
        return value[1:-1]
    return value


def read_build_metadata(path: Path) -> dict[str, str]:
    lines = path.read_text(encoding="utf-8").splitlines()
    metadata: dict[str, str] = {}
    index = 0

    while index < len(lines):
        match = SCALAR_PATTERN.match(lines[index])
        if not match:
            index += 1
            continue

        key = match.group("key")
        value = match.group("value").strip()
        if value in ("|", "|-", ">", ">-"):
            block: list[str] = []
            index += 1
            while index < len(lines):
                line = lines[index]
                if line and not line.startswith((" ", "\t")):
                    break
                block.append(line[2:] if line.startswith("  ") else line.lstrip())
                index += 1
            metadata[key] = "\n".join(block).rstrip()
            continue

        metadata[key] = parse_scalar(value)
        index += 1

    required = (
        "name",
        "guid",
        "targetAbi",
        "owner",
        "overview",
        "description",
        "category",
        "changelog",
    )
    missing = [key for key in required if not metadata.get(key)]
    if missing:
        raise ValueError(f"build.yaml is missing required fields: {', '.join(missing)}")

    return metadata


def read_project_version(path: Path) -> str:
    root = ET.parse(path).getroot()
    version = root.findtext("./PropertyGroup/Version")
    if not version or not version.strip():
        raise ValueError(f"Version was not found in {path}")
    return version.strip()


def md5sum(path: Path) -> str:
    digest = hashlib.md5()  # Jellyfin repository checksums use MD5.
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def load_existing_manifest(path: Path | None) -> list[dict[str, object]]:
    if path is None or not path.exists():
        return []

    value = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(value, list):
        raise ValueError("Existing manifest must contain a JSON array")
    return value


def build_manifest(args: argparse.Namespace) -> list[dict[str, object]]:
    root = Path(__file__).resolve().parent.parent
    build = read_build_metadata(root / "build.yaml")
    version = read_project_version(root / "Jellyfin.Plugin.ThunderCat.csproj")
    zip_path = Path(args.zip).resolve()
    if not zip_path.is_file():
        raise FileNotFoundError(zip_path)

    tag = args.tag or f"v{version}"
    if tag != f"v{version}":
        raise ValueError(f"Tag {tag!r} does not match project version {version!r}")
    if not re.fullmatch(r"[^/\s]+/[^/\s]+", args.repository):
        raise ValueError("Repository must use the owner/name form")

    timestamp = args.timestamp or (
        datetime.now(timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")
    )
    version_entry: dict[str, object] = {
        "checksum": md5sum(zip_path),
        "changelog": build["changelog"],
        "targetAbi": build["targetAbi"],
        "sourceUrl": (
            f"https://github.com/{args.repository}/releases/download/"
            f"{tag}/{zip_path.name}"
        ),
        "timestamp": timestamp,
        "version": version,
    }

    manifest = load_existing_manifest(
        Path(args.existing_manifest).resolve() if args.existing_manifest else None
    )
    plugin_entry = next(
        (
            item
            for item in manifest
            if isinstance(item, dict) and item.get("guid") == build["guid"]
        ),
        None,
    )
    if plugin_entry is None:
        plugin_entry = {
            "guid": build["guid"],
            "name": build["name"],
            "description": build["description"],
            "overview": build["overview"],
            "owner": build["owner"],
            "category": build["category"],
            "versions": [],
        }
        manifest.insert(0, plugin_entry)

    versions = plugin_entry.get("versions")
    if not isinstance(versions, list):
        versions = []
        plugin_entry["versions"] = versions
    versions[:] = [
        item
        for item in versions
        if not isinstance(item, dict) or item.get("version") != version
    ]
    versions.insert(0, version_entry)
    return manifest


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--zip", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--repository", required=True)
    parser.add_argument("--tag")
    parser.add_argument("--timestamp")
    parser.add_argument("--existing-manifest")
    args = parser.parse_args()

    manifest = build_manifest(args)
    output = Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(
        json.dumps(manifest, indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8",
    )
    print(output)


if __name__ == "__main__":
    main()
