#!/usr/bin/env python3

from __future__ import annotations

import argparse
import html
import re
import sys
from dataclasses import dataclass
from pathlib import Path

try:
    import yaml  # type: ignore
except Exception:  # pragma: no cover - optional dependency
    yaml = None


@dataclass(frozen=True)
class SkillMetadata:
    name: str
    description: str
    skill_md: Path


class SkillMetadataError(Exception):
    pass


def _parse_frontmatter(skill_md: Path) -> tuple[str, str]:
    text = skill_md.read_text(encoding="utf-8")
    lines = text.splitlines()
    if not lines or lines[0].strip() != "---":
        raise SkillMetadataError("Missing YAML frontmatter start ('---') at line 1.")

    end_index = None
    for i in range(1, len(lines)):
        if lines[i].strip() == "---":
            end_index = i
            break
    if end_index is None:
        raise SkillMetadataError("Missing YAML frontmatter end ('---').")

    frontmatter_text = "\n".join(lines[1:end_index]).strip()

    if yaml is not None:
        try:
            data = yaml.safe_load(frontmatter_text) or {}
        except Exception as e:
            raise SkillMetadataError(f"Invalid YAML frontmatter: {e}") from e

        if not isinstance(data, dict):
            raise SkillMetadataError("YAML frontmatter must be a mapping (key/value object).")

        name_value = data.get("name")
        description_value = data.get("description")

        if not isinstance(name_value, str) or not name_value.strip():
            raise SkillMetadataError("Missing YAML 'name'.")
        if not isinstance(description_value, str) or not description_value.strip():
            raise SkillMetadataError("Missing YAML 'description'.")

        if "\n" in name_value or "\r" in name_value:
            raise SkillMetadataError("YAML 'name' must be a single line.")
        if "\n" in description_value or "\r" in description_value:
            raise SkillMetadataError("YAML 'description' must be a single line.")

        return name_value, description_value

    # Fallback parser (no PyYAML installed): only supports single-line `key: value` scalars.
    name_value: str | None = None
    description_value: str | None = None

    for raw in lines[1:end_index]:
        line = raw.strip()
        if not line or line.startswith("#"):
            continue

        match = re.match(r"^([A-Za-z0-9_-]+):\s*(.*)$", line)
        if not match:
            continue
        key, value = match.group(1), match.group(2).strip()

        if value in {"|", ">", "|-", ">-"}:
            raise SkillMetadataError(
                f"Unsupported multi-line YAML value for '{key}'. Use a single-line scalar."
            )

        if len(value) >= 2 and value[0] in {"'", '"'} and value[-1] == value[0]:
            value = value[1:-1]

        if key == "name":
            name_value = value
        elif key == "description":
            description_value = value

    if not name_value:
        raise SkillMetadataError("Missing YAML 'name'.")
    if not description_value:
        raise SkillMetadataError("Missing YAML 'description'.")

    return name_value, description_value


def _collect_skills(skills_root: Path) -> list[SkillMetadata]:
    skills: list[SkillMetadata] = []
    for child in sorted(skills_root.iterdir()):
        if not child.is_dir():
            continue
        skill_md = child / "SKILL.md"
        if not skill_md.exists():
            continue
        name, description = _parse_frontmatter(skill_md)
        skills.append(SkillMetadata(name=name, description=description, skill_md=skill_md))
    return sorted(skills, key=lambda s: s.name)


def _detect_skills_dir() -> str:
    candidates = (Path(".codex/skills"), Path(".claude/skills"), Path("skills"))

    def contains_skills(dir_path: Path) -> bool:
        if not dir_path.exists() or not dir_path.is_dir():
            return False
        for child in dir_path.iterdir():
            if not child.is_dir():
                continue
            if (child / "SKILL.md").is_file():
                return True
        return False

    for candidate in candidates:
        if contains_skills(candidate):
            return str(candidate)

    for candidate in candidates:
        if candidate.exists() and candidate.is_dir():
            return str(candidate)

    return "skills"


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Generate an <available_skills> XML block from */SKILL.md metadata under a skills directory."
    )
    parser.add_argument(
        "skills_dir",
        nargs="?",
        default=_detect_skills_dir(),
        help="Path to skills directory (default: auto-detect .codex/skills, .claude/skills, or ./skills).",
    )
    parser.add_argument(
        "--absolute",
        action="store_true",
        help="Use absolute paths in <location> (recommended for filesystem-based agents).",
    )
    args = parser.parse_args()

    skills_root = Path(args.skills_dir).resolve()
    if not skills_root.exists() or not skills_root.is_dir():
        print(f"ERROR: Skills directory does not exist: {skills_root}", file=sys.stderr)
        return 2

    try:
        skills = _collect_skills(skills_root)
    except SkillMetadataError as e:
        print(f"ERROR: {e}", file=sys.stderr)
        return 2

    if not skills:
        print(f"ERROR: No skills found under: {skills_root}", file=sys.stderr)
        return 2

    print("<available_skills>")
    for skill in skills:
        location = str(skill.skill_md.resolve() if args.absolute else skill.skill_md.relative_to(Path.cwd()))
        print("  <skill>")
        print(f"    <name>{html.escape(skill.name)}</name>")
        print(f"    <description>{html.escape(skill.description)}</description>")
        print(f"    <location>{html.escape(location)}</location>")
        print("  </skill>")
    print("</available_skills>")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
