import json
import os
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CRATE = os.path.join(ROOT, "native", "fomoxa-rapier")
OUTPUT = os.path.join(ROOT, "com.fomoxa.networking.rapier", "Third Party Notices.md")
APACHE = os.path.join(ROOT, "LICENSE.md")


def linked_packages():
    metadata = json.loads(subprocess.check_output(["cargo", "metadata", "--format-version", "1", "--locked"], cwd=CRATE))
    packages = {package["id"]: package for package in metadata["packages"]}
    nodes = {node["id"]: node for node in metadata["resolve"]["nodes"]}
    root = metadata["resolve"]["root"]
    seen = set()
    stack = [root]
    while stack:
        current = stack.pop()
        if current in seen:
            continue
        package = packages[current]
        if any("proc-macro" in target["kind"] for target in package["targets"]):
            continue
        seen.add(current)
        for dependency in nodes[current]["deps"]:
            if any(kind["kind"] is None for kind in dependency["dep_kinds"]):
                stack.append(dependency["pkg"])
    seen.discard(root)
    return sorted((packages[package] for package in seen), key=lambda package: (package["name"], package["version"]))


def license_files(package):
    directory = os.path.dirname(package["manifest_path"])
    names = sorted(name for name in os.listdir(directory) if name.upper().startswith(("LICENSE", "LICENCE", "COPYING", "NOTICE")))
    return [(name, open(os.path.join(directory, name), encoding="utf-8", errors="replace").read().strip()) for name in names if os.path.isfile(os.path.join(directory, name))]


def main():
    packages = linked_packages()
    lines = [
        "# Third Party Notices",
        "",
        "The native libraries in `Runtime/Plugins` are built from the `fomoxa-rapier` crate and statically link the Rust crates below. Each crate is listed with its version, license and source; the license texts the crates ship follow. Crates whose packages ship no license file are covered by the Apache License 2.0 text at the end of this file.",
        "",
        "| Crate | Version | License | Source |",
        "|---|---|---|---|",
    ]
    for package in packages:
        lines.append(f"| {package['name']} | {package['version']} | {package['license']} | {package.get('repository') or ''} |")
    lines.append("")
    texts = {}
    for package in packages:
        for name, text in license_files(package):
            texts.setdefault(text, []).append(f"{package['name']} {package['version']} ({name})")
    for text, owners in texts.items():
        if "Apache License" in text[:300] and "Version 2.0" in text[:300]:
            continue
        lines.append("## " + ", ".join(owners))
        lines.append("")
        lines.append("```")
        lines.append(text)
        lines.append("```")
        lines.append("")
    lines.append("## Apache License 2.0")
    lines.append("")
    lines.append("```")
    lines.append(open(APACHE, encoding="utf-8").read().strip())
    lines.append("```")
    with open(OUTPUT, "w", encoding="utf-8", newline="\n") as output:
        output.write("\n".join(lines) + "\n")
    print(f"{len(packages)} crates written to {OUTPUT}")


if __name__ == "__main__":
    sys.exit(main())
