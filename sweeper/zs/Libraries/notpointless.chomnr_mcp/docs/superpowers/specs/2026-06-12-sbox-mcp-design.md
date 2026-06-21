# s&box MCP — Design Spec

**Date:** 2026-06-12
**Status:** Approved (decisions confirmed by user; remaining details decided autonomously per user instruction)

## What this is

An s&box **library** (`Type: "library"`, installable from the sbox.game library browser) that runs a full **MCP server inside the s&box editor**. Any MCP client — Claude Code, Claude Desktop, Cursor, VS Code Copilot — connects over local HTTP and gets deep, tool-based control of the editor: scenes, GameObjects, components, assets, ModelDoc, AnimGraph, ShaderGraph, ActionGraph, code files, diagnostics, screenshots, play mode.

Setup story (the whole point): **install the library, paste the config**. The config snippet for every supported client lives in a colorful in-editor dock, one click to copy.

## Confirmed decisions

| Decision | Choice |
|---|---|
| Transport | Streamable HTTP MCP server in-editor (HttpListener), default `http://127.0.0.1:9090/sbox-mcp` |
| Coverage depth | Full authoring everywhere — including ModelDoc/AnimGraph via direct KV3 file authoring where no managed API exists |
| UI scope | Status + copy-paste configs, live activity feed, permission/approval modes, tool browser, connection dashboard |
| Client snippets | Claude Code, Claude Desktop (mcp-remote shim), Cursor, VS Code/Copilot |
| Distribution | s&box library, all code under `Editor/` (editor-only assembly, unsandboxed) |

## Why this architecture works (verified against Facepunch/sbox-public source)

- **Editor assemblies are not sandboxed.** `Project.Compiling.cs` sets `Whitelist = false`, `Unsafe = true` for the editor compiler. `System.Net.HttpListener`, threads, and sockets are all available.
- **Library structure:** `.sbproj` with `"Type": "library"`, code in `Editor/` compiles into an editor assembly, loaded automatically; libraries publish as source via sbox.game.
- **Init hooks:** static init via `[EditorEvent.Hotload]`-style attributes / assembly load; per-frame pump via `[EditorEvent.Frame]`.
- **Editor UI:** Qt-wrapped widgets (`Editor.Widget`), `[Dock("Editor", "MCP", icon)]` for dockable panels, immediate-mode `Paint` API (gradients, rounded rects, Material Icons), `Theme` palette, CSS via `SetStyles`. `EditorUtility.Clipboard.Copy` for copy buttons.
- **Scene APIs:** `SceneEditorSession.Active`, `Scene.CreateObject`, `GameObject.Components.Create(TypeDescription)`, component `Serialize()/Deserialize(JsonObject)`, `ISceneUndoScope`, `SelectionSystem`, `SetPlaying/StopPlaying`, `EditorTypeLibrary` reflection.
- **Assets:** `AssetSystem.FindByPath/RegisterFile/CreateResource/All`, `asset.Compile(bool)`.
- **ModelDoc:** `.vmdl` is KV3 text (`format:modeldoc30`); managed helpers exist (`CModelDoc.Create`, `g_pModelDocUtils.InitFromMesh`, `AddPhysicsHullFromRender`, `SaveToFile`) and `EditorUtility.KeyValues3ToJson` converts KV3↔readable JSON.
- **AnimGraph:** `.vanmgrph` is KV3 (`CAnimationGraph`/`CAnimNodeManager`); no managed authoring API → author via KV3 text + asset recompile. Citizen graph ships as a format reference.
- **ShaderGraph:** `.shdrgrph` is JSON; `Editor.ShaderGraph.ShaderGraph` has public `Serialize()/Deserialize()`.
- **ActionGraph:** JSON via `ActionGraphResource.SerializedGraph`.
- **Diagnostics:** `ConsoleWidget` retains up to 10k `LogEvent`s + Roslyn `Diagnostic` list; we additionally hook the log event stream into our own ring buffer.

## Architecture

```
Editor/
  Server/        McpServer (HttpListener + Streamable HTTP), McpSession,
                 JsonRpc types, MainThreadDispatcher ([EditorEvent.Frame] pump)
  Registry/      McpToolAttribute, ToolRegistry (reflection → JSON Schema),
                 ToolContext (invocation metadata, permission checks)
  Tools/         SceneTools, GameObjectTools, ComponentTools, PrefabTools,
                 AssetTools, ModelDocTools, AnimGraphTools, ShaderGraphTools,
                 ActionGraphTools, CodeTools, EditorTools (logs/screenshots/play)
  Permissions/   PermissionMode (FullAccess | ApproveWrites | ReadOnly),
                 ApprovalQueue (TCS-based, UI answers, 60s timeout → deny)
  Activity/      ActivityLog ring buffer (tool, args digest, result, ms, ok/err),
                 events the UI subscribes to
  UI/            McpDock ([Dock]), header/status pill, tabs:
                 OverviewPage, ActivityPage, ToolsPage, SettingsPage
                 + custom painted widgets (Pill, CategoryChip, CodeSnippet,
                 CopyButton, GradientHeader)
  Settings/      McpSettings (port, autostart, permission mode) persisted via
                 EditorCookie/ProjectCookie
```

### MCP protocol (Server/)

