using System;
using System.IO;
using System.Linq;
using System.Reflection;
using SboxMcp.Registry;

namespace SboxMcp.Tools;

/// <summary>
/// Integration with the Humanoid Retargeter library, bound purely via
/// reflection so sbox-mcp works whether or not it is installed. The contact
/// surface is the same one the retargeter's own asset-browser context menu
/// uses: RetargetWindow.Open() and window.AddFiles(paths).
/// </summary>
public static class RetargeterTools
{
	public const string Requirement = "humanoid-retargeter";

	const string WindowTypeName = "HumanoidRetargeter.Editor.RetargetWindow";

	static Type _windowType;
	static DateTime _lastLookup = DateTime.MinValue;

	/// <summary>The retargeter's window type, re-resolved at most every few seconds
	/// so installing the library mid-session enables the tools.</summary>
	static Type WindowType
	{
		get
		{
			// re-resolve periodically: picks up installs mid-session and
			// avoids pointing at a stale assembly after the retargeter hotloads
			if ( DateTime.Now - _lastLookup > TimeSpan.FromSeconds( 5 ) )
			{
				_lastLookup = DateTime.Now;
				_windowType = AppDomain.CurrentDomain.GetAssemblies()
					.Select( a => a.GetType( WindowTypeName ) )
					.LastOrDefault( t => t is not null );
			}

			return _windowType;
		}
	}

	public static bool IsInstalled => WindowType is not null;

	[McpTool( "retargeter_status", "Whether the Humanoid Retargeter library (animation retargeting to s&box rigs) is installed and its tools are usable.", ToolCategory.Retargeter )]
	public static object Status() => new
	{
		installed = IsInstalled,
		note = IsInstalled
			? "Retargeter tools are available."
			: "Install the Humanoid Retargeter library from the s&box Library Manager to enable retargeter tools."
	};

	[McpTool( "retargeter_open", "Opens (or raises) the Humanoid Retargeter window.", ToolCategory.Retargeter, Requires = Requirement )]
	public static object Open()
	{
		OpenWindow();
		return new { opened = true };
	}

	[McpTool( "retargeter_add_files", "Opens the Humanoid Retargeter pre-loaded with animation files (.fbx/.bvh/.glb/.gltf). The user reviews mappings and converts in the window.", ToolCategory.Retargeter, Requires = Requirement )]
	public static object AddFiles(
		[Desc( "Absolute paths (or project-relative) of animation files to load" )] string[] paths )
	{
		if ( paths is null || paths.Length == 0 )
			throw new ArgumentException( "Pass at least one animation file path" );

		var resolved = paths
			.Select( p => Path.IsPathRooted( p ) ? Path.GetFullPath( p ) : AssetTools.ResolveInProject( p ) )
			.ToArray();

		var missing = resolved.Where( p => !File.Exists( p ) ).ToArray();
		if ( missing.Length > 0 )
			throw new FileNotFoundException( $"Not found: {string.Join( ", ", missing )}" );

		var window = OpenWindow();
		var addFiles = window.GetType().GetMethod( "AddFiles", BindingFlags.Public | BindingFlags.Instance )
			?? throw new InvalidOperationException( "The installed Humanoid Retargeter version has no AddFiles method - update one of the two libraries" );

		addFiles.Invoke( window, new object[] { resolved } );
		return new { added = resolved.Length, note = "Files are loaded in the retargeter window; review mappings and convert there." };
	}

	static object OpenWindow()
	{
		var type = WindowType
			?? throw new InvalidOperationException( "Humanoid Retargeter is not installed" );

		var open = type.GetMethod( "Open", BindingFlags.Public | BindingFlags.Static )
			?? throw new InvalidOperationException( "The installed Humanoid Retargeter version has no Open method - update one of the two libraries" );

		return open.Invoke( null, null );
	}
}
