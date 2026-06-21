using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SboxMcp.Registry;

namespace SboxMcp.Integration;

/// <summary>
/// Lets the user expose public static methods from other installed libraries
/// as MCP tools. Imports are persisted (per editor, via cookies) and re-bound
/// every session; methods whose library is gone simply don't register until
/// it returns.
/// </summary>
public static class ToolImporter
{
	static readonly Type[] BindableParams =
	{
		typeof( string ), typeof( int ), typeof( long ), typeof( float ), typeof( double ),
		typeof( bool ), typeof( string[] ), typeof( int[] ), typeof( float[] )
	};

	// system/engine assemblies are never offered as import sources
	static readonly string[] ExcludedPrefixes =
	{
		"System", "Microsoft", "netstandard", "mscorlib", "Sandbox", "Facepunch",
		"NLog", "Sentry", "Refit", "protobuf", "Mono", "MonoMod", "Skia", "Topten",
		"Humanizer", "Azure", "LiteDB", "Fleck", "Zio", "ExCSS", "xunit", "JetBrains"
	};

	/// <summary>
	/// True for assemblies compiled from installed s&box libraries (named
	/// "package.{org}.{ident}[.editor]"), excluding the open project's own code.
	/// </summary>
	public static bool IsLibraryAssembly( Assembly assembly )
	{
		var name = assembly.GetName().Name ?? "";
		if ( !name.StartsWith( "package.", StringComparison.OrdinalIgnoreCase ) )
			return false;

		var config = Sandbox.Project.Current?.Config;
		if ( config is null )
			return true;

		return !name.StartsWith( $"package.{config.Org}.{config.Ident}", StringComparison.OrdinalIgnoreCase );
	}

	public static string FriendlyName( Assembly assembly )
	{
		var name = assembly.GetName().Name ?? "?";
		return name.StartsWith( "package.", StringComparison.OrdinalIgnoreCase ) ? name[8..] : name;
	}

	/// <summary>Loaded assemblies that look like user libraries with importable methods.</summary>
	public static IEnumerable<Assembly> CandidateAssemblies()
	{
		var own = typeof( ToolImporter ).Assembly;

		return AppDomain.CurrentDomain.GetAssemblies()
			.Where( a => !a.IsDynamic && a != own )
			.Where( a =>
			{
				var name = a.GetName().Name ?? "";
				return name.Length > 0 && !ExcludedPrefixes.Any( p => name.StartsWith( p, StringComparison.OrdinalIgnoreCase ) );
			} )
			.Where( a => CandidateMethods( a ).Any() )
			.OrderBy( a => a.GetName().Name );
	}

	/// <summary>Public static methods with simple, schema-expressible parameters.</summary>
	public static IEnumerable<MethodInfo> CandidateMethods( Assembly assembly )
	{
		Type[] types;
		try { types = assembly.GetExportedTypes(); }
		catch { yield break; }

		foreach ( var type in types.Where( t => t.IsClass && !t.IsGenericTypeDefinition ) )
		{
			foreach ( var method in type.GetMethods( BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly ) )
			{
				if ( method.IsSpecialName || method.IsGenericMethodDefinition )
					continue;

				if ( method.GetParameters().All( p => BindableParams.Contains( p.ParameterType ) || p.ParameterType.IsEnum ) )
					yield return method;
			}
		}
	}

	public static string ToolNameFor( ImportedToolDef def )
	{
		var typeName = def.Type.Split( '.' ).Last();
		return Sanitize( $"lib_{typeName}_{def.Method}" );
	}

	static string Sanitize( string name ) =>
		new( name.Select( c => char.IsLetterOrDigit( c ) ? char.ToLowerInvariant( c ) : '_' ).ToArray() );

	public static bool IsImported( MethodInfo method ) =>
		McpSettings.ImportedTools.Contains( DefFor( method ) );

	public static ImportedToolDef DefFor( MethodInfo method ) =>
		new( method.DeclaringType?.Assembly.GetName().Name, method.DeclaringType?.FullName, method.Name );

	/// <summary>Imports a method now and persists the choice.</summary>
	public static void Import( MethodInfo method )
	{
		var def = DefFor( method );
		McpSettings.AddImportedTool( def );
		Register( McpHost.Registry, def, method );
	}

	/// <summary>Removes an import now and persists the choice.</summary>
	public static void Unimport( MethodInfo method )
	{
		var def = DefFor( method );
		McpSettings.RemoveImportedTool( def );
		McpHost.Registry?.Remove( ToolNameFor( def ) );
	}

	/// <summary>Re-binds every persisted import that still resolves.</summary>
	public static void RegisterSaved( ToolRegistry registry )
	{
		foreach ( var def in McpSettings.ImportedTools )
		{
			var method = Resolve( def );
			if ( method is not null )
				Register( registry, def, method );
			else
				McpHost.Log.Warning( $"Imported tool {def.Type}.{def.Method} not found ({def.Assembly} missing?) - it will return when the library does" );
		}
	}

	static MethodInfo Resolve( ImportedToolDef def )
	{
		var assembly = AppDomain.CurrentDomain.GetAssemblies()
			.LastOrDefault( a => a.GetName().Name == def.Assembly );

		var type = assembly?.GetType( def.Type );
		return type?.GetMethods( BindingFlags.Public | BindingFlags.Static )
			.FirstOrDefault( m => m.Name == def.Method && !m.IsGenericMethodDefinition );
	}

	static void Register( ToolRegistry registry, ImportedToolDef def, MethodInfo method )
	{
		if ( registry is null )
			return;

		var parameters = string.Join( ", ", method.GetParameters().Select( p => p.Name ) );
		registry.AddImported(
			ToolNameFor( def ),
			$"Imported from the '{def.Assembly}' library: {def.Type.Split( '.' ).Last()}.{def.Method}({parameters})",
			ToolCategory.Imported,
			method );
	}
}
