import re
import sys
import glob

def format_file(filepath):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    # Rule: exactly one empty line before #endregion
    content = re.sub(r'\n+\s*#endregion', r'\n\n#endregion', content)
    # Rule: exactly one empty line after #region
    content = re.sub(r'#region([^\n]*)\n+', r'#region\1\n\n', content)

    MAX_LEN = 150

    def replacer_1(match):
        squashed = f"{match.group(1)} {{ {match.group(2).strip()} }}"
        return squashed if len(squashed.strip()) <= MAX_LEN else match.group(0)

    def replacer_2(match):
        squashed = f"}} else {{ {match.group(1).strip()} }}"
        return squashed if len(squashed.strip()) <= MAX_LEN else match.group(0)

    # Rule: compress single line block if, else, foreach, etc.
    content = re.sub(r'(if\s*\([^)]+\))\s*\{\s*([^{};\n]+\;)\s*\}', replacer_1, content)
    content = re.sub(r'\}\s*else\s*\{\s*([^{};\n]+\;)\s*\}', replacer_2, content)
    content = re.sub(r'(foreach\s*\([^)]+\))\s*\{\s*([^{};\n]+\;)\s*\}', replacer_1, content)
    content = re.sub(r'(for\s*\([^)]+\))\s*\{\s*([^{};\n]+\;)\s*\}', replacer_1, content)

    # Compress method signatures that have only 1 line of body
    content = re.sub(r'([A-Za-z0-9_<>\[\] \t]+\([^)]*\))\s*\{\s*([^{};\n]+;)\s*\}', replacer_1, content)

    # Group functions by removing large gaps (more than 2 newlines)
    content = re.sub(r'\n{3,}', r'\n\n', content)

    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

if __name__ == "__main__":
    if len(sys.argv) > 1:
        files = sys.argv[1:]
    else:
        files = glob.glob("**/*.cs", recursive=True)
    
    for f in files:
        if "obj" not in f and "bin" not in f:
            format_file(f)
            print(f"Formatted: {f}")
