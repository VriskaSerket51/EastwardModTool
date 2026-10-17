# EastwardModTool

English | [한국어](README.ko.md)

A tool for viewing and editing Eastward assets.

## How to build

Requires .NET 10, CMake 3.20 or newer and a C++20 compiler.

```sh
git clone --recurse-submodules https://github.com/VriskaSerket51/EastwardModTool.git
cd EastwardModTool
dotnet build EastwardModTool -c Release
dotnet run --project EastwardModTool
```

For an existing checkout, run `git submodule update --init --recursive`. Set the CMake path with `-p:CMakeExecutable=/path/to/cmake` if needed.

## How to use

Open the Eastward installation directory or `content/game`. DLC resources are loaded as well.

The asset list follows the Gii directory structure. Select **Asset types** to group assets by type instead.

Use **Asset > Export source...** to export sources and **Import file... / Import folder...** to import edited sources. **Global metadata** edits the asset, script and texture indexes.

**Save project** saves your edits as an `.ewproject`. Drafts remain when editor tabs are closed; use **Reopen closed tab** to open them again.

### Creating mods

Use **Mod > Export mod...** to save changed resources in an `.ewmod`.

In **Manage mods...**, install the loader, add the `.ewmod` and enable it.

Use **Build archives...** to save `.g` archives. Output goes to a separate directory, preserving the originals.

### Language patches

In **Mod > Create language patch...**, enter the language ID, display name and base language, then choose the file downloaded with **Export JSON** in the translation editor. You can also select menus, images and fonts.

Check the output from **Create editing drafts**, then export it as a mod. See the [language patch guide](https://github.com/VriskaSerket51/EastwardModLoader/blob/main/docs/language-patches.md) for details.

## Asset previews

| Asset | View |
| --- | --- |
| Code, text | Source editor |
| Scenes, proto, serialized objects | Hierarchy, properties, object references |
| Tables, locale packs | Sheets, messages and language comparisons |
| Graphs, meshes | Node connections, wireframes |
| Textures, decks, sprites | Images, geometry, animations |
| Fonts, packages, binary files | Glyphs, file lists, hex view |

## Other operating systems

Builds support Windows, Linux and macOS. Build the native decompiler on the target OS as well.

```sh
dotnet publish EastwardModTool -c Release -r win-x64 --self-contained true -o ./publish/win-x64
```

On Linux and macOS, build in that environment and use a runtime such as `linux-x64` or `osx-arm64`.
