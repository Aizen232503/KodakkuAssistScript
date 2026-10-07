"""Generate OnlineRepo.json from local C# scripts, without executing them."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import sys
import tempfile
from urllib.parse import quote
from uuid import UUID


TOKEN = re.compile(
    r'(?P<space>\s+)|(?P<comment>//[^\r\n]*|/\*.*?\*/)'  # Comments are not metadata values.
    r'|(?P<string>@"(?:""|[^"])*"|"(?:\\.|[^"\\])*")'
    r'|(?P<number>0[xX][\da-fA-F_]+|\d[\d_]*)'
    r'|(?P<identifier>[^\W\d]\w*)|(?P<punctuation>.)',
    re.DOTALL,
)
SKIP_DIRECTORIES = {".git", ".vs", "bin", "obj", "utils", "__pycache__"}


def is_public(source, file):
    """Only a leading // OnlineRepo: false comment opts a script out."""
    flags = []
    for token in TOKEN.finditer(source):
        if token.lastgroup == "space":
            continue
        if token.lastgroup != "comment":
            break
        comment = token.group()
        match = re.fullmatch(r"//\s*OnlineRepo\s*:\s*(.*?)\s*", comment, re.IGNORECASE)
        if match:
            value = match.group(1).lower()
            if value not in {"true", "false"}:
                raise ValueError(f"{file}: OnlineRepo must be true or false.")
            flags.append(value == "true")
    if len(flags) > 1:
        raise ValueError(f"{file}: duplicate OnlineRepo comments.")
    return flags[0] if flags else True


def split_arguments(tokens, separator):
    parts, current, depth = [], [], 0
    for kind, value in tokens:
        if kind == "punctuation":
            if value in "([{":
                depth += 1
            elif value in ")]}":
                depth -= 1
            if value == separator and depth == 0:
                parts.append(current)
                current = []
                continue
        current.append((kind, value))
    if current:
        parts.append(current)
    return parts


def parse_script(source, file):
    tokens = [(token.lastgroup, token.group()) for token in TOKEN.finditer(source)
              if token.lastgroup not in {"space", "comment"}]
    attributes = []
    for index, token in enumerate(tokens[:-1]):
        if token[0] != "identifier" or token[1] not in {"ScriptType", "ScriptTypeAttribute"}:
            continue
        if tokens[index + 1] != ("punctuation", "("):
            continue
        depth = 1
        for end in range(index + 2, len(tokens)):
            if tokens[end] == ("punctuation", "("):
                depth += 1
            elif tokens[end] == ("punctuation", ")"):
                depth -= 1
                if depth == 0:
                    if tokens[end + 1:end + 2] == [("punctuation", "]")]:
                        attributes.append(tokens[index + 2:end])
                    break
    if not attributes:
        return None  # Ordinary C# helper files are not scripts.
    if len(attributes) != 1:
        raise ValueError(f"{file}: expected one ScriptType attribute.")

    arguments = {}
    for part in split_arguments(attributes[0], ","):
        if len(part) >= 3 and part[1] == ("punctuation", ":"):
            arguments[part[0][1]] = part[2:]

    # Resolve simple string/integer constants used by ScriptType parameters.
    constants = {}
    for index in range(len(tokens) - 4):
        if tokens[index] != ("identifier", "const") or tokens[index + 1][1] not in {"string", "int"}:
            continue
        if tokens[index + 3] != ("punctuation", "="):
            continue
        end = index + 4
        while end < len(tokens) and tokens[end] != ("punctuation", ";"):
            end += 1
        constants[tokens[index + 2][1]] = tokens[index + 4:end]

    def scalar(expression, resolving=()):
        if len(expression) != 1:
            terms = split_arguments(expression, "+")
            if len(terms) > 1:
                values = [scalar(term, resolving) for term in terms]
                if all(isinstance(value, str) for value in values):
                    return "".join(values)
            raise ValueError(f"{file}: unsupported metadata expression.")
        kind, value = expression[0]
        if kind == "string":
            return value[2:-1].replace('""', '"') if value.startswith('@"') else json.loads(value)
        if kind == "number":
            return int(value.replace("_", ""), 16 if value.lower().startswith("0x") else 10)
        if kind == "identifier" and value in constants and value not in resolving:
            return scalar(constants[value], resolving + (value,))
        raise ValueError(f"{file}: metadata must use literal values or string/integer constants.")

    def string_parameter(key):
        value = scalar(arguments[key])
        if not isinstance(value, str) or not value.strip():
            raise ValueError(f"{file}: {key} must be a nonempty string.")
        return value

    territory_tokens = arguments["territorys"]
    if territory_tokens[:1] == [("punctuation", "[")]:
        territory_tokens = territory_tokens[1:-1]
    else:
        start = territory_tokens.index(("punctuation", "{"))
        territory_tokens = territory_tokens[start + 1:-1]
    territories = [scalar(part) for part in split_arguments(territory_tokens, ",")]
    if any(not isinstance(value, int) for value in territories):
        raise ValueError(f"{file}: territory IDs must be integers.")
    return {
        "Name": string_parameter("name"),
        "Guid": string_parameter("guid"),
        "Version": string_parameter("version"),
        "Author": string_parameter("author"),
        "TerritoryIds": territories,
        "DownloadUrl": "",
        "UpdateInfo": "",
    }


