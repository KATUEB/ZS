using System.Text.Json;
using SboxMcp.Server;
using Xunit;

namespace SboxMcp.Tests;

public class ProtocolTests
{
	[Fact]
	public void Parse_request_with_id_and_params()
	{
		var req = JsonRpcRequest.Parse( """{"jsonrpc":"2.0","id":7,"method":"tools/call","params":{"name":"x"}}""" );

		Assert.False( req.IsNotification );
		Assert.Equal( 7, req.Id.Value.GetInt32() );
		Assert.Equal( "tools/call", req.Method );
		Assert.Equal( "x", req.Params.Value.GetProperty( "name" ).GetString() );
	}

	[Fact]
	public void Parse_notification_has_no_id()
	{
		var req = JsonRpcRequest.Parse( """{"jsonrpc":"2.0","method":"notifications/initialized"}""" );

		Assert.True( req.IsNotification );
		Assert.Equal( "notifications/initialized", req.Method );
	}

	[Fact]
	public void Parse_invalid_json_throws()
	{
		Assert.Throws<JsonRpcParseException>( () => JsonRpcRequest.Parse( "{nope" ) );
	}

	[Fact]
	public void Parse_missing_method_throws()
	{
		Assert.Throws<JsonRpcParseException>( () => JsonRpcRequest.Parse( """{"jsonrpc":"2.0","id":1}""" ) );
	}

	[Fact]
	public void Writer_result_emits_envelope()
	{
		var id = JsonDocument.Parse( "3" ).RootElement;
		var json = JsonRpcWriter.Result( id, new { protocolVersion = "2025-06-18" } );
		var doc = JsonDocument.Parse( json ).RootElement;

		Assert.Equal( "2.0", doc.GetProperty( "jsonrpc" ).GetString() );
		Assert.Equal( 3, doc.GetProperty( "id" ).GetInt32() );
		Assert.Equal( "2025-06-18", doc.GetProperty( "result" ).GetProperty( "protocolVersion" ).GetString() );
	}

	[Fact]
	public void Writer_error_emits_code_and_message()
	{
		var json = JsonRpcWriter.Error( null, JsonRpcError.MethodNotFound, "no such method" );
		var doc = JsonDocument.Parse( json ).RootElement;

		Assert.Equal( JsonValueKind.Null, JsonKind( doc, "id" ) );
		Assert.Equal( -32601, doc.GetProperty( "error" ).GetProperty( "code" ).GetInt32() );
		Assert.Equal( "no such method", doc.GetProperty( "error" ).GetProperty( "message" ).GetString() );
	}

	[Fact]
	public void Records_serialize_camel_case()
	{
		var schema = JsonDocument.Parse( """{"type":"object"}""" ).RootElement;
		var json = JsonRpcWriter.Result( null,
			McpResults.ToolsList( new[] { new McpToolDescriptor( "a_tool", "does things", schema ) } ) );
		var doc = JsonDocument.Parse( json ).RootElement;
		var tool = doc.GetProperty( "result" ).GetProperty( "tools" )[0];

		Assert.Equal( "a_tool", tool.GetProperty( "name" ).GetString() );
		Assert.Equal( "does things", tool.GetProperty( "description" ).GetString() );
		Assert.Equal( "object", tool.GetProperty( "inputSchema" ).GetProperty( "type" ).GetString() );
	}

	[Fact]
	public void Version_negotiation()
	{
		// only 2025-06-18 is supported (older revisions require JSON-RPC batching)
		Assert.Equal( "2025-06-18", McpVersion.Negotiate( "2025-06-18" ) );
		Assert.Equal( "2025-06-18", McpVersion.Negotiate( "2025-03-26" ) );
		Assert.Equal( "2025-06-18", McpVersion.Negotiate( null ) );
	}

	[Fact]
	public void Null_id_is_rejected()
	{
		Assert.Throws<JsonRpcParseException>( () =>
			JsonRpcRequest.Parse( """{"jsonrpc":"2.0","id":null,"method":"ping"}""" ) );
	}

	[Fact]
	public void Text_content_shape()
	{
		var json = JsonRpcWriter.Result( null, McpResults.TextContent( "hello", isError: true ) );
		var result = JsonDocument.Parse( json ).RootElement.GetProperty( "result" );

		Assert.Equal( "text", result.GetProperty( "content" )[0].GetProperty( "type" ).GetString() );
		Assert.Equal( "hello", result.GetProperty( "content" )[0].GetProperty( "text" ).GetString() );
		Assert.True( result.GetProperty( "isError" ).GetBoolean() );
	}

	[Fact]
	public void Image_content_shape()
	{
		var json = JsonRpcWriter.Result( null, McpResults.ImageContent( "QUJD", "a screenshot" ) );
		var content = JsonDocument.Parse( json ).RootElement.GetProperty( "result" ).GetProperty( "content" );

		Assert.Equal( "image", content[0].GetProperty( "type" ).GetString() );
		Assert.Equal( "QUJD", content[0].GetProperty( "data" ).GetString() );
		Assert.Equal( "image/png", content[0].GetProperty( "mimeType" ).GetString() );
		Assert.Equal( "a screenshot", content[1].GetProperty( "text" ).GetString() );
	}

	static JsonValueKind JsonKind( JsonElement el, string prop ) =>
		el.TryGetProperty( prop, out var v ) ? v.ValueKind : JsonValueKind.Undefined;
}
