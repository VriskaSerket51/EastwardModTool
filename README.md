# EastwardModTool

You can inspect Eastward assets, edit their source, and build mod packages with EastwardLib.

## How to build

You need .NET 10, CMake 3.20+ and a C++20 compiler (MSVC on Windows, GCC/Clang on Linux, or Xcode command-line tools on macOS). Clone this repository with its EastwardLib submodule:

```sh
git clone --recurse-submodules https://github.com/VriskaSerket51/EastwardModTool.git
cd EastwardModTool
dotnet build EastwardModTool -c Release
dotnet run --project EastwardModTool
```

For an existing checkout, run `git submodule update --init --recursive` before building.

## How to use

Open an Eastward installation or its `content/game` folder. Archives load when an asset needs them; available `content_dlc` archives are registered as overlays.

The asset browser defaults to **Gii directories**, using indexed project paths and parent relationships. Switch to **Asset types** to group assets by type. Virtual assets stay under their owning asset in both views. Search and type filters are shared; switching views preserves selection, editing drafts and the editor position. If a filter hides the selection, clearing it reveals the same asset again.

Open assets in document tabs. Tabs show unsaved edits; closing a tab keeps its draft in the project. Reopen closed tabs with **File → Reopen closed tab** or Ctrl/Cmd+Shift+T. Switching tabs preserves the cursor, selection, scroll position and sprite frame. Ctrl/Cmd+W closes the current tab.

Use **View → Back/Forward**, or Alt+Left/Right, to revisit documents. **View → Pin / unpin asset** adds the current document to the pinned-assets menu. Tabs, navigation history and pins belong to the current workspace session.

Text, JSON, SQ, CSV, GraphML and workbook data can be edited directly. Source editors highlight Lua, GLSL, SQ, JSON, XML/GraphML, bitmap-font definitions, OBJ and CSV with dark-theme colours. Shader scripts highlight the embedded GLSL blocks within their Lua source. SQ labels, contexts, tags and directives have distinct colours; multiline comments, strings and escapes are highlighted without changing the source. Images, sprites and source packages can be exported and imported. Sprites have animation and frame previews. All frames use a shared canvas and origin, preserving runtime part offsets when frame sizes change. Use **Play/Pause** to preview original frame timings, and **Loop** for repeated playback. Scrubbing a frame or leaving the editor pauses playback. Undo, redo and revert keep the original game data.

Deck items show a geometry preview and their original JSON definition. Mesh decks support composite/part selection, mesh tilesets expose tiles, and quad decks expose indices. Color and normal-map channels use the parent pack textures. Opening a PSD parent shows a searchable list of its decks and a preview of the selected item. Parent source import/export remains available. Mesh previews project XY without scene camera, depth or lighting. Sprite-sequence decks use the parent animation.

The workspace adapts to window size. Wide windows show all three panels; medium windows put the editor and metadata in tabs; narrow windows put assets in a tab as well. The top menu groups workspace commands under **File**, editing under **Edit**, source import/export and metadata under **Asset**, mod building under **Mod**, and navigation under **View**. Selecting an asset opens its editor.

Use Ctrl/Cmd+L to focus asset search, Ctrl/Cmd+F to find text, and Ctrl/Cmd+S to save your project. Ctrl+H opens text replacement; on macOS use Cmd+Alt+F. Ctrl/Cmd+O opens a game; adding Shift opens a project. Text search supports case matching, whole words and regular expressions.

Use **Export mod** for a portable `.ewmod` package, or **Build archives** for new copies of affected archives. **Apply mod** checks original resource hashes before writing another output directory. The game is not started or overwritten.

**Save project** keeps editing drafts in an `.ewproject` file, including unfinished text that cannot compile yet. **Open project** checks the game metadata and resource hashes before restoring those drafts.

**Asset → Global metadata** opens asset-index, script-library and texture-index drafts. They retain unknown fields and are validated in an independent editing session when building a mod.

The LuaJIT decompiler is included in the application. Lua bytecode opens as source without a separate download, Node.js or `--decompiler` configuration. The native library loads only when bytecode source is requested. Plain Lua files do not load it.

