using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Editor;
using Sandbox;
using SboxMcp.Registry;
using static SboxMcp.Tools.AssetTools;

namespace SboxMcp.Tools;

/// <summary>
/// Material and sound-event authoring.
/// </summary>
public static class ContentTools
{
	[McpTool( "material_create", "Creates a .vmat material using the standard complex shader, with optional texture maps. Edit further with asset_read_raw / asset_write_raw.", ToolCategory.Asset, Writes = true )]
	public static object MaterialCreate(
		[Desc( "Output path ending in .vmat, e.g. 'materials/crate.vmat'" )] string path,
		[Desc( "Color/albedo texture asset path (png/jpg/tga)" )] string colorTexture = null,
		[Desc( "Normal map texture path" )] string normalTexture = null,
		[Desc( "Roughness texture path" )] string roughnessTexture = null,
		[Desc( "Metalness texture path" )] string metalnessTexture = null )
	{
		if ( !path.EndsWith( ".vmat", StringComparison.OrdinalIgnoreCase ) )
			throw new ArgumentException( "path must end in .vmat" );

		var sb = new StringBuilder();
		sb.AppendLine( "\"Layer0\"" );
		sb.AppendLine( "{" );
		sb.AppendLine( "\t\"shader\"\t\t\"shaders/complex.shader_c\"" );

		void Slot( string key, string texture )
		{
			if ( !string.IsNullOrWhiteSpace( texture ) )
				sb.AppendLine( $"\t\"{key}\"\t\t\"{texture}\"" );
		}

		Slot( "TextureColor", colorTexture );
		Slot( "TextureNormal", normalTexture );
		Slot( "TextureRoughness", roughnessTexture );
		Slot( "TextureMetalness", metalnessTexture );

		if ( !string.IsNullOrWhiteSpace( metalnessTexture ) )
			sb.AppendLine( "\t\"F_METALNESS_TEXTURE\"\t\t\"1\"" );

		sb.AppendLine( "}" );

		return AssetTools.WriteRaw( path, sb.ToString() );
	}

	[McpTool( "texture_write", "Saves base64-encoded image bytes (png/jpg/tga) as a texture asset - pipe in generated or downloaded images, then use them in material_create.", ToolCategory.Asset, Writes = true )]
	public static object TextureWrite(
		[Desc( "Output path ending in .png/.jpg/.tga, e.g. 'textures/crate_color.png'" )] string path,
		[Desc( "The image file's bytes, base64-encoded" )] string base64Data )
	{
		var extension = Path.GetExtension( path ).ToLowerInvariant();
		if ( extension is not (".png" or ".jpg" or ".jpeg" or ".tga") )
			throw new ArgumentException( "path must end in .png, .jpg or .tga" );

		byte[] bytes;
		try
		{
			bytes = Convert.FromBase64String( base64Data );
		}
		catch ( FormatException )
		{
			throw new ArgumentException( "base64Data is not valid base64" );
		}

		var absolute = ResolveNewAssetPath( path );
		Directory.CreateDirectory( Path.GetDirectoryName( absolute ) );
		File.WriteAllBytes( absolute, bytes );

		var asset = AssetSystem.RegisterFile( absolute );
		asset?.Compile( true );

		return new { written = path, bytes = bytes.Length, registered = asset is not null };
	}

	[McpTool( "soundevent_create", "Creates a .sound event resource referencing sound files (wav/mp3/ogg), playable via SoundEvent components or Sound.Play.", ToolCategory.Asset, Writes = true )]
	public static object SoundEventCreate(
		[Desc( "Output path ending in .sound, e.g. 'sounds/jump.sound'" )] string path,
		[Desc( "Sound file asset paths; one is picked at random when playing" )] string[] soundFiles,
		[Desc( "Volume 0-1" )] float volume = 1f,
		[Desc( "Pitch multiplier" )] float pitch = 1f,
		[Desc( "2D UI sound (no spatialization)" )] bool ui = false )
	{
		if ( !path.EndsWith( ".sound", StringComparison.OrdinalIgnoreCase ) )
			throw new ArgumentException( "path must end in .sound" );

		if ( soundFiles is null || soundFiles.Length == 0 )
			throw new ArgumentException( "Pass at least one sound file path" );

		var json = JsonSerializer.Serialize( new Dictionary<string, object>
		{
			["Sounds"] = soundFiles,
			["UI"] = ui,
			["Volume"] = volume,
			["Pitch"] = pitch
		}, new JsonSerializerOptions { WriteIndented = true } );

		return AssetTools.WriteRaw( path, json );
	}
}
