using System;
using System.Linq;
using Editor;
using Sandbox;
using SboxMcp.Registry;
using static SboxMcp.Tools.ToolHelpers;

namespace SboxMcp.Tools;

public static class GameObjectTools
{
	[McpTool( "gameobject_create", "Creates a new GameObject in the active scene.", ToolCategory.GameObject, Writes = true )]
	public static object Create(
		string name,
		[Desc( "Id of the parent GameObject; omit for scene root" )] string parentId = null,
		[Desc( "World position [x, y, z]" )] float[] position = null,
		[Desc( "Rotation [pitch, yaw, roll] in degrees" )] float[] rotation = null,
		[Desc( "Scale [x, y, z]" )] float[] scale = null )
	{
		var session = RequireSession();
		var parent = parentId is null ? null : FindGameObject( parentId );

		using var undo = session.UndoScope( $"MCP: create {name}" ).WithGameObjectCreations().Push();

		var go = session.Scene.CreateObject();
		go.Name = string.IsNullOrWhiteSpace( name ) ? "GameObject" : name;

		if ( parent is not null )
			go.Parent = parent;

		if ( position is not null )
			go.WorldPosition = ToVector3( position, "position" );

		if ( rotation is not null )
		{
			if ( rotation.Length != 3 )
				throw new ArgumentException( "'rotation' must be [pitch, yaw, roll]" );

			go.WorldRotation = Rotation.From( rotation[0], rotation[1], rotation[2] );
		}

		if ( scale is not null )
			go.LocalScale = ToVector3( scale, "scale" );

		return Describe( go );
	}

	[McpTool( "gameobject_delete", "Deletes a GameObject (and its children).", ToolCategory.GameObject, Writes = true )]
	public static object Delete( [Desc( "GameObject id or unique name" )] string gameObject )
	{
		var session = RequireSession();
		var go = FindGameObject( gameObject );
		var name = go.Name;

		using var undo = session.UndoScope( $"MCP: delete {name}" )
			.WithGameObjectDestructions( new[] { go } ).Push();

		go.Destroy();
		return new { deleted = name };
	}

	[McpTool( "gameobject_rename", "Renames a GameObject.", ToolCategory.GameObject, Writes = true )]
	public static object Rename( [Desc( "GameObject id or unique name" )] string gameObject, string newName )
	{
		var session = RequireSession();
		var go = FindGameObject( gameObject );

		using var undo = session.UndoScope( $"MCP: rename to {newName}" )
			.WithGameObjectChanges( go, GameObjectUndoFlags.Properties ).Push();

		go.Name = newName;
		return Describe( go );
	}

	[McpTool( "gameobject_set_enabled", "Enables or disables a GameObject.", ToolCategory.GameObject, Writes = true )]
	public static object SetEnabled( [Desc( "GameObject id or unique name" )] string gameObject, bool enabled )
	{
		var session = RequireSession();
		var go = FindGameObject( gameObject );

		using var undo = session.UndoScope( $"MCP: set enabled {enabled}" )
			.WithGameObjectChanges( go, GameObjectUndoFlags.Properties ).Push();

		go.Enabled = enabled;
		return Describe( go );
	}

	[McpTool( "gameobject_set_parent", "Reparents a GameObject (keeps world position).", ToolCategory.GameObject, Writes = true )]
	public static object SetParent(
		[Desc( "GameObject id or unique name" )] string gameObject,
		[Desc( "New parent id; omit to move to scene root" )] string parentId = null )
	{
		var session = RequireSession();
		var go = FindGameObject( gameObject );
		var parent = parentId is null ? (GameObject)session.Scene : FindGameObject( parentId );

		using var undo = session.UndoScope( "MCP: reparent" )
			.WithGameObjectChanges( go, GameObjectUndoFlags.All ).Push();

		go.SetParent( parent, keepWorldPosition: true );
		return Describe( go );
	}

	[McpTool( "gameobject_get_transform", "Gets a GameObject's world and local transform.", ToolCategory.GameObject )]
	public static object GetTransform( [Desc( "GameObject id or unique name" )] string gameObject )
	{
		var go = FindGameObject( gameObject );

		return new
		{
			id = go.Id,
			name = go.Name,
			world = new { position = V( go.WorldPosition ), rotation = A( go.WorldRotation ), scale = V( go.WorldScale ) },
			local = new { position = V( go.LocalPosition ), rotation = A( go.LocalRotation ), scale = V( go.LocalScale ) }
		};
	}

