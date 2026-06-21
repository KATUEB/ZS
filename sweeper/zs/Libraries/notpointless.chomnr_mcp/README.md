<p align="center"><img src="Brain10.webp" width="140" alt="s&box MCP logo"></p>

# s&box MCP

**An MCP server that runs inside the s&box editor.** Connect Claude Code, Claude Desktop, Cursor or VS Code and let AI build games in the editor — scenes, GameObjects, components, prefabs, assets, materials, sounds, input actions, cloud content, ModelDoc, AnimGraph, ShaderGraph, ActionGraph, code files (C#/Razor/SCSS), console diagnostics, play mode and screenshots. 70+ tools, an in-editor dashboard, and a setup that is exactly two steps.

## Setup

1. **Install the library** — in the s&box editor open the Library Manager and install *s&box MCP* (or clone this repo into your project's `Libraries/` folder).
2. **Copy the config** — open the dashboard via the **MCP menu** in the menu bar, find your AI client's card on the Overview tab and click the snippet to copy it. Paste it into your client's MCP config. Done.

The server starts automatically with the editor and listens on `http://127.0.0.1:9090/sbox-mcp` (port configurable in Settings).

### Client configs at a glance

**Claude Code**
```bash
claude mcp add --transport http sbox http://127.0.0.1:9090/sbox-mcp
```

**Claude Desktop** (`claude_desktop_config.json`, needs Node.js)
```json
{ "mcpServers": { "sbox": { "command": "npx", "args": ["-y", "mcp-remote", "http://127.0.0.1:9090/sbox-mcp"] } } }
```

**Cursor** (`.cursor/mcp.json`)
```json
{ "mcpServers": { "sbox": { "url": "http://127.0.0.1:9090/sbox-mcp" } } }
```

**VS Code** (`.vscode/mcp.json`)
```json
{ "servers": { "sbox": { "type": "http", "url": "http://127.0.0.1:9090/sbox-mcp" } } }
```

## The dashboard

- **Overview** — server status with a live pulse, connected clients, one-click copy config cards.
- **Activity** — every tool call the AI makes, live: category chip, arguments, duration, result. Approval cards appear here when a write needs your OK.
- **Tools** — searchable browser of every tool, filterable by category, with a **per-tool enable/disable toggle** (persisted; some tools like cloud downloads ship disabled). **Import Tools** opens a searchable dialog that exposes public static methods from your other installed libraries as MCP tools.
- **Settings** — port, autostart, and the permission mode dropdown.

## Permission modes

| Mode | Behavior |
|---|---|
| **Full access** *(default)* | Every tool runs without asking. |
| **Approve writes** | Tools that modify your project pop an Approve/Deny card in the dock (60s timeout = deny). Read-only tools always run. |
| **Read-only** | Write tools are rejected with a message the AI understands. |

Every scene mutation runs inside an editor undo scope — anything the AI does, you can Ctrl+Z.

## Tool families

| Prefix | What it covers |
|---|---|
| `scene_` | Create, open, list, save / save-as, status, hierarchy, undo/redo |
| `gameobject_` | Create, delete, rename, transform, reparent, duplicate, find, details, select |
| `component_` | Type search, add/remove, get/set properties (any `[Property]`, resources by path) |
| `prefab_` | Create from GameObject, instantiate, break instance, re-sync |
| `asset_` | Search, info, compile, create resource, raw read/write of any text asset |
| `material_` / `soundevent_` | Create .vmat materials and .sound events |
| `cloud_` | Search and install sbox.game content (disabled by default — opt in from the tool browser) |
| `modeldoc_` | Create .vmdl from FBX/OBJ, read as JSON, write KV3, auto-generate collision |
| `animgraph_` | Read as JSON, write KV3, list parameters |
| `shadergraph_` / `actiongraph_` | Read/write the JSON formats, list nodes |
| `input_` / `project_` | List/add/remove input actions, set the startup scene |
| `code_` | List/read/write C#, Razor, SCSS and shader files (hot-reload is automatic), compile errors, invoke a static method to test |
| `editor_` | Console logs, screenshots, play/stop, console commands, project info, selection |
| `retargeter_` | Drives the Humanoid Retargeter library when installed (shown disabled otherwise) |
| `lib_` | Tools you imported from other libraries via Import Tools |

Notes on graph authoring: `.vmdl` and `.vanmgrph` are KV3 text, ShaderGraph/ActionGraph are JSON. The write tools accept the full on-disk format and compile immediately, so format mistakes surface as compile errors the AI can read and fix. Reading an existing asset first (`*_get` with `raw=true`) is the recommended way for the AI to learn the current schema.

## Security

- The server binds to `127.0.0.1` only and rejects non-localhost browser origins.
- File tools are jailed to the project root.
- No auth — anything on your machine that can reach localhost can connect. That is the same trust model as most local MCP servers; use Read-only or Approve-writes mode if it concerns you.

## Troubleshooting

- **Port already in use** — the Overview tab shows the error with a one-click "try another port"; snippets update automatically.
- **Claude Desktop won't connect** — it needs Node.js installed (`npx` runs the `mcp-remote` shim).
- **Client connects but tools error with "No scene is open"** — open a scene in the editor first.
- **`editor_screenshot` fails** — the scene needs an enabled `CameraComponent` (the AI can add one).
- **Changed code in `Editor/` of this library** — the editor hot-reloads it; the server stops its old listener and restarts automatically.

## First-run smoke checklist

1. Open a project → console shows `s&box MCP loaded (... tools)` and `MCP server listening on http://127.0.0.1:9090/sbox-mcp`.
2. View → MCP → status pill pulses green.
3. `claude mcp add --transport http sbox http://127.0.0.1:9090/sbox-mcp`, then in Claude Code: *"what's in my scene?"* → watch the Activity tab.
4. Ask it to create a GameObject → approval card appears (default mode) → Approve → Ctrl+Z undoes it.

## Development

The s&box editor compiles `Editor/` directly — just edit and let it hot-reload. The `Editor/Server` and `Editor/Registry` layers are deliberately Sandbox-free so they can be unit-tested outside the editor.

## License

MIT — see [LICENSE](LICENSE).
