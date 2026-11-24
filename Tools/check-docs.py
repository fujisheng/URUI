#!/usr/bin/env python3
"""Validate URUI bilingual documentation parity, links, images, and stale terms."""

from __future__ import annotations

import pathlib
import re
import sys
import urllib.parse

REPO_ROOT = pathlib.Path(__file__).resolve().parents[1]

EXEMPT_SINGLE_LANG = {
    pathlib.Path("AGENTS.md"),
    pathlib.Path("Packages/com.fujisheng.urui/CHANGELOG.md"),
    pathlib.Path("Packages/com.fujisheng.urui/LICENSE.md"),
    pathlib.Path("Packages/com.fujisheng.urui/Third Party Notices.md"),
    pathlib.Path("Packages/com.fujisheng.urui/Documentation~/Third Party Notices.md"),
    pathlib.Path("Packages/com.fujisheng.urui/Native~/Engine/UPSTREAM.md"),
    pathlib.Path("Packages/com.fujisheng.urui.authoring/LICENSE.md"),
    pathlib.Path("Packages/com.fujisheng.urui.effect/ThirdPartyNotices.md"),
}

CJK_RE = re.compile(r"[\u4e00-\u9fff\u3000-\u303f\uff00-\uffef]")
SWITCH_LINK_RE = re.compile(r"(?:\[[^\]]*中文[^\]]*\]\([^)]+\)|\|\s*中文\s*\|)")
LINK_RE = re.compile(r"!?\[[^\]]*\]\(([^)]+)\)")

FORBIDDEN_PATTERNS = [
    ("ArtOfTD", re.compile(r"ArtOfTD")),
    ("/Volumes/FData", re.compile(r"/Volumes/FData")),
    ("yefanhua", re.compile(r"yefanhua")),
    ("docs/superpowers", re.compile(r"docs/superpowers")),
    ("Game Flow", re.compile(r"Game\s+Flow", re.IGNORECASE)),
    ("GameFlow", re.compile(r"GameFlow", re.IGNORECASE)),
    ("Kraft Studio", re.compile(r"Kraft\s+Studio", re.IGNORECASE)),
    ("KraftStudio", re.compile(r"KraftStudio", re.IGNORECASE)),
    ("CatalogLab", re.compile(r"Catalog\s*Lab", re.IGNORECASE)),
    ("North / 07", re.compile(r"North\s*/\s*07")),
    ("2532", re.compile(r"\b2532\b")),
    ("ABI8-12", re.compile(r"\bABI(?:[89]|1[012])\b")),
    ("render-plan1-8", re.compile(r"\brender-plan[1-8]\b")),
    ("artifacts/urui-", re.compile(r"artifacts/urui-")),
]


def iter_doc_markdown_files() -> list[pathlib.Path]:
    files: set[pathlib.Path] = set()
    for p in REPO_ROOT.glob("*.md"):
        files.add(p)
    docs_dir = REPO_ROOT / "docs"
    if docs_dir.is_dir():
        files.update(docs_dir.rglob("*.md"))
    for pkg in (REPO_ROOT / "Packages").glob("com.fujisheng.*"):
        for p in pkg.glob("*.md"):
            files.add(p)
        doc_tilde = pkg / "Documentation~"
        if doc_tilde.is_dir():
            files.update(doc_tilde.rglob("*.md"))
        for sub_name in ("Samples~", "Tests~"):
            sub_dir = pkg / sub_name
            if sub_dir.is_dir():
                for child in sub_dir.iterdir():
                    if child.is_dir():
                        files.update(child.glob("README*.md"))
        upstream = pkg / "Native~/Engine/UPSTREAM.md"
        if upstream.is_file():
            files.add(upstream)
    return sorted(files)


def strip_fenced_blocks(text: str) -> str:
    lines = []
    in_fence = False
    for line in text.splitlines():
        stripped = line.lstrip()
        if stripped.startswith("```") or stripped.startswith("~~~"):
            in_fence = not in_fence
            lines.append("")
            continue
        lines.append("" if in_fence else line)
    return "\n".join(lines)


def main() -> int:
    errors: list[str] = []
    files = iter_doc_markdown_files()
    rel_set = {f.relative_to(REPO_ROOT) for f in files}

    # 1. Check 1-to-1 EN <-> _ZH.md pairing
    for rel in sorted(rel_set):
        if rel in EXEMPT_SINGLE_LANG:
            continue
        name = rel.name
        if name.endswith("_ZH.md"):
            en_rel = rel.with_name(name[:-6] + ".md")
            if en_rel not in rel_set:
                errors.append(f"[PAIR] Missing English counterpart for {rel}: expected {en_rel}")
        elif name.endswith(".md"):
            zh_rel = rel.with_name(name[:-3] + "_ZH.md")
            if zh_rel not in rel_set:
                errors.append(f"[PAIR] Missing Chinese counterpart for {rel}: expected {zh_rel}")

    # 2, 3, 4. Check links, CJK in EN docs, and forbidden patterns
    for path in files:
        rel = path.relative_to(REPO_ROOT)
        if rel == pathlib.Path("AGENTS.md"):
            continue
        text = path.read_text(encoding="utf-8")

        # Forbidden stale terms (scan full text except AGENTS.md already skipped)
        for line_no, line in enumerate(text.splitlines(), 1):
            for label, pat in FORBIDDEN_PATTERNS:
                if pat.search(line):
                    errors.append(f"[STALE:{label}] {rel}:{line_no}: {line.strip()}")

        # Stray CJK in English documents
        if not path.name.endswith("_ZH.md") and rel not in EXEMPT_SINGLE_LANG:
            for line_no, line in enumerate(text.splitlines(), 1):
                cleaned = SWITCH_LINK_RE.sub("", line)
                if CJK_RE.search(cleaned):
                    errors.append(f"[CJK_IN_EN] {rel}:{line_no}: {line.strip()}")

        # Relative links & images (outside fenced code blocks)
        prose = strip_fenced_blocks(text)
        for line_no, line in enumerate(prose.splitlines(), 1):
            # Also drop inline code spans before parsing links
            line_no_code = re.sub(r"`[^`]+`", "", line)
            for match in LINK_RE.finditer(line_no_code):
                raw_target = match.group(1).strip()
                if not raw_target:
                    continue
                if raw_target.startswith("<") and raw_target.endswith(">"):
                    raw_target = raw_target[1:-1].strip()
                if " " in raw_target and not (path.parent / urllib.parse.unquote(raw_target.split("#", 1)[0])).exists():
                    # Support optional title after whitespace: path "title"
                    maybe_title = raw_target.split(' "', 1)[0].split(" '", 1)[0].strip()
                    raw_target = maybe_title
                if raw_target.startswith(("http://", "https://", "mailto:", "file://", "#")):
                    continue
                target_no_anchor = raw_target.split("#", 1)[0].split("?", 1)[0]
                if not target_no_anchor:
                    continue
                decoded = urllib.parse.unquote(target_no_anchor)
                resolved = (path.parent / decoded).resolve()
                if not resolved.exists():
                    errors.append(f"[BROKEN_LINK] {rel}:{line_no}: {raw_target} -> {resolved}")

    print(f"Checked {len(files)} markdown files across URUI.")
    if errors:
        print(f"Found {len(errors)} documentation issue(s):")
        for e in errors:
            print(" ", e)
        return 1
    print("OK: all bilingual pairs, relative links, images, and hygiene checks passed.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
