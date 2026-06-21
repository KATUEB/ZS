using SboxMcp.Server;
using Xunit;

namespace SboxMcp.Tests;

public class PathJailTests
{
	const string Root = @"C:\project";

	[Theory]
	[InlineData( "Assets/models/crate.vmdl" )]
	[InlineData( @"Code\Player.cs" )]
	[InlineData( "file.txt" )]
	[InlineData( @"C:\project\Editor\Tool.cs" )]
	public void Allows_paths_inside_root( string path )
	{
		var resolved = PathJail.Resolve( Root, path );
		Assert.StartsWith( Root, resolved, StringComparison.OrdinalIgnoreCase );
	}

	[Theory]
	[InlineData( @"..\outside.txt" )]
	[InlineData( @"..\..\Windows\System32\drivers\etc\hosts" )]
	[InlineData( @"Code\..\..\outside.txt" )]
	[InlineData( @"C:\Windows\System32\cmd.exe" )]
	[InlineData( @"D:\other\place.txt" )]
	[InlineData( @"Assets/../../escape.txt" )]
	public void Blocks_paths_outside_root( string path )
	{
		Assert.Throws<UnauthorizedAccessException>( () => PathJail.Resolve( Root, path ) );
	}

	[Fact]
	public void Blocks_empty_path()
	{
		Assert.Throws<ArgumentException>( () => PathJail.Resolve( Root, "  " ) );
	}

	[Fact]
	public void Sneaky_prefix_sibling_is_blocked()
	{
		// C:\project-evil shares the prefix "C:\project" but is outside
		Assert.Throws<UnauthorizedAccessException>( () => PathJail.Resolve( Root, @"C:\project-evil\file.txt" ) );
	}

	[Fact]
	public void Root_itself_is_allowed()
	{
		Assert.Equal( Root, PathJail.Resolve( Root, @"C:\project" ), ignoreCase: true );
	}
}
