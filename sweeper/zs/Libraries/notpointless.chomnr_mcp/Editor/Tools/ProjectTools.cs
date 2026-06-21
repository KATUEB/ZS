using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Editor;
using Sandbox;
using SboxMcp.Registry;

namespace SboxMcp.Tools;

/// <summary>
/// Project configuration: input actions and startup scene.
/// </summary>
public static class ProjectTools
{
	[McpTool( "input_list_actions", "Lists the project's input actions (the names used with Input.Pressed/Down in code).", ToolCategory.Editor )]
	public static object ListActions()
	{
		var settings = ProjectSettings.Input
			?? throw new InvalidOperationException( "Input settings are unavailable - is a project loaded?" );

		var actions = (settings.Actions ?? new())
			.Select( a => new { name = a.Name, group = a.GroupName, keyboard = a.KeyboardCode, gamepad = a.GamepadCode.ToString() } )
			.ToArray();

		return new { count = actions.Length, actions };
	}

	[McpTool( "input_add_action", "Adds an input action to the project (use the name with Input.Pressed in code). Applies on next play.", ToolCategory.Editor, Writes = true )]
	public static object AddAction(
		[Desc( "Action name, e.g. 'Dash'" )] string name,
		[Desc( "Keyboard key, e.g. 'shift', 'e', 'mouse1'" )] string keyboardCode,
		[Desc( "Group shown in settings UI, e.g. 'Movement'" )] string group = "Other" )
	{
		var settings = ProjectSettings.Input
			?? throw new InvalidOperationException( "Input settings are unavailable - is a project loaded?" );

		settings.Actions ??= new();

		if ( settings.Actions.Any( a => string.Equals( a.Name, name, StringComparison.OrdinalIgnoreCase ) ) )
			throw new InvalidOperationException( $"An input action named '{name}' already exists" );

		settings.Actions.Add( new InputAction { Name = name, KeyboardCode = keyboardCode, GroupName = group } );
		SaveInputSettings( settings );

		return new { added = name, keyboard = keyboardCode, group, note = "available to code as Input.Pressed(\"" + name + "\") after entering play mode" };
	}

	[McpTool( "input_remove_action", "Removes an input action from the project.", ToolCategory.Editor, Writes = true )]
	public static object RemoveAction( [Desc( "Action name" )] string name )
	{
		var settings = ProjectSettings.Input
			?? throw new InvalidOperationException( "Input settings are unavailable - is a project loaded?" );

		var action = settings.Actions?.FirstOrDefault( a => string.Equals( a.Name, name, StringComparison.OrdinalIgnoreCase ) )
			?? throw new InvalidOperationException( $"No input action named '{name}' - use input_list_actions" );

		settings.Actions.Remove( action );
		SaveInputSettings( settings );

		return new { removed = action.Name };
	}

	static void SaveInputSettings( InputSettings settings )
	{
		var root = AssetTools.ProjectRoot;
		var dir = Path.Combine( root, "ProjectSettings" );
		Directory.CreateDirectory( dir );
		File.WriteAllText( Path.Combine( dir, "Input.config" ), Json.Serialize( settings ) );
	}

	[McpTool( "project_set_startup_scene", "Sets the scene the game opens with when launched.", ToolCategory.Editor, Writes = true )]
	public static object SetStartupScene( [Desc( "Scene asset path, e.g. 'scenes/main_menu.scene'" )] string scenePath )
	{
		var asset = AssetSystem.FindByPath( scenePath )
			?? throw new InvalidOperationException( $"No scene at '{scenePath}' - use scene_list" );

		var root = AssetTools.ProjectRoot;
		var sbproj = Directory.GetFiles( root, "*.sbproj" ).FirstOrDefault()
			?? throw new InvalidOperationException( "No .sbproj file found in the project root" );

		var json = JsonNode.Parse( File.ReadAllText( sbproj ) ) as JsonObject
			?? throw new InvalidOperationException( "Could not parse the .sbproj file" );

		var metadata = json["Metadata"] as JsonObject;
		if ( metadata is null )
			json["Metadata"] = metadata = new JsonObject();

		metadata["StartupScene"] = asset.Path;
		File.WriteAllText( sbproj, json.ToJsonString( new System.Text.Json.JsonSerializerOptions { WriteIndented = true } ) );

		return new { startupScene = asset.Path };
	}
}
