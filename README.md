# Cocos Gallery

A sleek, robust, and fluid media gallery built exclusively for Windows using WinUI 3. It allows seamless browsing, tagging, viewing, and organizing of your local media items through an intuitive user interface.

## Features

* **Cross-Memory Selection (Shared Memory Across Instances):** Open multiple windows of the app to edit and browse in parallel. When "Cross-Memory Selection" is enabled in settings, all running instances sync their selections in real-time, functioning as a single powerful workspace.
* **Intelligent Wipe Cache System:** Keep your PC clean. Enable the Wipe Cache option to permanently delete generated video thumbnails, empty the app's internal recycle bin, and clear temporary system files automatically whenever the application shuts down.
* **Bulk Editing & Tagging:** Manage tags efficiently across your library. Right-click individual items or use Selection Mode to add, remove, or toggle tags across hundreds of items instantly using the floating Edit Tags menu.
* **Dynamic Grid Scaling:** Fluidly resize the entire media grid and thumbnail dimensions. Scale up for large, accessible previews or scale down to view more of your library at a glance.
* **Internal Recycle Bin:** Deleted items are kept safe in an isolated local Recycle Bin instead of being permanently wiped, allowing you to restore items to their original locations and tags with a single click.
* **System Tray Integration:** Runs silently in the background via the system tray, allowing instant window restoration and fast shutdowns.

## Technologies Used

* **C# / .NET 8:** Core application logic.
* **WinUI 3 / Windows App SDK:** Native, modern UI and fluent design system.
* **H.NotifyIcon:** For robust system tray functionalities and taskbar context menus.

## Getting Started

1. Clone the repository.
2. Open the solution in **Visual Studio 2022**.
3. Ensure the **.NET 8.0 SDK** and **Windows App SDK** workloads are installed.
4. Set the project architecture to `x64` (or `x86`/`ARM64` depending on your machine).
5. Build and Run!

## Developer Scripts

The repository includes custom Python scripts located in the inner `CocosGallery` folder designed for build automation, maintaining a highly compact C# coding style ("snug formatting"), and managing XML documentation.

* **`SnugFormatter.py`**: A structural formatting script that traverses `.cs` files and compresses short methods and control blocks (if/foreach/while) into dense, single-line statements.
  * **Usage**: `py SnugFormatter.py` (formats all files)
* **`DocManager.py`**: A documentation manager that dynamically extracts, hides, and restores `/// <summary>` tags across the codebase, allowing you to read raw logic without visual clutter. It safely stores hidden docs in a background `docs.json` database.
  * **Usage**: `py DocManager.py all 0` (hides docs across all files)
  * **Usage**: `py DocManager.py all 1` (restores docs across all files)
  * **Usage**: `py DocManager.py MainWindow.xaml.cs 0` (targets a specific file)
* **`Publish.py`**: An automated build script that compiles the app into a standalone, single-file executable for a chosen platform (x64, x86, arm64). It outputs the final distribution-ready files to the `BuildOutputs` directory.
  * **Usage**: `py Publish.py <platform>` (e.g., `py Publish.py x64`)

## About This Project

This is a personal project, intended to be attached to my CV and managed by myself. 

If you find any bugs or issues while testing the application, please feel free to submit an issue, and I will try to personally fix it!
