#!/usr/bin/env python3
"""Report carrier tags newer than the versions pinned in carriers.json.

The pins describe the carriers the binding surfaces are currently generated
from. This script asks each carrier repository for its released tags and
compares the newest strict `vX.Y.Z` tag with the pin, so a scheduled CI job can
open an update issue without anyone watching the upstream repositories.

Tags are read with `git ls-remote`, which needs no API token and does not count
against the GitHub API rate limit.
"""

from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
from pathlib import Path

TAG = re.compile(r"^v?(\d+)\.(\d+)\.(\d+)$")
HERE = Path(__file__).resolve().parent


def version(tag: str) -> tuple[int, int, int] | None:
    match = TAG.match(tag)
    return tuple(int(part) for part in match.groups()) if match else None


def remote_tags(repository: str) -> list[str]:
    completed = subprocess.run(
        ["git", "ls-remote", "--tags", "--refs", f"https://github.com/{repository}.git"],
        capture_output=True,
        text=True,
        timeout=120,
    )
    if completed.returncode != 0:
        raise RuntimeError(completed.stderr.strip() or f"git ls-remote exited with {completed.returncode}")
    tags = []
    for line in completed.stdout.splitlines():
        _, _, ref = line.partition("\t")
        if ref.startswith("refs/tags/"):
            tags.append(ref[len("refs/tags/"):])
    return tags


def command(name: str, latest: str) -> str:
    line = f"powershell -File frontend/new-carrier.ps1 -Carrier {name} -Version {latest} -Fetch"
    if name == "levilamina":
        line += " -BuildCarrier"
    return line


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--pins", default=str(HERE / "carriers.json"))
    parser.add_argument("--github-output", help="append the outdated=true/false step output here")
    parser.add_argument("--markdown", help="write the issue body here")
    args = parser.parse_args()

    pins = json.loads(Path(args.pins).read_text(encoding="utf-8"))
    rows = []
    outdated = False
    errors = []
    for name, entry in pins.items():
        pinned = entry["tag"]
        pinned_version = version(pinned)
        if pinned_version is None:
            print(f"{name}: pinned tag {pinned!r} is not vX.Y.Z", file=sys.stderr)
            errors.append(name)
            continue
        try:
            tags = [tag for tag in remote_tags(entry["repository"]) if version(tag)]
        except (RuntimeError, subprocess.TimeoutExpired) as error:
            print(f"{name}: {error}", file=sys.stderr)
            errors.append(name)
            continue
        if not tags:
            print(f"{name}: no vX.Y.Z tags in {entry['repository']}", file=sys.stderr)
            errors.append(name)
            continue
        latest = max(tags, key=version)
        newer = version(latest) > pinned_version
        outdated = outdated or newer
        rows.append((name, entry["repository"], pinned, latest, newer))
        state = "update available" if newer else "up to date"
        print(f"{name:12} pinned {pinned:10} latest {latest:10} {state}")

    if outdated:
        lines = [
            "A carrier newer than the pinned version is available.",
            "",
            "| Carrier | Pinned | Latest |",
            "|---|---|---|",
        ]
        lines += [f"| {name} | {pinned} | {latest} |" for name, _, pinned, latest, newer in rows if newer]
        lines += ["", "Regenerate with:"]
        for name, _, _, latest, newer in rows:
            if newer:
                lines += ["", "```shell", command(name, latest), "```"]
        lines += [
            "",
            "After the regeneration is reviewed, update `frontend/carriers.json` and close this issue.",
        ]
        body = "\n".join(lines) + "\n"
        if args.markdown:
            Path(args.markdown).write_text(body, encoding="utf-8")
        else:
            print()
            print(body)

    if args.github_output:
        with open(args.github_output, "a", encoding="utf-8") as stream:
            stream.write(f"outdated={'true' if outdated else 'false'}\n")

    return 1 if errors else 0


if __name__ == "__main__":
    raise SystemExit(main())
