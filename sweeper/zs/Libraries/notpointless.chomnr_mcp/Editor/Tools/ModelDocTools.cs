using System;
using System.IO;
using Editor;
using Sandbox;
using SboxMcp.Registry;
using static SboxMcp.Tools.AssetTools;

namespace SboxMcp.Tools;

public static class ModelDocTools
{
	[McpTool( "modeldoc_create_from_mesh", "Creates a .vmdl model from a mesh asset (FBX/OBJ/SMD) with optional auto-generated collision.", ToolCategory.ModelDoc, Writes = true )]
	public static object CreateFromMesh(
		[Desc( "Source mesh asset path, e.g. 'models/crate.fbx'" )] string meshAssetPath,
		[Desc( "Project-relative output path ending in .vmdl; omit to place next to the mesh" )] string outputVmdlPath = null,
		[Desc( "Collision to generate from the render mesh" )] CollisionKind collision = CollisionKind.Hull )
	{
		var meshAsset = AssetSystem.FindByPath( meshAssetPath )
			?? throw new InvalidOperationException( $"No mesh asset at '{meshAssetPath}' - use asset_search" );

		var meshSource = meshAsset.GetSourceFile( true );

		var absolute = outputVmdlPath is not null
			? ResolveNewAssetPath( outputVmdlPath )
			: meshSource is not null
				? Path.ChangeExtension( meshSource, ".vmdl" )
				: throw new InvalidOperationException(
					$"'{meshAssetPath}' has no local source file - pass outputVmdlPath explicitly" );

		if ( File.Exists( absolute ) )
			throw new InvalidOperationException( $"'{absolute}' already exists - delete it first or pick another path" );

		var asset = EditorUtility.CreateModelFromMeshFile( meshAsset, absolute )
			?? throw new InvalidOperationException(
				$"The engine could not create a model from '{meshAssetPath}' - is it a valid mesh file?" );

		var physics = collision == CollisionKind.None
			? null
			: AddPhysics( asset.Path, collision );

		return new
		{
			created = asset.Path,
			compiled = asset.IsCompiled,
			collision = collision.ToString(),
			physicsResult = physics
		};
	}

	public enum CollisionKind { Hull, Mesh, None }

	[McpTool( "modeldoc_get", "Reads a .vmdl as JSON (or raw KV3 text). The node tree shows meshes, materials, physics, attachments, LODs, bodygroups.", ToolCategory.ModelDoc )]
	public static object Get(
		[Desc( "Model asset path, e.g. 'models/crate.vmdl'" )] string vmdlPath,
		[Desc( "Return the raw KV3 text instead of a JSON view" )] bool raw = false )
	{
		var asset = AssetSystem.FindByPath( vmdlPath )
			?? throw new InvalidOperationException( $"No model at '{vmdlPath}' - use asset_search with assetType 'vmdl'" );

		var text = File.ReadAllText( asset.GetSourceFile( true ) );

		if ( raw )
			return new { path = asset.Path, format = "kv3", content = text };

		var json = EditorUtility.KeyValues3ToJson( text )
			?? throw new InvalidOperationException( $"'{vmdlPath}' could not be parsed as KV3" );

		return new { path = asset.Path, format = "json (read-only view; write with modeldoc_set using KV3)", content = json };
	}

	[McpTool( "modeldoc_set", "Writes full KV3 content to a .vmdl (creating it if missing), then compiles. Get the current KV3 with modeldoc_get raw=true first; compile errors are reported back.", ToolCategory.ModelDoc, Writes = true )]
	public static object Set(
		[Desc( "Model asset path or project-relative path ending in .vmdl" )] string vmdlPath,
		[Desc( "Complete KV3 file content. The '<!-- kv3 ... -->' header line is added automatically when missing." )] string kv3Content )
	{
		if ( !kv3Content.TrimStart().StartsWith( "<!--" ) )
			kv3Content = VmdlHeader + "\n" + kv3Content;

		return AssetTools.WriteRaw( vmdlPath, kv3Content );
	}

