using System;
using System.IO;

namespace SboxMcp.Server;

/// <summary>
/// Confines file access to the project root. Every file-touching tool resolves
/// paths through here.
/// </summary>
public static class PathJail
{
	/// <summary>
	/// Resolves <paramref name="path"/> (relative to root, or absolute) and
	/// throws if it escapes <paramref name="root"/>.
	/// </summary>
	public static string Resolve( string root, string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) )
			throw new ArgumentException( "Path must not be empty" );

		var rootFull = Path.GetFullPath( root )
			.TrimEnd( Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar );

		var combined = Path.IsPathRooted( path ) ? path : Path.Combine( rootFull, path );
		var full = Path.GetFullPath( combined );

		if ( !full.Equals( rootFull, StringComparison.OrdinalIgnoreCase )
			&& !full.StartsWith( rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase ) )
		{
			throw new UnauthorizedAccessException( $"Path '{path}' is outside the project and cannot be accessed" );
		}

		return full;
	}
}
