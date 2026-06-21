using System;
using System.Collections.Generic;
using System.Linq;
using Editor;
using Sandbox;
using static Sandbox.Internal.GlobalToolsNamespace;

namespace SboxMcp.Tools;

internal static class ToolHelpers
{
	public static SceneEditorSession RequireSession() =>
		SceneEditorSession.Active
		?? throw new InvalidOperationException( "No scene is open in the editor - open or create a scene first" );

	public static Scene RequireScene() => RequireSession().Scene
		?? throw new InvalidOperationException( "The active editor session has no scene" );

	/// <summary>
	/// Resolves a GameObject by id (preferred) or by unique name.
	/// </summary>
	public static GameObject FindGameObject( string idOrName )
	{
		if ( string.IsNullOrWhiteSpace( idOrName ) )
			throw new ArgumentException( "GameObject id/name must not be empty" );

		var scene = RequireScene();

		if ( Guid.TryParse( idOrName, out var guid ) )
		{
			return scene.Directory.FindByGuid( guid )
				?? throw new InvalidOperationException( $"No GameObject with id '{idOrName}' - use gameobject_find to search" );
		}

		var matches = scene.GetAllObjects( false )
			.Where( o => o is not Scene )
			.Where( o => string.Equals( o.Name, idOrName, StringComparison.OrdinalIgnoreCase ) )
			.ToList();

		return matches.Count switch
		{
			1 => matches[0],
			0 => throw new InvalidOperationException( $"No GameObject named '{idOrName}' - use gameobject_find to search" ),
			_ => throw new InvalidOperationException(
				$"{matches.Count} GameObjects are named '{idOrName}' - use an id instead: "
				+ string.Join( ", ", matches.Take( 5 ).Select( m => m.Id ) ) )
		};
	}

	public static Component FindComponent( GameObject go, string typeName )
	{
		var components = go.Components.GetAll<Component>( FindMode.EverythingInSelf ).ToList();

		var match = components.FirstOrDefault( c => string.Equals( c.GetType().FullName, typeName, StringComparison.OrdinalIgnoreCase ) )
			?? components.FirstOrDefault( c => string.Equals( c.GetType().Name, typeName, StringComparison.OrdinalIgnoreCase ) );

		return match ?? throw new InvalidOperationException(
			$"'{go.Name}' has no component '{typeName}'. It has: "
			+ string.Join( ", ", components.Select( c => c.GetType().Name ) ) );
	}

	public static TypeDescription FindComponentType( string typeName )
	{
		var all = EditorTypeLibrary.GetTypes<Component>()
			.Where( t => !t.IsAbstract && !t.IsGenericType )
			.ToList();

		var match = all.FirstOrDefault( t => string.Equals( t.FullName, typeName, StringComparison.OrdinalIgnoreCase ) )
			?? all.FirstOrDefault( t => string.Equals( t.Name, typeName, StringComparison.OrdinalIgnoreCase ) );

		if ( match is not null )
			return match;

		var close = all
			.Where( t => t.Name.Contains( typeName, StringComparison.OrdinalIgnoreCase ) )
			.Take( 8 )
			.Select( t => t.Name )
			.ToList();

		throw new InvalidOperationException( close.Count > 0
			? $"No component type '{typeName}'. Did you mean: {string.Join( ", ", close )}?"
			: $"No component type '{typeName}' - use component_list_types to search" );
	}

	public static Vector3 ToVector3( float[] v, string argName )
	{
		if ( v is null || v.Length != 3 )
			throw new ArgumentException( $"'{argName}' must be an array of 3 numbers [x, y, z]" );

		return new Vector3( v[0], v[1], v[2] );
	}

	public static float[] V( Vector3 v ) => new[] { v.x, v.y, v.z };

	public static float[] A( Rotation r )
	{
		var angles = r.Angles();
		return new[] { angles.pitch, angles.yaw, angles.roll };
	}

	public static object Describe( GameObject go ) => new
	{
		id = go.Id,
		name = go.Name,
		enabled = go.Enabled,
		position = V( go.WorldPosition ),
		components = go.Components.GetAll<Component>( FindMode.EverythingInSelf )
			.Select( c => c.GetType().Name ).ToArray(),
		childCount = go.Children.Count,
		isPrefabInstance = go.IsPrefabInstance
	};

	public static object DescribeTree( GameObject go, int depth )
	{
		var components = go.Components.GetAll<Component>( FindMode.EverythingInSelf )
			.Select( c => c.GetType().Name ).ToArray();

		return new
		{
			id = go.Id,
			name = go.Name,
			enabled = go.Enabled,
			components,
			children = depth <= 0
				? (object)$"{go.Children.Count} children (increase maxDepth to see them)"
				: go.Children.Select( c => DescribeTree( c, depth - 1 ) ).ToArray()
		};
	}
}
