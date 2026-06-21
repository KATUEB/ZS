using System.Text.Json;
using SboxMcp.Registry;
using Xunit;

namespace SboxMcp.Tests;

public static class SampleTools
{
	public enum Flavor { Sweet, Sour }

	[McpTool( "sample_greet", "Greets someone.", ToolCategory.Editor )]
	public static string Greet(
		[Desc( "Who to greet" )] string name,
		int times = 1 )
	{
		var s = "";
		for ( var i = 0; i < times; i++ ) s += $"hi {name};";
		return s;
	}

	[McpTool( "sample_struct", "Returns structured data.", ToolCategory.Scene, Writes = true )]
	public static object Structured( float scale, bool enabled, string[] tags, Flavor flavor = Flavor.Sweet )
		=> new { scale, enabled, tags, flavor = flavor.ToString() };

	[McpTool( "sample_void", "Does nothing visible.", ToolCategory.Code )]
	public static void DoNothing() { }

	// not a tool - must not be discovered
	public static string NotATool() => "nope";
}

public class RegistryTests
{
	static ToolRegistry Build()
	{
		var r = new ToolRegistry();
		r.AddAssembly( typeof( SampleTools ).Assembly );
		return r;
	}

	[Fact]
	public void Discovers_annotated_static_methods_only()
	{
		var r = Build();

		Assert.NotNull( r.Find( "sample_greet" ) );
		Assert.NotNull( r.Find( "sample_struct" ) );
		Assert.NotNull( r.Find( "sample_void" ) );
		Assert.Null( r.Find( "not_a_tool" ) );
	}

	[Fact]
	public void Schema_types_required_and_enum()
	{
		var r = Build();
		var schema = r.Find( "sample_struct" ).Descriptor.InputSchema;

		Assert.Equal( "object", schema.GetProperty( "type" ).GetString() );
		var props = schema.GetProperty( "properties" );
		Assert.Equal( "number", props.GetProperty( "scale" ).GetProperty( "type" ).GetString() );
		Assert.Equal( "boolean", props.GetProperty( "enabled" ).GetProperty( "type" ).GetString() );
		Assert.Equal( "array", props.GetProperty( "tags" ).GetProperty( "type" ).GetString() );
		Assert.Equal( "string", props.GetProperty( "tags" ).GetProperty( "items" ).GetProperty( "type" ).GetString() );
		Assert.Equal( "string", props.GetProperty( "flavor" ).GetProperty( "type" ).GetString() );
		Assert.Contains( "Sweet", props.GetProperty( "flavor" ).GetProperty( "enum" ).EnumerateArray().Select( e => e.GetString() ) );

		var required = schema.GetProperty( "required" ).EnumerateArray().Select( e => e.GetString() ).ToArray();
		Assert.Equal( new[] { "scale", "enabled", "tags" }, required );
	}

	[Fact]
	public void Schema_includes_param_description()
	{
		var r = Build();
		var schema = r.Find( "sample_greet" ).Descriptor.InputSchema;

		Assert.Equal( "Who to greet",
			schema.GetProperty( "properties" ).GetProperty( "name" ).GetProperty( "description" ).GetString() );
	}

	[Fact]
	public void Invoke_binds_args_and_defaults()
	{
		var r = Build();
		var args = JsonDocument.Parse( """{"name":"bob"}""" ).RootElement;

		var result = r.Find( "sample_greet" ).Invoke( args );

		Assert.Equal( "hi bob;", result );
	}

	[Fact]
	public void Invoke_binds_enum_and_array()
	{
		var r = Build();
		var args = JsonDocument.Parse( """{"scale":2.5,"enabled":true,"tags":["a","b"],"flavor":"Sour"}""" ).RootElement;

		var result = r.Find( "sample_struct" ).Invoke( args );
		var json = JsonDocument.Parse( ToolRegistry.FormatResult( result ) ).RootElement;

		Assert.Equal( 2.5f, json.GetProperty( "scale" ).GetSingle() );
		Assert.Equal( "Sour", json.GetProperty( "flavor" ).GetString() );
	}

	[Fact]
	public void Invoke_missing_required_throws()
	{
		var r = Build();
		var args = JsonDocument.Parse( "{}" ).RootElement;

		var ex = Assert.Throws<ToolArgumentException>( () => r.Find( "sample_greet" ).Invoke( args ) );
		Assert.Contains( "name", ex.Message );
	}

	[Fact]
	public void Void_tool_formats_as_ok()
	{
		var r = Build();
		var result = r.Find( "sample_void" ).Invoke( null );

		Assert.Contains( "ok", ToolRegistry.FormatResult( result ) );
	}

	[Fact]
	public void Writes_flag_carried_on_metadata()
	{
		var r = Build();

		Assert.True( r.Find( "sample_struct" ).Meta.Writes );
		Assert.False( r.Find( "sample_greet" ).Meta.Writes );
	}
}
