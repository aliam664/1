#!/usr/bin/env python3
"""AC Mod Hub 2.0 — static validation suite.

Runs without the .NET SDK: XML/XAML well-formedness, JSON validity, JavaScript syntax
(node), YAML validity (PyYAML when available), HTML sanity, localization key parity,
version single-sourcing, resource reference integrity, and hygiene checks.

Exit code is non-zero when any check fails. Used locally and by .github/workflows/ci.yml.
"""
import glob
import json
import os
import re
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
FAILURES: list[str] = []
NOTES: list[str] = []


def fail(message: str) -> None:
    FAILURES.append(message)
    print(f"  FAIL  {message}")


def ok(message: str) -> None:
    print(f"  ok    {message}")


def note(message: str) -> None:
    NOTES.append(message)
    print(f"  note  {message}")


def text_files(patterns):
    for pattern in patterns:
        for path in glob.glob(str(ROOT / pattern), recursive=True):
            p = Path(path)
            if ".git" in p.parts:
                continue
            yield p


def check_xml_wellformed() -> None:
    print("[1] XML / XAML well-formedness")
    count = 0
    for path in text_files(["src/**/*.xaml", "src/**/*.csproj", "src/**/*.manifest", "installer/*.iss"]):
        try:
            if path.suffix == ".iss":
                continue  # Inno script is not XML
            ET.parse(path)
            count += 1
        except ET.ParseError as ex:
            fail(f"{path}: {ex}")
    ok(f"parsed {count} XML/XAML files")


def check_json_valid() -> None:
    print("[2] JSON validity")
    count = 0
    for path in text_files(["**/*.json"]):
        try:
            json.loads(path.read_text(encoding="utf-8"))
            count += 1
        except json.JSONDecodeError as ex:
            fail(f"{path}: {ex}")
    ok(f"parsed {count} JSON files")


def check_embedded_catalog() -> None:
    print("[3] Embedded catalog schema")
    path = ROOT / "assets" / "catalog.v1.json"
    data = json.loads(path.read_text(encoding="utf-8"))
    if data.get("schemaVersion") != 1:
        fail("embedded catalog schemaVersion != 1")
    if not isinstance(data.get("mods"), list):
        fail("embedded catalog 'mods' is not an array")
    if data.get("mods"):
        fail("embedded catalog must ship empty (no fake mods)")
    if data.get("repository") != "aliam664/Data":
        fail("embedded catalog repository mismatch")
    if data.get("minimumLauncherVersion") != "2.0.0":
        fail("embedded catalog minimumLauncherVersion mismatch")
    ok("empty, schema v1, repository aliam664/Data")


def check_js_syntax() -> None:
    print("[4] JavaScript syntax (node --check)")
    node = shutil_which("node")
    if node is None:
        note("node is not available; JavaScript syntax check skipped")
        return
    for path in text_files(["src/**/*.js"]):
        result = subprocess.run([node, "--check", str(path)], capture_output=True, text=True)
        if result.returncode != 0:
            fail(f"{path}: {result.stderr.strip()}")
        else:
            ok(f"{path} syntax valid")


def shutil_which(name):
    import shutil
    return shutil.which(name)


def check_html_sanity() -> None:
    print("[5] HTML template sanity")
    from html.parser import HTMLParser

    class Parser(HTMLParser):
        pass

    for path in text_files(["src/**/*.html"]):
        content = path.read_text(encoding="utf-8")
        parser = Parser()
        try:
            parser.feed(content)
        except Exception as ex:  # noqa: BLE001
            fail(f"{path}: HTML parse error: {ex}")
            continue
        for placeholder in ("{{CSS}}", "{{JS}}", "{{DATA}}"):
            if placeholder not in content:
                fail(f"{path}: missing placeholder {placeholder}")
        ok(f"{path} parses with expected placeholders")


def check_yaml() -> None:
    print("[6] Workflow YAML validity")
    try:
        import yaml  # type: ignore
    except ImportError:
        note("PyYAML is not installed; YAML validity check skipped (CI installs it)")
        return
    for path in text_files([".github/workflows/*.yml", ".github/workflows/*.yaml"]):
        try:
            with open(path, encoding="utf-8") as handle:
                list(yaml.safe_load_all(handle))
            ok(f"{path} valid YAML")
        except yaml.YAMLError as ex:
            fail(f"{path}: {ex}")


def check_localization_parity() -> None:
    print("[7] Localization key parity (fa-IR / en-US)")
    keys = {}
    for lang in ("fa-IR", "en-US"):
        path = ROOT / "src" / "ACModHub.UI" / "Resources" / f"Strings.{lang}.xaml"
        content = path.read_text(encoding="utf-8")
        found = re.findall(r'x:Key="([^"]+)"', content)
        keys[lang] = (found, content)
    fa, en = keys["fa-IR"][0], keys["en-US"][0]
    if len(set(fa)) != len(fa):
        fail("duplicate keys in Strings.fa-IR.xaml")
    if len(set(en)) != len(en):
        fail("duplicate keys in Strings.en-US.xaml")
    missing_en = sorted(set(fa) - set(en))
    missing_fa = sorted(set(en) - set(fa))
    if missing_en:
        fail(f"keys missing in en-US: {missing_en}")
    if missing_fa:
        fail(f"keys missing in fa-IR: {missing_fa}")
    # Every localized key referenced via DynamicResource must exist.
    dyn = set()
    for path in text_files(["src/**/*.xaml"]):
        for match in re.findall(r'\{DynamicResource\s+([A-Za-z0-9_.]+)\}', path.read_text(encoding="utf-8")):
            dyn.add(match)
    missing_dyn = sorted(k for k in dyn if k not in set(fa))
    if missing_dyn:
        fail(f"DynamicResource keys not present in string tables: {missing_dyn}")
    ok(f"{len(set(fa))} keys per language; all DynamicResource references resolve")


