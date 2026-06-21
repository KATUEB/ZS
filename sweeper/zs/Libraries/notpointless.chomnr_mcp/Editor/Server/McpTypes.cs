using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace SboxMcp.Server;

/// <summary>
/// A tool as advertised to MCP clients via tools/list.
/// </summary>
public record McpToolDescriptor( string Name, string Description, JsonElement InputSchema );

/// <summary>
/// Result payload shapes defined by the MCP specification.
/// </summary>
public static class McpResults
{
	public const string ServerName = "sbox-mcp";
	public const string ServerVersion = "1.0.0";

	public static object Initialize( string negotiatedVersion ) => new
	{
		protocolVersion = negotiatedVersion,
		capabilities = new { tools = new { listChanged = false } },
		serverInfo = new { name = ServerName, version = ServerVersion }
	};

	public static object ToolsList( IEnumerable<McpToolDescriptor> tools ) => new
	{
		tools = tools.ToArray()
	};

	public static object TextContent( string text, bool isError = false ) => new
	{
		content = new object[] { new { type = "text", text } },
		isError
	};

	public static object ImageContent( string base64Png, string text = null )
	{
		var content = new List<object> { new { type = "image", data = base64Png, mimeType = "image/png" } };
		if ( !string.IsNullOrEmpty( text ) )
			content.Add( new { type = "text", text } );

		return new { content = content.ToArray(), isError = false };
	}
}

public static class McpVersion
{
	/// <summary>
	/// Protocol revisions this server understands. 2025-06-18 only: older
	/// revisions REQUIRE JSON-RPC batch support, which this server does not
	/// implement, so advertising them would be a lie.
	/// </summary>
	public static readonly string[] Supported = { "2025-06-18" };

	/// <summary>Exact match wins; anything else gets our newest revision.</summary>
	public static string Negotiate( string clientRequested ) =>
		Supported.Contains( clientRequested ) ? clientRequested : Supported[0];
}
