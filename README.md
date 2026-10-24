# EastwardModTool

English | [한국어](README.ko.md)

Unofficial desktop editor for Eastward assets and mods.

## How to use

1. Open the Eastward installation folder or `content/game`.
2. Open and edit assets.
3. Use `Save project` to save edits as an `.ewproject`.

The default view follows Gii folders. `Asset types` groups assets by type.

`Asset > Export source...` exports editable files. Use `Import file...` or `Import folder...` to import changes. Installed DLC archives are loaded when `content_dlc` is found.

### Mod creation

1. Use `Mod > Export mod...` to save changes as an `.ewmod`.
2. Open `Manage mods...`, install the loader and add the mod.
3. Enable it and select `Apply enabled mods`.

To save `.g` archives instead, use `Build archives...`. Output goes to a separate folder.

### Language patches

Open `Mod > Create language patch...`. Enter the language ID, display name and base language, then select the JSON exported from [Eastward Translate](https://github.com/VriskaSerket51/eastward-kr). Add menus, images or fonts as needed.

Select `Create editing drafts`, check the changes and export the mod. See the [language patch guide](https://github.com/VriskaSerket51/EastwardModLoader/blob/main/docs/language-patches.md).

## How to build

.NET 10, CMake 3.20+ and a C++20 compiler required.

```sh
git clone --recurse-submodules https://github.com/VriskaSerket51/EastwardModTool.git
cd EastwardModTool
dotnet build EastwardModTool -c Release
dotnet run --project EastwardModTool
```

For an existing checkout, run `git submodule update --init --recursive`. Use `-p:CMakeExecutable=/path/to/cmake` if CMake is not on PATH.

Build on the target OS: Windows, Linux or macOS. For example:

```sh
dotnet publish EastwardModTool -c Release -r win-x64 --self-contained true -o ./publish/win-x64
```

Use `linux-x64` or `osx-arm64` for the corresponding target. The native decompiler must also be built for that OS.