def check_version_single_source() -> None:
    print("[8] Version single-sourcing (2.0.0-preview.1)")
    props = (ROOT / "Directory.Build.props").read_text(encoding="utf-8")
    match = re.search(r"<Version>([^<]+)</Version>", props)
    if not match:
        fail("Directory.Build.props has no <Version>")
        return
    version = match.group(1)
    bad = []
    for path in text_files(["src/**/*.csproj", "installer/*.iss", "src/**/*.manifest", "assets/**/*.json"]):
        content = path.read_text(encoding="utf-8")
        if re.search(r"\d+\.\d+\.\d+(-[A-Za-z0-9.]+)?", content) and "<Version>" not in content:
            # allow assembly identity (2.0.0.0) and manifest identity
            pass
        if re.search(r"\b2\.0\.0-preview\.1\b", content) and path.suffix not in (".json",):
            bad.append(str(path.relative_to(ROOT)))
        if "1.0.1" in content or "1.0.0-preview" in content:
            bad.append(f"{path.relative_to(ROOT)} (stale 1.x version)")
    if bad:
        fail("hard-coded versions outside the single source: " + ", ".join(sorted(set(bad))))
    else:
        ok(f"version '{version}' sourced only from Directory.Build.props")


def check_static_resources() -> None:
    print("[9] StaticResource references resolve against the central dictionaries")
    pool = set()
    for path in text_files([
        "src/ACModHub.UI/Resources/Theme.xaml",
        "src/ACModHub.UI/Resources/Icons.xaml",
        "src/ACModHub.UI/Resources/Strings.fa-IR.xaml",
        "src/ACModHub.UI/App.xaml",
    ]):
        content = path.read_text(encoding="utf-8")
        pool.update(re.findall(r'x:Key="([^"]+)"', content))
    skip = {"{x:Type Button}", "{x:Type TextBlock}"}
    problems = []
    for path in text_files(["src/**/*.xaml"]):
        content = path.read_text(encoding="utf-8")
        local = set(re.findall(r'x:Key="([^"]+)"', content))
        referenced = set(re.findall(r'\{StaticResource\s+([^}]+)\}', content))
        for ref in referenced:
            if ref in skip or ref in local or ref in pool:
                continue
            if ref.startswith("{x:Type"):
                continue
            problems.append(f"{path.relative_to(ROOT)}: {ref}")
    if problems:
        fail("unresolved StaticResource keys: " + "; ".join(sorted(set(problems))[:20]))
    else:
        ok("all StaticResource references resolve")


def check_hygiene() -> None:
    print("[10] Hygiene: no TODO/pseudo-code, no blocking calls, no fake artifacts")
    for path in text_files(["src/**/*.cs"]):
        content = path.read_text(encoding="utf-8")
        if re.search(r"\bTODO\b|\bFIXME\b|NotImplementedException|throw new Exception\(\)", content):
            fail(f"{path}: TODO/FIXME/NotImplemented found")
        if re.search(r"\.Result\b|\b\.Wait\(\)", content):
            fail(f"{path}: blocking .Result/.Wait() found")
    for path in text_files(["src/**/*.cs"]):
        if re.search(r"GetAwaiter\(\)\.GetResult\(\)", path.read_text(encoding="utf-8")):
            fail(f"{path}: GetAwaiter().GetResult() found")
    fake_exes = list(text_files(["releases/**/*.exe", "releases/**/*.msi"]))
    if fake_exes:
        fail("placeholder binaries in releases/: " + ", ".join(str(p) for p in fake_exes))
    else:
        ok("no TODO/pseudo-code, no blocking calls, no placeholder EXEs")


def check_trailing_whitespace() -> None:
    print("[11] Trailing whitespace (git diff --check equivalent)")
    bad = []
    for path in text_files(["**/*.cs", "**/*.xaml", "**/*.md", "**/*.yml", "**/*.json", "**/*.py", "**/*.ps1", "**/*.iss", "**/*.html", "**/*.css", "**/*.js", "**/*.props", "**/*.targets", "**/*.sln", "**/*.manifest", "**/*.gitignore"]):
        for lineno, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
            if line.endswith((" ", "\t")):
                bad.append(f"{path.relative_to(ROOT)}:{lineno}")
    if bad:
        fail("trailing whitespace at: " + ", ".join(bad[:20]))
    else:
        ok("no trailing whitespace")


def check_css_braces() -> None:
    print("[12] CSS brace balance")
    for path in text_files(["src/**/*.css"]):
        content = path.read_text(encoding="utf-8")
        if content.count("{") != content.count("}"):
            fail(f"{path}: unbalanced braces")
        else:
            ok(f"{path} balanced")


def main() -> int:
    check_xml_wellformed()
    check_json_valid()
    check_embedded_catalog()
    check_js_syntax()
    check_html_sanity()
    check_yaml()
    check_localization_parity()
    check_version_single_source()
    check_static_resources()
    check_hygiene()
    check_trailing_whitespace()
    check_css_braces()
    print()
    if FAILURES:
        print(f"{len(FAILURES)} failure(s).")
        return 1
    print("All static validation checks passed.")
    for entry in NOTES:
        print(f"note: {entry}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