The build compiles the vendored C++ core and copies the native library and MIT notices into the application output. Build and publish on the target OS. If CMake is not on PATH, pass `-p:CMakeExecutable=/absolute/path/to/cmake`. `--decompiler /path/to/library` remains available to override the bundled library. Lua edits are saved as Lua source; LuaJIT bytecode compilation is not included.

Virtual entries belong to their parent source packages. Original project settings and discarded editing data cannot be recovered. Please check EastwardLib for source compilation compatibility.

## Asset views

| Asset | Default view |
| --- | --- |
| Lua, component scripts, SQ, behaviour scripts, GLSL and shader sources | Code editor with search and source history. |
| JSON, animator data, materials, physics and serialized settings | Property tree; typed value edits update the original source document. |
| CSV, workbooks, data sheets and asset maps | Table; workbook sheets can be selected. Source remains editable. |
| FSM, quest and portal graphs | Nodes and directed topology; original GraphML is available in Source. |
| Scenes, prefabs and prototypes | Group/entity/component hierarchy and selected-object data. |
| PSD deck packs and `.deck2d` | Item list and the selected deck preview. |
| Stretch patches and polygon decks | Texture split guides or polygon outline, with the original definition. |
| Named tilesets and texture collections | Theme/item selection and pixel preview. |
| Sprites and sprite-sequence decks | Shared-origin frame preview and timed playback. |
| Meshes | XY/XZ/YZ wireframe of transformed source vertices. |
| Localization | Keys and languages side by side. |
| TrueType/OpenType and bitmap fonts | Sample text rendered with the original font or glyph pages and metrics. |
| Other packages, including FMOD projects | File list and typed file preview. |
| Raw data and encoded video | File size, signature and paged hex inspection. |
| Missing or unavailable source | Resource notice; original index metadata remains visible. |

Image previews have Fit and 1×/2×/4×/8× nearest-neighbour zoom. Structured views keep a Source tab; search opens it automatically. Table, graph, hierarchy and mesh views are inspectors, with source editing or import for supported assets. They do not execute game scripts or start the game. Scene rendering, mesh depth/lighting, graph manipulation, FMOD event playback and encoded video playback require additional engine-specific work.

## Structure

- `EastwardModTool.Core`: game workspace, asset documents and edit history.
- `EastwardModTool`: Avalonia desktop views.
- `EastwardLib`: asset parsing, source compilation, archives and mod packages.

You can publish for Windows, Linux and macOS with `dotnet publish EastwardModTool -c Release -r <runtime> --self-contained true`.

## Planned features

These priorities adapt workflows from [UndertaleModTool's UI](https://github.com/UnderminersTeam/UndertaleModTool/blob/master/UndertaleModTool/MainWindow.xaml) and [included scripts](https://github.com/UnderminersTeam/UndertaleModTool/blob/master/SCRIPTS.md) to Eastward. They are plans, not implemented features.

| Priority | Feature | Eastward scope |
| --- | --- | --- |
| 1 | Reference lookup | Metadata dependencies and known asset references; mark uncertain Lua matches. |
| 1 | Search across sources | Filter by type, show matches, cancel expensive reads. |
| 1 | Change review | Changed assets, source diffs, compile errors and package resource summary. |
| 1 | Batch import/export | Explicit selections, previews of replacements and shared-atlas handling. |
| 1 | Recovery and recent projects | Draft autosave, recovery prompt and remembered workspace paths. |
| 2 | Sprite and texture editors | Checkerboard, origin/anchor editing and atlas rectangles; playback and pixel zoom are available. |
| 2 | Scene and graph editors | Scene rendering, entity/layer manipulation and GraphML node editing. |
| 2 | Table and localization editors | Workbook cell and locale-key editing; tables and language comparison are available. |
| 2 | Asset creation and cloning | Coordinate asset IDs, virtual children, scripts and texture indices. |
| 3 | Repeatable mod actions | Batch operations through Core with validation and diagnostics. |

GameMaker bytecode, chunk rewriting and its debugger belong to that engine. Eastward needs its own LuaJIT, archive and asset handling. Audio work starts with FMOD bank metadata and bank replacement; rebuilding audio sources requires a separate FMOD workflow. Game launch and debugger integration are outside the current scope.
