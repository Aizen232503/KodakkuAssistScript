"""Generate OnlineRepo.json from local C# scripts, without executing them."""

import json
import os
from pathlib import Path
import re
import sys
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


def generate(root):
    config = json.loads((root / "utils" / "config.json").read_text(encoding="utf-8-sig"))
    repository, branch = config["Repository"], config["Branch"]
    if not re.fullmatch(r"[\w.-]+/[\w.-]+", repository) or not branch.strip():
        raise ValueError("Set Repository to owner/name and specify Branch.")
    infos, guids = [], set()
    for file in script_files(root):
        relative = file.relative_to(root).as_posix()
        source = file.read_text(encoding="utf-8-sig")
        if not is_public(source, relative):
            print(f"Excluded: {relative}")
            continue
        info = parse_script(source, relative)
        if info is None:
            continue
        guid = UUID(info["Guid"])
        if guid in guids:
            raise ValueError(f"{relative}: duplicate script GUID {guid}.")
        guids.add(guid)
        info["DownloadUrl"] = f"https://raw.githubusercontent.com/{repository}/{quote(branch, safe='')}/{quote(relative, safe='/')}"
        infos.append(info)
        print(f"Included: {relative}")

    # Replace the index only after all included scripts parsed successfully.
    output = root / "OnlineRepo.json"
    temporary = output.with_suffix(".json.tmp")
    temporary.write_text(json.dumps(infos, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    temporary.replace(output)
    print(f"Generated OnlineRepo.json with {len(infos)} entries.")


def main():
    try:
        generate(find_root(sys.argv[1] if len(sys.argv) > 1 else Path(__file__).resolve().parent.parent))
        return 0
    except (OSError, ValueError, KeyError) as error:
        print(f"OnlineRepo generation failed: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
