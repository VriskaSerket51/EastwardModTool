# EastwardModTool

You can inspect Eastward assets, edit their source, and build mod packages with EastwardLib.

## How to build

You need .NET 10. Clone this repository with its EastwardLib submodule:

```sh
git clone --recurse-submodules https://github.com/VriskaSerket51/EastwardModTool.git
cd EastwardModTool
dotnet build EastwardModTool -c Release
dotnet run --project EastwardModTool
```

For an existing checkout, run `git submodule update --init --recursive` before building.

## How to use

Open an Eastward installation or its `content/game` folder. Archives load when an asset needs them; available `content_dlc` archives are registered as overlays.

You can search by path and filter by asset type. Text, JSON, SQ, CSV, GraphML and workbook data can be edited directly. Images, sprites and source packages can be exported and imported. Sprites have animation and frame previews. Undo, redo and revert keep the original game data.

Use **Export mod** for a portable `.ewmod` package, or **Build archives** for new copies of affected archives. **Apply mod** checks original resource hashes before writing another output directory. The game is not started or overwritten.

**Save project** keeps editing drafts in an `.ewproject` file, including unfinished text that cannot compile yet. **Open project** checks the game metadata and resource hashes before restoring those drafts.

The inspector's **Index**, **Scripts** and **Textures** buttons open global metadata drafts. They retain unknown fields and are validated in an independent editing session when building a mod.

Some Lua assets need a native LuaJIT decompiler to expose their source. You can pass its path when opening a game:

```sh
dotnet run --project EastwardModTool -- --game /path/to/Eastward --decompiler /path/to/eastward-luajit.dll
```

Virtual entries belong to their parent source packages. Original project settings and discarded editing data cannot be recovered. Please check EastwardLib for source compilation compatibility.

## Structure

- `EastwardModTool.Core`: game workspace, asset documents and edit history.
- `EastwardModTool`: Avalonia desktop views.
- `EastwardLib`: asset parsing, source compilation, archives and mod packages.

You can publish for Windows, Linux and macOS with `dotnet publish EastwardModTool -c Release -r <runtime> --self-contained true`.
