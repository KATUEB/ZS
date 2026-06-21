using System;
using Editor;
using Sandbox;
using SboxMcp.Registry;
using static SboxMcp.Tools.ToolHelpers;

namespace SboxMcp.Tools;

public static class PrefabTools
{
	[McpTool( "prefab_instantiate", "Instantiates a prefab into the active scene.", ToolCategory.Prefab, Writes = true )]
	public static object Instantiate(
		[Desc( "Prefab asset path, e.g. 'prefabs/door.prefab'" )] string prefabPath,
		[Desc( "World position [x, y, z]" )] float[] position = null )
	{
		var session = RequireSession();

		var prefabFile = ResourceLibrary.Get<PrefabFile>( prefabPath )
			?? throw new InvalidOperationException( $"No prefab at '{prefabPath}' - use asset_search with assetType 'prefab'" );

		var prefabScene = SceneUtility.GetPrefabScene( prefabFile )
			?? throw new InvalidOperationException( $"Prefab '{prefabPath}' could not be loaded" );

		using var undo = session.UndoScope( $"MCP: instantiate {prefabPath}" ).WithGameObjectCreations().Push();

		var transform = position is null
			? global::Transform.Zero
			: new Transform( ToVector3( position, "position" ) );

		var instance = prefabScene.Clone( transform );
		return Describe( instance );
	}

	[McpTool( "prefab_create_from_gameobject", "Turns a GameObject (and its children) into a reusable .prefab asset; the original becomes an instance of it.", ToolCategory.Prefab, Writes = true )]
	public static object CreateFromGameObject(
		[Desc( "GameObject id or unique name" )] string gameObject,
		[Desc( "Output path ending in .prefab, e.g. 'prefabs/door.prefab'" )] string prefabPath )
	{
		if ( !prefabPath.EndsWith( ".prefab", StringComparison.OrdinalIgnoreCase ) )
			throw new ArgumentException( "prefabPath must end in .prefab" );

		var session = RequireSession();
		var go = FindGameObject( gameObject );
		var absolute = AssetTools.ResolveNewAssetPath( prefabPath );

		if ( System.IO.File.Exists( absolute ) )
			throw new InvalidOperationException( $"'{prefabPath}' already exists" );

		System.IO.Directory.CreateDirectory( System.IO.Path.GetDirectoryName( absolute ) );

		using var undo = session.UndoScope( $"MCP: create prefab {prefabPath}" )
			.WithGameObjectChanges( go, GameObjectUndoFlags.All ).Push();

		EditorUtility.Prefabs.ConvertGameObjectToPrefab( go, absolute );

		return new { created = prefabPath, instanceId = go.Id };
	}

	[McpTool( "prefab_break_instance", "Unlinks a prefab instance so it becomes plain GameObjects.", ToolCategory.Prefab, Writes = true )]
	public static object BreakInstance( [Desc( "GameObject id or unique name of the prefab instance root" )] string gameObject )
	{
		var session = RequireSession();
		var go = FindGameObject( gameObject );

		if ( !go.IsPrefabInstance )
			throw new InvalidOperationException( $"'{go.Name}' is not a prefab instance" );

		using var undo = session.UndoScope( "MCP: break prefab instance" )
			.WithGameObjectChanges( go, GameObjectUndoFlags.All ).Push();

		go.BreakFromPrefab();
		return Describe( go );
	}

	[McpTool( "prefab_update_from_prefab", "Re-syncs a prefab instance from its source prefab file.", ToolCategory.Prefab, Writes = true )]
	public static object UpdateFromPrefab( [Desc( "GameObject id or unique name of the prefab instance root" )] string gameObject )
	{
		var session = RequireSession();
		var go = FindGameObject( gameObject );

		if ( !go.IsPrefabInstance )
			throw new InvalidOperationException( $"'{go.Name}' is not a prefab instance" );

		using var undo = session.UndoScope( "MCP: update from prefab" )
			.WithGameObjectChanges( go, GameObjectUndoFlags.All ).Push();

		go.UpdateFromPrefab();
		return Describe( go );
	}
}
