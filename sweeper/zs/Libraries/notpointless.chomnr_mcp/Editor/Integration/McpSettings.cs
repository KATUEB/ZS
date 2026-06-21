using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using static Sandbox.Internal.GlobalToolsNamespace;

namespace SboxMcp.Integration;

/// <summary>A user-imported tool: a public static method from another library.</summary>
public sealed record ImportedToolDef( string Assembly, string Type, string Method );

/// <summary>
/// Persisted settings. EditorCookie is not thread-safe and must only be
/// touched on the editor main thread, so values are cached in fields:
/// getters are safe from any thread, setters are UI (main thread) only.
/// </summary>
public static class McpSettings
{
	public const int DefaultPort = 9090;

	static int _port = DefaultPort;
	static bool _autoStart = true;
	static PermissionMode _mode = PermissionMode.FullAccess;

	/// <summary>Called once from the editor main thread before anything reads settings.</summary>
	internal static void LoadFromCookies()
	{
		_port = EditorCookie.Get( "SboxMcp.Port", DefaultPort );
		_autoStart = EditorCookie.Get( "SboxMcp.AutoStart", true );
		_mode = EditorCookie.Get( "SboxMcp.PermissionMode", PermissionMode.FullAccess );
		LoadExtras();
	}

	public static int Port
	{
		get => _port;
		set { _port = value; EditorCookie.Set( "SboxMcp.Port", value ); }
	}

	public static bool AutoStart
	{
		get => _autoStart;
		set { _autoStart = value; EditorCookie.Set( "SboxMcp.AutoStart", value ); }
	}

	public static PermissionMode Mode
	{
		get => _mode;
		set { _mode = value; EditorCookie.Set( "SboxMcp.PermissionMode", value ); }
	}

	// ---- dashboard window size (persisted) ---------------------------------

	static Vector2 _dockSize = new( 420, 560 );

	public static Vector2 DockSize
	{
		get => _dockSize;
		set
		{
			_dockSize = value;
			EditorCookie.Set( "SboxMcp.DockSize", $"{(int)value.x}x{(int)value.y}" );
		}
	}

	// ---- per-tool enable/disable overrides (persisted) ---------------------

	// reference-swapped on change so worker threads can read without locks;
	// absence of a key means "use the tool's default"
	static Dictionary<string, bool> _toolDisabledOverrides = new();

	/// <summary>The user's explicit choice for a tool, or null = tool default.</summary>
	public static bool? GetToolDisabledOverride( string toolName ) =>
		_toolDisabledOverrides.TryGetValue( toolName, out var disabled ) ? disabled : null;

	/// <summary>UI/main thread only (writes a cookie).</summary>
	public static void SetToolDisabled( string toolName, bool disabled )
	{
		var next = new Dictionary<string, bool>( _toolDisabledOverrides ) { [toolName] = disabled };
		_toolDisabledOverrides = next;
		EditorCookie.Set( "SboxMcp.ToolOverrides",
			string.Join( ";", next.Select( kv => $"{kv.Key}={(kv.Value ? 1 : 0)}" ) ) );
	}

	// ---- imported tools (persisted) ----------------------------------------

	static List<ImportedToolDef> _importedTools = new();

	public static IReadOnlyList<ImportedToolDef> ImportedTools => _importedTools;

	/// <summary>UI/main thread only (writes a cookie).</summary>
	public static void AddImportedTool( ImportedToolDef def )
	{
		if ( _importedTools.Contains( def ) )
			return;

		_importedTools = new List<ImportedToolDef>( _importedTools ) { def };
		SaveImports();
	}

	/// <summary>UI/main thread only (writes a cookie).</summary>
	public static void RemoveImportedTool( ImportedToolDef def )
	{
		_importedTools = _importedTools.Where( d => d != def ).ToList();
		SaveImports();
	}

	static void SaveImports() =>
		EditorCookie.Set( "SboxMcp.ImportedTools", JsonSerializer.Serialize( _importedTools ) );

	static void LoadExtras()
	{
		var size = EditorCookie.Get( "SboxMcp.DockSize", "" );
		var sizeParts = size.Split( 'x' );
		if ( sizeParts.Length == 2 && int.TryParse( sizeParts[0], out var w ) && int.TryParse( sizeParts[1], out var h ) )
			_dockSize = new Vector2( Math.Max( w, 360 ), Math.Max( h, 220 ) );

		var overrides = EditorCookie.Get( "SboxMcp.ToolOverrides", "" );
		_toolDisabledOverrides = overrides
			.Split( ';', StringSplitOptions.RemoveEmptyEntries )
			.Select( pair => pair.Split( '=' ) )
			.Where( parts => parts.Length == 2 )
			.ToDictionary( parts => parts[0], parts => parts[1] == "1" );

		var imports = EditorCookie.Get( "SboxMcp.ImportedTools", "" );
		try
		{
			_importedTools = string.IsNullOrWhiteSpace( imports )
				? new List<ImportedToolDef>()
				: JsonSerializer.Deserialize<List<ImportedToolDef>>( imports ) ?? new List<ImportedToolDef>();
		}
		catch ( JsonException )
		{
			_importedTools = new List<ImportedToolDef>();
		}
	}
}
