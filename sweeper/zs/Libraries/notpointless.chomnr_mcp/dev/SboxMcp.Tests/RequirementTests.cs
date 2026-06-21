using System.Text.Json;
using SboxMcp.Registry;
using Xunit;

namespace SboxMcp.Tests;

public static class GatedSampleTools
{
	[McpTool( "gated_sample", "Needs an integration.", ToolCategory.Retargeter, Requires = "some-library" )]
	public static string Gated() => "ran";
}

public class RequirementTests
{
	[Fact]
	public void Tool_without_requires_is_always_available()
	{
		var r = new ToolRegistry();
		r.AddAssembly( typeof( SampleTools ).Assembly );

		Assert.True( r.Find( "sample_greet" ).IsAvailable );
		Assert.Null( r.Find( "sample_greet" ).UnavailableReason );
	}

	[Fact]
	public void Requires_resolves_through_resolver()
	{
		var r = new ToolRegistry();
		r.AddAssembly( typeof( GatedSampleTools ).Assembly );
		var tool = r.Find( "gated_sample" );

		var previous = ToolRegistry.RequirementResolver;
		try
		{
			ToolRegistry.RequirementResolver = key => key == "some-library" ? "Not Installed" : null;
			Assert.False( tool.IsAvailable );
			Assert.Equal( "Not Installed", tool.UnavailableReason );

			ToolRegistry.RequirementResolver = _ => null;
			Assert.True( tool.IsAvailable );

			// no resolver wired = available
			ToolRegistry.RequirementResolver = null;
			Assert.True( tool.IsAvailable );
		}
		finally
		{
			ToolRegistry.RequirementResolver = previous;
		}
	}
}
