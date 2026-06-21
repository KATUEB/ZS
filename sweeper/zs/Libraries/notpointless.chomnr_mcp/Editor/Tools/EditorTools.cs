using System;
using System.Linq;
using Editor;
using Sandbox;
using SboxMcp.Integration;
using SboxMcp.Registry;
using SboxMcp.Server;
using static SboxMcp.Tools.ToolHelpers;

namespace SboxMcp.Tools;

public static class EditorTools
{
	[McpTool( "editor_get_logs", "Reads recent editor console output (newest first).", ToolCategory.Editor )]
	public static object GetLogs(
		int count = 100,
		[Desc( "Minimum severity: trace, info, warning or error" )] string minSeverity = null )
	{
		var logs = LogCapture.Recent( count, minSeverity )
			.Select( l => new { time = l.Time.ToString( "HH:mm:ss" ), level = l.Level, logger = l.Logger, message = l.Message } )
			.ToArray();

		return new { count = logs.Length, logs };
	}

	[McpTool( "editor_clear_logs", "Clears the captured console log buffer.", ToolCategory.Editor )]
	public static object ClearLogs()
	{
		LogCapture.Clear();
		return new { cleared = true };
	}

	[McpTool( "editor_screenshot", "Takes a screenshot rendered through the scene's camera and returns it as an image. Needs a CameraComponent in the scene.", ToolCategory.Editor )]
	public static object Screenshot(
		[Desc( "Image width in pixels" )] int width = 1280,
		[Desc( "Image height in pixels" )] int height = 720 )
	{
		var session = RequireSession();
		var scene = session.IsPlaying && Game.ActiveScene is not null ? Game.ActiveScene : session.Scene;

		if ( scene.Camera is null )
			throw new InvalidOperationException(
				"The scene has no enabled CameraComponent to render from - add one with component_add" );

		width = Math.Clamp( width, 64, 4096 );
		height = Math.Clamp( height, 64, 4096 );

		var pixmap = new Pixmap( width, height );

		if ( !scene.RenderToPixmap( pixmap ) )
			throw new InvalidOperationException( "Rendering failed - is the scene camera valid?" );

		var png = pixmap.GetPng();
		return new RawMcpResult( McpResults.ImageContent(
			Convert.ToBase64String( png ),
			$"{(session.IsPlaying ? "game" : "scene")} camera view, {width}x{height}" ) );
	}

	[McpTool( "editor_screenshot_from", "Renders the scene from an arbitrary viewpoint (no camera component needed) - use it to inspect what you built from any angle.", ToolCategory.Editor )]
	public static object ScreenshotFrom(
		[Desc( "Camera world position [x, y, z]" )] float[] position,
		[Desc( "Camera rotation [pitch, yaw, roll]; ignored when lookAt is set" )] float[] rotation = null,
		[Desc( "GameObject id/name to aim the camera at" )] string lookAt = null,
		int width = 1280,
		int height = 720 )
	{
		var session = RequireSession();
		var scene = session.Scene;

		width = Math.Clamp( width, 64, 4096 );
		height = Math.Clamp( height, 64, 4096 );

		// temporary camera, intentionally outside any undo scope
		var go = scene.CreateObject();
		try
		{
			go.Name = "__mcp_temp_camera";
			go.WorldPosition = ToVector3( position, "position" );

			if ( lookAt is not null )
			{
				var target = FindGameObject( lookAt );
				go.WorldRotation = Rotation.LookAt( target.WorldPosition - go.WorldPosition );
			}
			else if ( rotation is not null )
			{
				if ( rotation.Length != 3 )
					throw new ArgumentException( "'rotation' must be [pitch, yaw, roll]" );

				go.WorldRotation = Rotation.From( rotation[0], rotation[1], rotation[2] );
			}

			var camera = go.Components.Create<CameraComponent>();
			var pixmap = new Pixmap( width, height );

			if ( !camera.RenderToPixmap( pixmap ) )
				throw new InvalidOperationException( "Rendering failed" );

			return new RawMcpResult( McpResults.ImageContent(
				Convert.ToBase64String( pixmap.GetPng() ),
				$"view from [{string.Join( ", ", position )}], {width}x{height}" ) );
		}
		finally
		{
			go.Destroy();
		}
	}

	[McpTool( "editor_frame_object", "Points the editor viewport camera at a GameObject so the user can see it.", ToolCategory.Editor )]
	public static object FrameObject( [Desc( "GameObject id or unique name" )] string gameObject )
	{
		var session = RequireSession();
		var go = FindGameObject( gameObject );

		session.FrameTo( go.GetBounds() );
		return new { framed = go.Name };
	}

	[McpTool( "editor_play", "Enters play mode with the current scene.", ToolCategory.Editor, Writes = true )]
	public static object Play()
	{
		var session = RequireSession();

		if ( session.IsPlaying )
			return new { playing = true, note = "already in play mode" };

		EditorScene.Play();
		return new { playing = SceneEditorSession.Active?.IsPlaying ?? false };
	}

	[McpTool( "editor_stop", "Exits play mode.", ToolCategory.Editor, Writes = true )]
	public static object Stop()
	{
		var session = RequireSession();

		if ( !session.IsPlaying )
			return new { playing = false, note = "was not in play mode" };

		EditorScene.Stop();
		return new { playing = false };
	}

	[McpTool( "editor_is_playing", "Whether the editor is currently in play mode.", ToolCategory.Editor )]
	public static object IsPlaying()
	{
		return new { playing = SceneEditorSession.Active?.IsPlaying ?? false };
	}

	[McpTool( "editor_run_console_command", "Runs an editor console command (e.g. 'clear', convars).", ToolCategory.Editor, Writes = true )]
	public static object RunConsoleCommand( [Desc( "The console command line to run" )] string command )
	{
		Editor.ConsoleSystem.Run( command );
		return new { ran = command, note = "check editor_get_logs for output" };
	}

	[McpTool( "editor_get_project_info", "Gets the current project: title, ident, type, paths.", ToolCategory.Editor )]
	public static object GetProjectInfo()
	{
		var project = Project.Current
			?? throw new InvalidOperationException( "No project is loaded" );

		return new
		{
			title = project.Config?.Title,
			ident = project.Config?.Ident,
			org = project.Config?.Org,
			type = project.Config?.Type,
			rootPath = project.GetRootPath(),
			hasCode = project.HasCodePath(),
			hasEditorCode = project.HasEditorPath()
		};
	}

	[McpTool( "editor_get_selection", "Gets the GameObjects currently selected in the editor.", ToolCategory.Editor )]
	public static object GetSelection()
	{
		var session = RequireSession();
		var selected = session.Selection.OfType<GameObject>()
			.Select( o => new { id = o.Id, name = o.Name } )
			.ToArray();

		return new { count = selected.Length, selected };
	}
}