	[McpTool( "modeldoc_add_physics", "Adds auto-generated collision (hull or mesh) to an existing .vmdl from its render meshes.", ToolCategory.ModelDoc, Writes = true )]
	public static object AddPhysicsTool(
		[Desc( "Model asset path" )] string vmdlPath,
		[Desc( "Collision kind to generate" )] CollisionKind collision = CollisionKind.Hull )
	{
		if ( collision == CollisionKind.None )
			throw new ArgumentException( "collision must be Hull or Mesh" );

		return AddPhysics( vmdlPath, collision );
	}

	static object AddPhysics( string vmdlPath, CollisionKind collision )
	{
		var asset = AssetSystem.FindByPath( vmdlPath )
			?? throw new InvalidOperationException( $"No model at '{vmdlPath}'" );

		var file = asset.GetSourceFile( true );
		var text = File.ReadAllText( file );

		if ( text.Contains( "PhysicsHullFile" ) || text.Contains( "PhysicsMeshFile" ) )
			return new { path = asset.Path, skipped = "model already has physics nodes" };

		// the render mesh files drive the physics shapes
		var meshFile = ExtractFirstRenderMeshFilename( text )
			?? throw new InvalidOperationException( $"'{vmdlPath}' has no RenderMeshFile node to build physics from" );

		var nodeClass = collision == CollisionKind.Mesh ? "PhysicsMeshFile" : "PhysicsHullFile";
		var physicsBlock =
			"\t\t\t{\n" +
			"\t\t\t\t_class = \"PhysicsShapeList\"\n" +
			"\t\t\t\tchildren = \n" +
			"\t\t\t\t[\n" +
			"\t\t\t\t\t{\n" +
			$"\t\t\t\t\t\t_class = \"{nodeClass}\"\n" +
			$"\t\t\t\t\t\tfilename = \"{meshFile}\"\n" +
			"\t\t\t\t\t\timport_scale = 1.0\n" +
			"\t\t\t\t\t\tparent_bone = \"\"\n" +
			"\t\t\t\t\t\tsurface_prop = \"default\"\n" +
			"\t\t\t\t\t\tcollision_tags = \"solid\"\n" +
			"\t\t\t\t\t},\n" +
			"\t\t\t\t]\n" +
			"\t\t\t},\n";

		// insert as the first entry of RootNode's children array
		var anchorIndex = FindRootChildrenStart( text )
			?? throw new InvalidOperationException( $"Could not locate the RootNode children array in '{vmdlPath}'" );

		text = text.Insert( anchorIndex, "\n" + physicsBlock );
		File.WriteAllText( file, text );
		asset.Compile( true );

		return new
		{
			path = asset.Path,
			added = nodeClass,
			compiled = asset.IsCompiled,
			note = asset.IsCompiled ? null : "compile failed - read the file with modeldoc_get raw=true and fix it with modeldoc_set"
		};
	}

	static string ExtractFirstRenderMeshFilename( string kv3 )
	{
		var meshIdx = kv3.IndexOf( "\"RenderMeshFile\"", StringComparison.Ordinal );
		if ( meshIdx < 0 ) return null;

		var fileIdx = kv3.IndexOf( "filename", meshIdx, StringComparison.Ordinal );
		if ( fileIdx < 0 ) return null;

		var firstQuote = kv3.IndexOf( '"', fileIdx );
		var secondQuote = firstQuote < 0 ? -1 : kv3.IndexOf( '"', firstQuote + 1 );
		return secondQuote < 0 ? null : kv3.Substring( firstQuote + 1, secondQuote - firstQuote - 1 );
	}

	static int? FindRootChildrenStart( string kv3 )
	{
		var rootIdx = kv3.IndexOf( "\"RootNode\"", StringComparison.Ordinal );
		if ( rootIdx < 0 ) return null;

		var childrenIdx = kv3.IndexOf( "children", rootIdx, StringComparison.Ordinal );
		if ( childrenIdx < 0 ) return null;

		var bracket = kv3.IndexOf( '[', childrenIdx );
		return bracket < 0 ? null : bracket + 1;
	}

	internal const string VmdlHeader =
		"<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:modeldoc29:version{3cec427c-1b0e-4d48-a90a-0436f33a6041} -->";
}
