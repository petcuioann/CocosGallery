import re
import sys
import json
import glob
import os

DOCS_FILE = "docs.json"

def extract_and_hide_docs(target_file=None):
    docs_db = {}
    if os.path.exists(DOCS_FILE):
        with open(DOCS_FILE, 'r', encoding='utf-8') as f:
            docs_db = json.load(f)

    if target_file and target_file != "all":
        cs_files = [target_file] if os.path.exists(target_file) else []
        if not cs_files:
            print(f"Error: File '{target_file}' not found.")
            return
    else:
        cs_files = [f for f in glob.glob("**/*.cs", recursive=True) if "obj" not in f and "bin" not in f]
    
    for filepath in cs_files:
        with open(filepath, 'r', encoding='utf-8') as f:
            content = f.read()

        # Regex to match /// lines and the immediate following signature
        doc_pattern = re.compile(r'([ \t]*///.*\n)+([ \t]*[a-zA-Z0-9_\[\]<> \t]+(?:class|struct|interface|enum|void|int|string|bool|Task|public|private|protected|internal|event)[^\n\{]+)')
        
        matches = doc_pattern.finditer(content)
        for match in matches:
            doc_block = match.group(0).replace(match.group(2), "")
            signature = match.group(2).strip()
            # Save mapping
            docs_db[signature] = doc_block.strip()

        # Remove the doc blocks from the text
        new_content = re.sub(r'([ \t]*///.*\n)+([ \t]*[a-zA-Z0-9_\[\]<> \t]+(?:class|struct|interface|enum|void|int|string|bool|Task|public|private|protected|internal|event)[^\n\{]+)', r'\2', content)
        
        if content != new_content:
            with open(filepath, 'w', encoding='utf-8') as f:
                f.write(new_content)
            print(f"Hidden docs in: {filepath}")

    with open(DOCS_FILE, 'w', encoding='utf-8') as f:
        json.dump(docs_db, f, indent=4)
    if target_file == "all":
        print("Documentation extracted and saved to docs.json.")

def show_docs(target_file=None):
    if not os.path.exists(DOCS_FILE):
        print("Error: docs.json not found!")
        return

    with open(DOCS_FILE, 'r', encoding='utf-8') as f:
        docs_db = json.load(f)

    if target_file and target_file != "all":
        cs_files = [target_file] if os.path.exists(target_file) else []
        if not cs_files:
            print(f"Error: File '{target_file}' not found.")
            return
    else:
        cs_files = [f for f in glob.glob("**/*.cs", recursive=True) if "obj" not in f and "bin" not in f]
    
    for filepath in cs_files:
        with open(filepath, 'r', encoding='utf-8') as f:
            content = f.read()

        new_content = content
        for sig, doc_block in docs_db.items():
            escaped_sig = re.escape(sig)
            
            def replacer(match):
                indent = match.group(1)
                signature = match.group(2)
                
                # Format the doc block with the correct indent
                lines = doc_block.split('\n')
                formatted_doc = '\n'.join([indent + line.lstrip() for line in lines])
                return f"\n{formatted_doc}\n{indent}{signature}"
            
            # Match the signature if it does not already have a /// line directly above it
            pattern = re.compile(r'(?<!///)(?<!/// \</summary\>)(?<!/// \</remarks\>)\n([ \t]*)(' + escaped_sig + r')', re.MULTILINE)
            new_content = pattern.sub(replacer, new_content)

        if content != new_content:
            with open(filepath, 'w', encoding='utf-8') as f:
                f.write(new_content)
            print(f"Restored docs in: {filepath}")

if __name__ == "__main__":
    if len(sys.argv) >= 3:
        target = sys.argv[1]
        action = sys.argv[2]
        
        if action == "0":
            extract_and_hide_docs(target)
        elif action == "1":
            # First hide to prevent duplicating
            extract_and_hide_docs(target)
            show_docs(target)
        else:
            print("Invalid argument. Use 0 (hide) or 1 (show).")
    else:
        print("Usage: py DocManager.py [filename|all] [0|1]")
        print("Examples:")
        print("  py DocManager.py all 0                   (hides docs in all files)")
        print("  py DocManager.py MainWindow.xaml.cs 1    (shows docs in MainWindow.xaml.cs)")