def find_root(start):
    directory = Path(start).resolve()
    for candidate in [directory, *directory.parents]:
        if (candidate / "utils" / "config.json").is_file():
            return candidate
    raise FileNotFoundError("utils/config.json was not found. Pass the repository path.")


def script_files(root):
    for directory, folders, files in os.walk(root, followlinks=False):
        folders[:] = sorted(folder for folder in folders
                            if folder.lower() not in SKIP_DIRECTORIES and not Path(directory, folder).is_symlink())
        for name in sorted(files):
            if name.lower().endswith(".cs"):
                yield Path(directory, name)


def version_numbers(version):
    if not re.fullmatch(r"\d+(?:\.\d+){1,3}", version):
        raise ValueError(f"Unsupported version: {version}. Use 2–4 numeric components.")
    return tuple(int(part) for part in version.split("."))


def next_version(version):
    numbers = list(version_numbers(version))
    numbers[-1] += 1
    if numbers[-1] > 2147483647:
        raise ValueError(f"Version component is too large: {version}.")
    return ".".join(map(str, numbers))


def replace_version(source, file, version):
    tokens = [token for token in TOKEN.finditer(source)
              if token.lastgroup not in {"space", "comment"}]
    for index, token in enumerate(tokens[:-1]):
        if token.lastgroup != "identifier" or token.group() not in {"ScriptType", "ScriptTypeAttribute"}:
            continue
        if tokens[index + 1].group() != "(":
            continue
        depth = 0
        for position in range(index + 2, len(tokens) - 2):
            current = tokens[position]
            if depth == 0 and current.group() == ")":
                break
            if depth == 0 and current.group() == "version" and tokens[position + 1].group() == ":":
                value = tokens[position + 2]
                if value.lastgroup == "identifier":
                    # A literal string constant can also hold the version.
                    name = value.group()
                    value = next((tokens[i + 4] for i in range(len(tokens) - 5)
                                  if [item.group() for item in tokens[i:i + 4]] == ["const", "string", name, "="]
                                  and tokens[i + 5].group() == ";"), None)
                elif tokens[position + 3].group() not in {",", ")"}:
                    value = None
                if value is None or value.lastgroup != "string":
                    raise ValueError(f"{file}: automatic version updates require a string literal or a literal string constant.")
                return source[:value.start()] + json.dumps(version) + source[value.end():]
            if current.lastgroup == "punctuation":
                if current.group() in "([{":
                    depth += 1
                elif current.group() in ")]}":
                    depth -= 1
    raise ValueError(f"{file}: version parameter was not found.")


def write_updates(updates):
    """Stage every file before replacing any; restore originals on write failure."""
    originals, staged, replaced = {}, {}, []
    try:
        for file, content in updates.items():
            originals[file] = file.read_bytes() if file.exists() else None
            with tempfile.NamedTemporaryFile(dir=file.parent, prefix=".update-", suffix=".tmp", delete=False) as temporary:
                staged[file] = Path(temporary.name)
                temporary.write(content)
        for file, temporary in staged.items():
            temporary.replace(file)
            replaced.append(file)
    except OSError:
        for file in reversed(replaced):
            if originals[file] is None:
                file.unlink()
            else:
                file.write_bytes(originals[file])
        raise
    finally:
        for temporary in staged.values():
            temporary.unlink(missing_ok=True)


