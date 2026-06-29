import sys
import subprocess
import os
import shutil

def publish_app(platform):
    valid_platforms = ["x64", "x86", "arm64"]
    platform = platform.lower()

    if platform not in valid_platforms:
        print(f"Error: Invalid platform '{platform}'. Please choose from {valid_platforms}.")
        return

    print(f"Starting standalone publish for Windows {platform}...")

    # The RID (Runtime Identifier) format
    rid = f"win-{platform}"

    # Build the dotnet publish command
    # We forcefully pass the single-file flags, even though they are in the .csproj, to be absolutely certain.
    command = [
        "dotnet", "publish",
        "-c", "Release",
        "-r", rid,
        "--self-contained", "true",
        "-p:PublishSingleFile=true",
        "-p:IncludeNativeLibrariesForSelfExtract=true"
    ]

    print("Running command: " + " ".join(command))
    
    try:
        # Run the dotnet build and pipe output to terminal
        subprocess.run(command, check=True)
    except subprocess.CalledProcessError:
        print("\n[!] Publish failed! Check the build errors above.")
        return

    # Standard .NET 8 output path
    publish_dir = os.path.join(os.getcwd(), "bin", "Release", "net8.0-windows10.0.19041.0", rid, "publish")
    output_dir = os.path.join(os.getcwd(), "BuildOutputs", platform)

    if os.path.exists(publish_dir):
        # Clean the old build output if it exists
        if os.path.exists(output_dir):
            shutil.rmtree(output_dir)
            
        # Copy the new publish folder to a clean location
        shutil.copytree(publish_dir, output_dir)
        
        print(f"\n=============================================")
        print(f" Success! Application compiled for {platform}")
        print(f"=============================================")
        print(f"Your ready-to-distribute files are located at:\n{output_dir}")
        print(f"You can launch CocosGallery.exe directly from there.")
    else:
        print("\nPublish finished, but the output directory was not found in the standard location.")
        print(f"Expected: {publish_dir}")

if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("Usage: py Publish.py <platform>")
        print("Available Platforms: x64, x86, arm64")
        print("Example: py Publish.py x64")
    else:
        publish_app(sys.argv[1])
