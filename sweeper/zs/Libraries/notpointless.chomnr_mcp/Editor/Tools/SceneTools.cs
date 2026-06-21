using System;
using System.Linq;
using Editor;
using Sandbox;
using SboxMcp.Registry;
using static SboxMcp.Tools.ToolHelpers;

namespace SboxMcp.Tools;

public static class SceneTools
{
	[McpTool( "scene_get_status", "Gets the active scene: name, play state, unsaved changes, object count.", ToolCategory.Scene )]
	public static object GetStatus()
	{
		var session = RequireSession();
		var scene = session.Scene;

		return new
		{
			name = scene.Name,
			isPlaying = session.IsPlaying,
			hasUnsavedChanges = session.HasUnsavedChanges,
			objectCount = scene.GetAllObjects( false ).Count( o => o is not Sandbox.Scene ),
			selection = session.Selection.OfType<Sandbox.GameObject>().Select( o => new { id = o.Id, name = o.Name } ).ToArray()
		};
	}

	[McpTool( "scene_get_hierarchy", "Gets the scene's GameObject tree with ids, names and component types.", ToolCategory.Scene )]
	public static object GetHierarchy(
		[Desc( "How many levels deep to expand" )] int maxDepth = 4,
		[Desc( "Id of a GameObject to use as the root; omit for the whole scene" )] string rootId = null )
	{
		if ( rootId is not null )
			return DescribeTree( FindGameObject( rootId ), maxDepth );

		var scene = RequireScene();
		return new
		{
			scene = scene.Name,
			objects = scene.Children.Select( c => DescribeTree( c, maxDepth - 1 ) ).ToArray()
		};
	}

	[McpTool( "scene_create", "Creates a new scene (with a camera and a light) and makes it active. Save it with scene_save_as.", ToolCategory.Scene, Writes = true )]
	public static object Create()
	{
		var session = SceneEditorSession.CreateDefault();
		session.MakeActive();
		return new { created = session.Scene.Name, note = "unsaved - use scene_save_as to write it to disk" };
	}

	[McpTool( "scene_open", "Opens a scene (or prefab) from disk in the editor and makes it active.", ToolCategory.Scene )]
	public static object Open( [Desc( "Scene asset path, e.g. 'scenes/minimal.scene'" )] string scenePath )
	{
		var session = SceneEditorSession.CreateFromPath( scenePath )
			?? throw new InvalidOperationException( $"No scene at '{scenePath}' - use scene_list" );

		session.MakeActive();
		return new { opened = session.Scene.Name };
	}

	[McpTool( "scene_list", "Lists all scene assets in the project.", ToolCategory.Scene )]
	public static object List()
	{
		var scenes = AssetSystem.All
			.Where( a => string.Equals( a.AssetType?.FileExtension, "scene", StringComparison.OrdinalIgnoreCase ) )
			.Select( a => a.Path )
			.OrderBy( p => p )
			.ToArray();

		return new { count = scenes.Length, scenes };
	}

	[McpTool( "scene_save", "Saves the active scene to disk. Fails for never-saved scenes - use scene_save_as for those.", ToolCategory.Scene, Writes = true )]
	public static object Save()
	{
		var session = RequireSession();

		if ( session.Scene.Source is null )
			throw new InvalidOperationException( "This scene has never been saved - use scene_save_as with a path" );

		session.Save( false );
		return new { saved = true, scene = session.Scene.Name };
	}

	[McpTool( "scene_save_as", "Saves the active scene to a new path under Assets/ (works for never-saved scenes).", ToolCategory.Scene, Writes = true )]
	public static object SaveAs( [Desc( "Assets-relative path ending in .scene, e.g. 'scenes/level1.scene'" )] string scenePath )
	{
		var session = RequireSession();
		var scene = session.Scene;

		if ( !scenePath.EndsWith( ".scene", StringComparison.OrdinalIgnoreCase ) )
			throw new ArgumentException( "scenePath must end in .scene" );

		var absolute = AssetTools.ResolveNewAssetPath( scenePath );
		System.IO.Directory.CreateDirectory( System.IO.Path.GetDirectoryName( absolute ) );

		var asset = AssetSystem.CreateResource( "scene", absolute )
			?? throw new InvalidOperationException( $"Could not create a scene resource at '{scenePath}' - is the path inside the project?" );

		// mirror of SceneEditorSession.Save: Scene.CreateSceneFile() is internal,
		// so reach it via reflection (same flow the editor's own Ctrl+S runs)
		var createSceneFile = typeof( Scene ).GetMethod( "CreateSceneFile",
			System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic )
			?? throw new InvalidOperationException( "Scene.CreateSceneFile not found - the engine changed; report this" );

		var resource = (Sandbox.GameResource)createSceneFile.Invoke( scene, null );
		asset.SaveToDisk( resource );

		// Scene.Source's setter is internal - reflection again, matching the editor's save flow
		typeof( Scene ).GetProperty( "Source" )?.SetValue( scene, resource );
		scene.Name = System.IO.Path.GetFileNameWithoutExtension( absolute );
		session.HasUnsavedChanges = false;

		return new { saved = asset.Path };
	}

	[McpTool( "scene_undo", "Undoes the last editor action.", ToolCategory.Scene, Writes = true )]
	public static object Undo()
	{
		var ok = RequireSession().UndoSystem.Undo();
		return new { undone = ok };
	}

	[McpTool( "scene_redo", "Redoes the last undone editor action.", ToolCategory.Scene, Writes = true )]
	public static object Redo()
	{
		var ok = RequireSession().UndoSystem.Redo();
		return new { redone = ok };
	}
}