	[McpTool( "gameobject_set_transform", "Sets position/rotation/scale on a GameObject. Omitted parts stay unchanged.", ToolCategory.GameObject, Writes = true )]
	public static object SetTransform(
		[Desc( "GameObject id or unique name" )] string gameObject,
		[Desc( "Position [x, y, z]" )] float[] position = null,
		[Desc( "Rotation [pitch, yaw, roll] in degrees" )] float[] rotation = null,
		[Desc( "Scale [x, y, z]" )] float[] scale = null,
		[Desc( "Apply in world space instead of local space" )] bool world = false )
	{
		var session = RequireSession();
		var go = FindGameObject( gameObject );

		using var undo = session.UndoScope( "MCP: set transform" )
			.WithGameObjectChanges( go, GameObjectUndoFlags.Properties ).Push();

		if ( position is not null )
		{
			var v = ToVector3( position, "position" );
			if ( world ) go.WorldPosition = v; else go.LocalPosition = v;
		}

		if ( rotation is not null )
		{
			if ( rotation.Length != 3 )
				throw new ArgumentException( "'rotation' must be [pitch, yaw, roll]" );

			var r = Rotation.From( rotation[0], rotation[1], rotation[2] );
			if ( world ) go.WorldRotation = r; else go.LocalRotation = r;
		}

		if ( scale is not null )
			go.LocalScale = ToVector3( scale, "scale" );

		return GetTransform( go.Id.ToString() );
	}

	[McpTool( "gameobject_duplicate", "Duplicates a GameObject next to the original.", ToolCategory.GameObject, Writes = true )]
	public static object Duplicate( [Desc( "GameObject id or unique name" )] string gameObject )
	{
		var session = RequireSession();
		var go = FindGameObject( gameObject );

		using var undo = session.UndoScope( $"MCP: duplicate {go.Name}" ).WithGameObjectCreations().Push();

		var clone = go.Clone( go.WorldTransform, go.Parent, go.Enabled, $"{go.Name} (copy)" );
		return Describe( clone );
	}

	[McpTool( "gameobject_find", "Searches GameObjects by name substring and/or component type.", ToolCategory.GameObject )]
	public static object Find(
		[Desc( "Name substring (case-insensitive); omit to match all" )] string query = null,
		[Desc( "Only objects having this component type" )] string componentType = null,
		int max = 50 )
	{
		var scene = RequireScene();

		var results = scene.GetAllObjects( false )
			.Where( o => o is not Scene )
			.Where( o => query is null || o.Name.Contains( query, StringComparison.OrdinalIgnoreCase ) )
			.Where( o => componentType is null || o.Components.GetAll<Component>( FindMode.EverythingInSelf )
				.Any( c => string.Equals( c.GetType().Name, componentType, StringComparison.OrdinalIgnoreCase )
					|| string.Equals( c.GetType().FullName, componentType, StringComparison.OrdinalIgnoreCase ) ) )
			.Take( max )
			.Select( Describe )
			.ToArray();

		return new { count = results.Length, results };
	}

	[McpTool( "gameobject_get_details", "Gets a GameObject with all component properties as JSON.", ToolCategory.GameObject )]
	public static object GetDetails( [Desc( "GameObject id or unique name" )] string gameObject )
	{
		var go = FindGameObject( gameObject );

		return new
		{
			id = go.Id,
			name = go.Name,
			enabled = go.Enabled,
			tags = go.Tags.TryGetAll().ToArray(),
			world = new { position = V( go.WorldPosition ), rotation = A( go.WorldRotation ), scale = V( go.WorldScale ) },
			parent = go.Parent is Scene ? null : (object)new { id = go.Parent?.Id, name = go.Parent?.Name },
			isPrefabInstance = go.IsPrefabInstance,
			prefabSource = go.PrefabInstanceSource,
			components = go.Components.GetAll<Component>( FindMode.EverythingInSelf )
				.Select( c => new
				{
					type = c.GetType().Name,
					enabled = c.Enabled,
					properties = c.Serialize()
				} ).ToArray()
		};
	}

	[McpTool( "gameobject_select", "Selects GameObjects in the editor (replaces current selection).", ToolCategory.GameObject )]
	public static object Select( [Desc( "GameObject ids or unique names" )] string[] gameObjects )
	{
		var session = RequireSession();
		var found = gameObjects.Select( FindGameObject ).ToList();

		session.Selection.Clear();
		foreach ( var go in found )
			session.Selection.Add( go );

		return new { selected = found.Select( g => g.Name ).ToArray() };
	}
}
