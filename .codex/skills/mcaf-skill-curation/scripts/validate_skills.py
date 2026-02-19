#!/usr/bin/env python3

from __future__ import annotations

import argparse
import re
import sys
from dataclasses import dataclass
from pathlib import Path

try:
    import yaml  # type: ignore
except Exception:  # pragma: no cover - optional dependency
    yaml = None


SKILL_NAME_RE = re.compile(r"^[a-z0-9]+(-[a-z0-9]+)*$")
MAX_NAME_LEN = 64
MAX_DESCRIPTION_LEN = 1024

FORBIDDEN_FILES = {
    "README.md",
    "CHANGELOG.md",
    "INSTALLATION_GUIDE.md",
    "QUICK_REFERENCE.md",
}


@dataclass(frozen=True)
class Frontmatter:
    name: str | None
    description: str | None


class SkillValidationError(Exception):
    pass


def _parse_frontmatter(skill_md: Path) -> Frontmatter:
    text = skill_md.read_text(encoding="utf-8")
    lines = text.splitlines()
    if not lines or lines[0].strip() != "---":
        raise SkillValidationError("Missing YAML frontmatter start ('---') at line 1.")

    end_index = None
    for i in range(1, len(lines)):
        if lines[i].strip() == "---":
            end_index = i
            break
    if end_index is None:
        raise SkillValidationError("Missing YAML frontmatter end ('---').")

    frontmatter_text = "\n".join(lines[1:end_index]).strip()

    if yaml is not None:
        try:
            data = yaml.safe_load(frontmatter_text) or {}
        except Exception as e:
            raise SkillValidationError(f"Invalid YAML frontmatter: {e}") from e

        if not isinstance(data, dict):
            raise SkillValidationError("YAML frontmatter must be a mapping (key/value object).")

        name_value = data.get("name")
        description_value = data.get("description")

        if name_value is not None and not isinstance(name_value, str):
            raise SkillValidationError("YAML 'name' must be a string.")
        if description_value is not None and not isinstance(description_value, str):
            raise SkillValidationError("YAML 'description' must be a string.")

        # Enforce single-line scalars (Codex expects concise metadata).
        if isinstance(name_value, str) and ("\n" in name_value or "\r" in name_value):
            raise SkillValidationError("YAML 'name' must be a single line.")
        if isinstance(description_value, str) and ("\n" in description_value or "\r" in description_value):
            raise SkillValidationError("YAML 'description' must be a single line.")

        return Frontmatter(name=name_value, description=description_value)

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
            raise SkillValidationError(
                f"Unsupported multi-line YAML value for '{key}'. Use a single-line scalar."
            )

        if len(value) >= 2 and value[0] in {"'", '"'} and value[-1] == value[0]:
            value = value[1:-1]

        if key == "name":
            name_value = value
        elif key == "description":
            description_value = value

    return Frontmatter(name=name_value, description=description_value)


def _validate_name(value: str, *, skill_dir_name: str) -> list[str]:
    errors: list[str] = []

    if not value:
        errors.append("YAML 'name' is empty.")
        return errors

    if len(value) > MAX_NAME_LEN:
        errors.append(f"YAML 'name' is too long ({len(value)} > {MAX_NAME_LEN}).")

    if not SKILL_NAME_RE.fullmatch(value):
        errors.append(
            "YAML 'name' must match ^[a-z0-9]+(-[a-z0-9]+)*$ (lowercase, digits, hyphens; no leading/trailing hyphen; no '--')."
        )

    if value != skill_dir_name:
        errors.append(f"YAML 'name' ('{value}') must match folder name ('{skill_dir_name}').")

    return errors


def _validate_description(value: str) -> list[str]:
    errors: list[str] = []

    if not value or not value.strip():
        errors.append("YAML 'description' is missing or empty.")
        return errors

    if len(value) > MAX_DESCRIPTION_LEN:
        errors.append(
            f"YAML 'description' is too long ({len(value)} > {MAX_DESCRIPTION_LEN})."
        )

    return errors


def _find_skill_dirs(skills_root: Path) -> list[Path]:
    if not skills_root.exists():
        raise SkillValidationError(f"Skills directory does not exist: {skills_root}")
    if not skills_root.is_dir():
        raise SkillValidationError(f"Skills path is not a directory: {skills_root}")

    skill_dirs: list[Path] = []
    for child in sorted(skills_root.iterdir()):
        if not child.is_dir():
            continue
        if (child / "SKILL.md").exists():
            skill_dirs.append(child)
    return skill_dirs


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
    parser = argparse.ArgumentParser(description="Validate Agent Skills under a directory (for example: .codex/skills).")
    parser.add_argument(
        "skills_dir",
        nargs="?",
        default=_detect_skills_dir(),
        help="Path to skills directory (default: auto-detect .codex/skills, .claude/skills, or ./skills).",
    )
    args = parser.parse_args()

    skills_root = Path(args.skills_dir).resolve()

    try:
        skill_dirs = _find_skill_dirs(skills_root)
    except SkillValidationError as e:
        print(f"ERROR: {e}", file=sys.stderr)
        return 2

    if not skill_dirs:
        print(f"No skills found under: {skills_root}", file=sys.stderr)
        return 2

    errors_count = 0
    warnings_count = 0

    for skill_dir in skill_dirs:
        skill_md = skill_dir / "SKILL.md"
        rel = skill_md.relative_to(skills_root.parent) if skills_root.parent in skill_md.parents else skill_md
        print(f"\n==> {rel}")

        try:
            fm = _parse_frontmatter(skill_md)
        except SkillValidationError as e:
            errors_count += 1
            print(f"ERROR: {e}", file=sys.stderr)
            continue

        if fm.name is None:
            errors_count += 1
            print("ERROR: YAML 'name' is missing.", file=sys.stderr)
        else:
            name_errors = _validate_name(fm.name, skill_dir_name=skill_dir.name)
            for err in name_errors:
                errors_count += 1
                print(f"ERROR: {err}", file=sys.stderr)

        if fm.description is None:
            errors_count += 1
            print("ERROR: YAML 'description' is missing.", file=sys.stderr)
        else:
            description_errors = _validate_description(fm.description)
            for err in description_errors:
                errors_count += 1
                print(f"ERROR: {err}", file=sys.stderr)

        # Additional hygiene checks.
        forbidden_present = []
        for name in FORBIDDEN_FILES:
            if (skill_dir / name).exists():
                forbidden_present.append(name)
        if forbidden_present:
            warnings_count += len(forbidden_present)
            print(
                f"WARNING: Remove extra docs from skill folder: {', '.join(sorted(forbidden_present))}",
                file=sys.stderr,
            )

    print("\n---")
    print(f"Skills validated: {len(skill_dirs)}")
    print(f"Errors: {errors_count}")
    print(f"Warnings: {warnings_count}")

    return 1 if errors_count else 0


if __name__ == "__main__":
    raise SystemExit(main())