- JSON-RPC 2.0 over Streamable HTTP: `POST /sbox-mcp` handles `initialize`, `notifications/initialized`, `ping`, `tools/list`, `tools/call`. Responses returned as `application/json` (single-response mode — spec-legal and radically simpler). `GET` returns 405 (no server-push streams in v1). `Mcp-Session-Id` issued on initialize, tracked per client for the dashboard.
- Bound to `127.0.0.1` only. No auth in v1 (localhost-only); port configurable in Settings.
- `protocolVersion: "2025-06-18"` with graceful fallback to client's offered version.
- All tool handlers hop to the editor main thread via `MainThreadDispatcher` (ConcurrentQueue drained by `[EditorEvent.Frame]`, results via TaskCompletionSource, 30s default timeout so a hung tool can't wedge the listener).
- Errors: tool exceptions → MCP tool result with `isError: true` and the exception message; protocol errors → JSON-RPC error objects.

### Tool registry (Registry/)

Tools are static methods marked `[McpTool("scene_create_object", "Creates a GameObject…", Category.Scene, Writes = true)]`. The registry reflects parameters (name, type, default, `[Description]`) into JSON Schema for `tools/list`. `Writes = true` marks tools subject to the permission gate. Adding a tool = writing one annotated method; the registry, schema, tool browser UI, and permission system all pick it up automatically.

### Tool surface (~60 tools, names are `category_verb_noun`)

- **scene_**: get_status, get_hierarchy (depth-limited tree w/ ids), save, load, undo, redo
- **gameobject_**: create, delete, rename, set_parent, get/set_transform, duplicate, find (by name/component/tag), get_details (components + serialized properties), set_enabled, select
- **component_**: list_types (searchable via EditorTypeLibrary), add, remove, get_properties, set_property (string path + JSON value, via SerializedObject/Deserialize), enable/disable
- **prefab_**: instantiate, break_instance, update_from_prefab
- **asset_**: search (by type/name), get_info, compile, create_resource, read_raw, write_raw (KV3/JSON text with registration + recompile)
- **modeldoc_**: create_from_mesh (FBX/OBJ → vmdl with hull/mesh/none collision), get (vmdl as JSON via KeyValues3ToJson), set (JSON/KV3 → vmdl text, register, compile), add_physics
- **animgraph_**: get (KV3→JSON), set (KV3 write + recompile), list_parameters
- **shadergraph_**: get, set (via ShaderGraph.Serialize/Deserialize for validation), list_nodes
- **actiongraph_**: get, set
- **code_**: read_file, write_file, list_files, get_compile_errors (Roslyn diagnostics)
- **editor_**: get_logs (tail w/ severity filter), clear_logs, screenshot (scene or game view → PNG base64 image content), play, stop, is_playing, run_console_command, get_project_info, get_selection

v1 ships all categories; graph **set** tools are documented as "format-faithful" (we round-trip the on-disk format and validate by compiling, since Facepunch can evolve KV3 schemas).

### Permission system

Three modes, set in UI, persisted: **Full Access** (default off), **Approve Writes** (default), **Read-Only**. In Approve Writes, a `Writes = true` tool call enqueues an approval card in the dock (tool, args summary, Approve/Deny); 60s timeout = deny. Read-only mode rejects write tools with a clear error the AI can relay.

### UI — the colorful part

A dockable panel (`[Dock("Editor", "MCP", "hub")]`) designed to look like nothing else in the editor:

- **Gradient header** (indigo→violet→pink linear gradient via `Paint.SetBrushLinear`), product mark "s&box MCP", and a **status pill** — pulsing green glow when running, amber while starting, gray when stopped — plus client count.
- **Tab bar** with colored underline per tab: Overview / Activity / Tools / Settings.
- **Overview:** big start/stop control, endpoint URL with copy button, then per-client config cards (Claude Code, Claude Desktop, Cursor, VS Code) — each a dark monospace snippet block with a one-click copy button and the client's accent color edge.
- **Activity:** live feed; each row = colored category chip (Scene=blue, Assets=orange, ModelDoc=violet, AnimGraph=pink, ShaderGraph=teal, ActionGraph=lime, Code=green, Editor=yellow), tool name, arg digest, duration, ok/error icon. Approval cards appear at top in Approve-Writes mode.
- **Tools:** search box + category filter chips; rows show name, description, write-badge. Doubles as docs.
- **Settings:** port, autostart-on-editor-load, permission mode (segmented control), clear-activity.

Category colors define the visual identity and are reused in chips, tool browser accents, and feed rows.

### Settings & lifecycle

Server autostarts on editor load by default (configurable). Port conflicts: try configured port; on failure surface the error in the dock with a one-click "try another port" that updates snippets automatically. Hotload: server stops/restarts cleanly via `IDisposable` + hotload events; HttpListener handle never leaks.

### Testing strategy

No headless s&box runner exists, so correctness is layered:
1. **Compile gate:** a dev-only `sbox-mcp.csproj` (gitignored from the published library, kept in repo) references the real installed assemblies (`D:\SteamLibrary\steamapps\common\sbox\bin\managed\*.dll`); `dotnet build` must pass with zero errors. This validates every API signature we call.
2. **Protocol tests:** the JSON-RPC/Streamable-HTTP layer and tool-schema generation are kept free of Sandbox dependencies where possible so they can be exercised by a tiny xunit project driving `McpServer` types directly. (Best-effort: anything needing editor state is covered by gate 1 + live smoke.)
3. **Live smoke checklist** in README for first run in the actual editor.

### Error handling principles

- Every tool validates inputs and throws descriptive messages ("GameObject 'x7f3…' not found — use gameobject_find") — these surface to the AI as `isError` tool results, never crash the editor.
- All scene mutations run inside `UndoScope` so a human can Ctrl+Z anything the AI did.
- File-writing tools refuse paths outside the project root.

## Out of scope (v1)

- SSE server-push / MCP resources & prompts (tools only)
- Remote (non-localhost) access and auth
- ModelDoc node-tree *incremental* editing UI parity (we do whole-document KV3 get/set + helpers, not per-node ops)
- Map/Hammer editing
