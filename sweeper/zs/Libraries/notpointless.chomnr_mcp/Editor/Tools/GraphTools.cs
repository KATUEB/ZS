using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Editor;
using Sandbox;
using SboxMcp.Registry;
using static SboxMcp.Tools.AssetTools;

namespace SboxMcp.Tools;

/// <summary>
/// AnimGraph (.vanmgrph, KV3), ShaderGraph (.shdrgrph, JSON) and
/// ActionGraph (JSON) authoring at the file-format level, with compile
/// verification through the asset system.
/// </summary>
public static class GraphTools
{
	// ---- AnimGraph -------------------------------------------------------

	internal const string AnimGraphHeader =
		"<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:animgraph2:version{0f7898b8-5471-45c4-9867-cd9c46bcfdb5} -->";

	[McpTool( "animgraph_get", "Reads an animation graph (.vanmgrph) as JSON (or raw KV3). Shows nodes, states, transitions and parameters.", ToolCategory.AnimGraph )]
	public static object AnimGraphGet(
		[Desc( "Animgraph asset path, e.g. 'models/citizen/citizen.vanmgrph'" )] string path,
		[Desc( "Return raw KV3 text instead of a JSON view" )] bool raw = false )
	{
		var asset = AssetSystem.FindByPath( path )
			?? throw new InvalidOperationException( $"No animgraph at '{path}' - use asset_search with assetType 'vanmgrph'" );

		var text = File.ReadAllText( asset.GetSourceFile( true ) );

		if ( raw )
			return new { path = asset.Path, format = "kv3", content = text };

		var json = EditorUtility.KeyValues3ToJson( text )
			?? throw new InvalidOperationException( $"'{path}' could not be parsed as KV3" );

		return new { path = asset.Path, format = "json (read-only view; write with animgraph_set using KV3)", content = json };
	}

	[McpTool( "animgraph_set", "Writes full KV3 content to a .vanmgrph (creating it if missing), then compiles. Read an existing graph with animgraph_get raw=true to learn the format.", ToolCategory.AnimGraph, Writes = true )]
	public static object AnimGraphSet(
		[Desc( "Animgraph asset path or project-relative path ending in .vanmgrph" )] string path,
		[Desc( "Complete KV3 file content. The '<!-- kv3 ... -->' header is added automatically when missing." )] string kv3Content )
	{
		if ( !kv3Content.TrimStart().StartsWith( "<!--" ) )
			kv3Content = AnimGraphHeader + "\n" + kv3Content;

		return AssetTools.WriteRaw( path, kv3Content );
	}

	[McpTool( "animgraph_list_parameters", "Lists an animation graph's parameters with their value types (the knobs SkinnedModelRenderer.Set drives).", ToolCategory.AnimGraph )]
	public static object AnimGraphListParameters( [Desc( "Animgraph asset path" )] string path )
	{
		var asset = AssetSystem.FindByPath( path )
			?? throw new InvalidOperationException( $"No animgraph at '{path}'" );

		var graph = AnimationGraph.Load( asset.Path )
			?? throw new InvalidOperationException( $"'{path}' failed to load - is it compiled? Try asset_compile first" );

		var parameters = Enumerable.Range( 0, graph.ParamCount )
			.Select( i => new
			{
				name = graph.GetParameterName( i ),
				type = graph.GetParameterType( i )?.Name
			} )
			.ToArray();

		return new { path = asset.Path, count = parameters.Length, parameters };
	}

	// ---- ShaderGraph -----------------------------------------------------

	[McpTool( "shadergraph_get", "Reads a shader graph (.shdrgrph). The format is JSON: nodes keyed by id with _class, inputs referencing other nodes' outputs.", ToolCategory.ShaderGraph )]
	public static object ShaderGraphGet( [Desc( "Shadergraph asset path" )] string path )
	{
		var asset = AssetSystem.FindByPath( path )
			?? throw new InvalidOperationException( $"No shadergraph at '{path}' - use asset_search with assetType 'shdrgrph'" );

		return new { path = asset.Path, content = File.ReadAllText( asset.GetSourceFile( true ) ) };
	}

	[McpTool( "shadergraph_set", "Writes full JSON content to a .shdrgrph (creating it if missing), then compiles. Read an existing graph first to learn the node schema.", ToolCategory.ShaderGraph, Writes = true )]
	public static object ShaderGraphSet(
		[Desc( "Shadergraph asset path or project-relative path ending in .shdrgrph" )] string path,
		[Desc( "Complete JSON file content" )] string jsonContent )
	{
		ValidateJson( jsonContent, path );
		return AssetTools.WriteRaw( path, jsonContent );
	}

	[McpTool( "shadergraph_list_nodes", "Lists the nodes in a shader graph: id, class and position.", ToolCategory.ShaderGraph )]
	public static object ShaderGraphListNodes( [Desc( "Shadergraph asset path" )] string path )
	{
		var asset = AssetSystem.FindByPath( path )
			?? throw new InvalidOperationException( $"No shadergraph at '{path}'" );

		var doc = JsonDocument.Parse( File.ReadAllText( asset.GetSourceFile( true ) ) );

		if ( !doc.RootElement.TryGetProperty( "nodes", out var nodes ) )
			return new { path = asset.Path, nodes = Array.Empty<object>() };

		var list = nodes.ValueKind switch
		{
			JsonValueKind.Array => nodes.EnumerateArray().Select( DescribeNode ).ToArray(),
			JsonValueKind.Object => nodes.EnumerateObject().Select( p => DescribeNode( p.Value ) ).ToArray(),
			_ => Array.Empty<object>()
		};

		return new { path = asset.Path, count = list.Length, nodes = list };
	}

	static object DescribeNode( JsonElement node ) => new
	{
		id = TryString( node, "Identifier" ) ?? TryString( node, "identifier" ),
		cls = TryString( node, "_class" ),
		position = TryString( node, "Position" ) ?? TryString( node, "position" )
	};

	static string TryString( JsonElement el, string name ) =>
		el.ValueKind == JsonValueKind.Object && el.TryGetProperty( name, out var v ) ? v.ToString() : null;

	// ---- ActionGraph -----------------------------------------------------

	[McpTool( "actiongraph_get", "Reads an action graph asset (visual scripting). The format is JSON.", ToolCategory.ActionGraph )]
	public static object ActionGraphGet( [Desc( "Action graph asset path, e.g. 'graphs/thing.action'" )] string path )
	{
		var asset = AssetSystem.FindByPath( path )
			?? throw new InvalidOperationException( $"No action graph at '{path}' - use asset_search with assetType 'action'" );

		return new { path = asset.Path, content = File.ReadAllText( asset.GetSourceFile( true ) ) };
	}

	[McpTool( "actiongraph_set", "Writes full JSON content to an action graph asset (creating it if missing), then compiles. Read an existing graph first to learn the node schema.", ToolCategory.ActionGraph, Writes = true )]
	public static object ActionGraphSet(
		[Desc( "Action graph asset path or project-relative path" )] string path,
		[Desc( "Complete JSON file content" )] string jsonContent )
	{
		ValidateJson( jsonContent, path );
		return AssetTools.WriteRaw( path, jsonContent );
	}

	static void ValidateJson( string json, string path )
	{
		try
		{
			using var _ = JsonDocument.Parse( json );
		}
		catch ( JsonException e )
		{
			throw new ArgumentException( $"Content for '{path}' is not valid JSON: {e.Message}" );
		}
	}
}