def generate(root, interactive=False):
    config = json.loads((root / "utils" / "config.json").read_text(encoding="utf-8-sig"))
    repository, branch = config["Repository"], config["Branch"]
    if not re.fullmatch(r"[\w.-]+/[\w.-]+", repository) or not branch.strip():
        raise ValueError("Set Repository to owner/name and specify Branch.")
    state_file = root / "utils" / "update-state.json"
    previous = json.loads(state_file.read_text(encoding="utf-8"))["Scripts"] if state_file.exists() else {}
    scripts, guids, proposals = [], set(), []
    for file in script_files(root):
        relative = file.relative_to(root).as_posix()
        raw = file.read_bytes()
        source = raw.decode("utf-8-sig")
        if not is_public(source, relative):
            print(f"Excluded: {relative}")
            continue
        info = parse_script(source, relative)
        if info is None:
            continue
        guid = str(UUID(info["Guid"]))
        if guid in guids:
            raise ValueError(f"{relative}: duplicate script GUID {guid}.")
        guids.add(guid)
        info["DownloadUrl"] = f"https://raw.githubusercontent.com/{repository}/{quote(branch, safe='')}/{quote(relative, safe='/')}"
        fingerprint = hashlib.sha256(source.replace("\r\n", "\n").encode("utf-8")).hexdigest()
        script = {"file": file, "path": relative, "source": source, "raw": raw, "info": info, "guid": guid, "hash": fingerprint}
        scripts.append(script)
        old = previous.get(guid)
        if interactive and old and (old["Hash"] != fingerprint or old["Path"] != relative):
            current_numbers = version_numbers(info["Version"])
            old_numbers = version_numbers(old["Version"])
            current_order = current_numbers + (0,) * (4 - len(current_numbers))
            old_order = old_numbers + (0,) * (4 - len(old_numbers))
            if current_order < old_order:
                raise ValueError(f"{relative}: version {info['Version']} is lower than the last recorded version {old['Version']}.")
            if current_order > old_order:
                print(f"已手动更新版本：{relative}（{old['Version']} -> {info['Version']}）")
            else:
                updated = next_version(info["Version"])
                # Check that every proposed version can be written before prompting.
                script["updated_source"] = replace_version(source, relative, updated)
                script["updated_version"] = updated
                proposals.append(script)
        print(f"Included: {relative}")

    if not state_file.exists():
        print("首次运行：建立当前内容记录，本次不自动递增版本。")
    elif interactive and not proposals:
        print("没有需要自动递增版本的脚本。")

    bump = False
    if proposals:
        print("\n以下公开脚本有改动：")
        for script in proposals:
            print(f"  {script['path']}\n    {script['info']['Version']} -> {script['updated_version']}")
        while True:
            answer = input("按回车递增上述版本并更新索引；输入 0 只更新索引：").strip()
            if answer in {"", "0"}:
                bump = answer == ""
                break
            print("请输入 0 或直接按回车。")

    updates, infos, records = {}, [], {}
    for script in scripts:
        info = script["info"]
        if bump and "updated_source" in script:
            source = script["updated_source"]
            # Reparse the rewritten source so source and index cannot diverge.
            revised = parse_script(source, script["path"])
            if revised["Version"] != script["updated_version"]:
                raise ValueError(f"{script['path']}: failed to rewrite version.")
            info["Version"] = revised["Version"]
            bom = b"\xef\xbb\xbf" if script["raw"].startswith(b"\xef\xbb\xbf") else b""
            updates[script["file"]] = bom + source.encode("utf-8")
            script["hash"] = hashlib.sha256(source.replace("\r\n", "\n").encode("utf-8")).hexdigest()
        infos.append(info)
        records[script["guid"]] = {"Hash": script["hash"], "Version": info["Version"], "Path": script["path"]}
    updates[root / "OnlineRepo.json"] = (json.dumps(infos, ensure_ascii=False, indent=2) + "\n").encode("utf-8")
    updates[state_file] = (json.dumps({"Scripts": records}, ensure_ascii=False, indent=2) + "\n").encode("utf-8")
    write_updates(updates)
    print(f"Generated OnlineRepo.json with {len(infos)} entries.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", nargs="?", default=Path(__file__).resolve().parent.parent)
    parser.add_argument("--interactive", action="store_true", help="Offer version updates for changed public scripts.")
    args = parser.parse_args()
    try:
        generate(find_root(args.root), interactive=args.interactive)
        return 0
    except (OSError, ValueError, KeyError, EOFError) as error:
        print(f"OnlineRepo generation failed: {error}", file=sys.stderr)
        return 1
    except KeyboardInterrupt:
        print("\n已取消更新。", file=sys.stderr)
        return 130


if __name__ == "__main__":
    raise SystemExit(main())
