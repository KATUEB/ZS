using System;
using System.IO;
using System.Linq;
using Editor;
using Sandbox;
using SboxMcp.Registry;
using SboxMcp.Server;

namespace SboxMcp.Tools;

public static class AssetTools
{
	internal static string ProjectRoot =>
		Project.Current?.GetRootPath()
		?? throw new InvalidOperationException( "No project is loaded" );

	internal static string ResolveInProject( string path ) => PathJail.Resolve( ProjectRoot, path );

	/// <summary>
	/// Resolves a path for a NEW asset file. Plain asset paths like
	/// 'models/new.vmdl' land in the project's Assets mount (where the asset
	/// system can register them); explicit 'Assets/...'-style or absolute
	/// paths resolve against the project root. Always jailed to the project.
	/// </summary>
	internal static string ResolveNewAssetPath( string path )
	{
		var rootResolved = ResolveInProject( path );

		if ( Path.IsPathRooted( path ) || File.Exists( rootResolved ) )
			return rootResolved;

		var assets = Project.Current?.GetAssetsPath();
		if ( assets is null )
			return rootResolved;

		// already targeting the assets folder explicitly?
		var assetsResolved = PathJail.Resolve( ProjectRoot, Path.Combine( assets, path ) );
		return rootResolved.StartsWith( Path.GetFullPath( assets ), StringComparison.OrdinalIgnoreCase )
			? rootResolved
			: assetsResolved;
	}

	[McpTool( "asset_search", "Searches project assets by name and/or type extension (vmdl, vmat, prefab, scene, vanmgrph, shdrgrph...).", ToolCategory.Asset )]
	public static object Search(
		[Desc( "Name/path substring (case-insensitive); omit for all" )] string query = null,
		[Desc( "File extension filter without dot, e.g. 'vmdl'" )] string assetType = null,
		int max = 50 )
	{
		var results = AssetSystem.All
			.Where( a => query is null || a.Path.Contains( query, StringComparison.OrdinalIgnoreCase ) )
			.Where( a => assetType is null
				|| string.Equals( a.AssetType?.FileExtension, assetType.TrimStart( '.' ), StringComparison.OrdinalIgnoreCase ) )
			.Take( max )
			.Select( a => new
			{
				path = a.Path,
				type = a.AssetType?.FileExtension,
				compiled = a.IsCompiled
			} )
			.ToArray();

		return new { count = results.Length, results };
	}

	[McpTool( "asset_get_info", "Gets details for one asset by path.", ToolCategory.Asset )]
	public static object GetInfo( [Desc( "Asset path, e.g. 'models/crate.vmdl'" )] string path )
	{
		var asset = AssetSystem.FindByPath( path )
			?? throw new InvalidOperationException( $"No asset at '{path}' - use asset_search" );

		return new
		{
			path = asset.Path,
			absolutePath = asset.AbsolutePath,
			type = asset.AssetType?.FriendlyName,
			extension = asset.AssetType?.FileExtension,
			compiled = asset.IsCompiled,
			canRecompile = asset.CanRecompile
		};
	}

	[McpTool( "asset_compile", "Compiles (or recompiles) an asset.", ToolCategory.Asset, Writes = true )]
	public static object Compile( [Desc( "Asset path" )] string path )
	{
		var asset = AssetSystem.FindByPath( path )
			?? throw new InvalidOperationException( $"No asset at '{path}' - use asset_search" );

		asset.Compile( true );
		return new { path = asset.Path, compiled = asset.IsCompiled };
	}

	[McpTool( "asset_create_resource", "Creates a new empty game resource asset (scene, prefab, or any custom GameResource extension).", ToolCategory.Asset, Writes = true )]
	public static object CreateResource(
		[Desc( "Resource type extension without dot, e.g. 'prefab', 'scene'" )] string type,
		[Desc( "Project-relative output path including extension, e.g. 'Assets/prefabs/new.prefab'" )] string path )
	{
		var absolute = ResolveNewAssetPath( path );

		if ( File.Exists( absolute ) )
			throw new InvalidOperationException( $"'{path}' already exists" );

		Directory.CreateDirectory( Path.GetDirectoryName( absolute ) );

		var asset = AssetSystem.CreateResource( type.TrimStart( '.' ), absolute )
			?? throw new InvalidOperationException( $"Could not create a '{type}' resource at '{path}'" );

		return new { created = asset.Path, type = asset.AssetType?.FriendlyName };
	}

	[McpTool( "asset_read_raw", "Reads an asset's source file as text (KV3/JSON formats are text).", ToolCategory.Asset )]
	public static object ReadRaw( [Desc( "Asset path or project-relative file path" )] string path )
	{
		var absolute = AssetSystem.FindByPath( path )?.GetSourceFile( true ) ?? ResolveInProject( path );

		if ( !File.Exists( absolute ) )
			throw new InvalidOperationException( $"No file at '{path}'" );

		return new { path, content = File.ReadAllText( absolute ) };
	}

	[McpTool( "asset_write_raw", "Writes text to an asset source file, registers it with the asset system and compiles it. Use for any text-based asset format.", ToolCategory.Asset, Writes = true )]
	public static object WriteRaw(
		[Desc( "Asset path or project-relative file path" )] string path,
		[Desc( "Full new file content" )] string content )
	{
		var existing = AssetSystem.FindByPath( path );
		var absolute = existing?.GetSourceFile( true ) ?? ResolveNewAssetPath( path );

		Directory.CreateDirectory( Path.GetDirectoryName( absolute ) );
		File.WriteAllText( absolute, content );

		var asset = existing ?? AssetSystem.RegisterFile( absolute );
		asset?.Compile( true );

		return new
		{
			written = path,
			registered = asset is not null,
			compiled = asset?.IsCompiled ?? false
		};
	}
}
