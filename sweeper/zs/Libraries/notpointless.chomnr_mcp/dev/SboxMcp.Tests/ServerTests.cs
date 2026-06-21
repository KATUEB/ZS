using System.Net;
using System.Text;
using System.Text.Json;
using SboxMcp.Registry;
using SboxMcp.Server;
using Xunit;

namespace SboxMcp.Tests;

public class ServerTests : IDisposable
{
	readonly McpServer _server;
	readonly HttpClient _client = new();
	readonly string _url;

	public ServerTests()
	{
		var registry = new ToolRegistry();
		registry.AddAssembly( typeof( SampleTools ).Assembly );

		_server = new McpServer( registry, ( tool, args ) =>
		{
			if ( tool.Meta.Name == "sample_void" )
				throw new InvalidOperationException( "boom" );

			return Task.FromResult( tool.Invoke( args ) );
		} );

		var port = StartOnFreePort( _server );
		_url = $"http://127.0.0.1:{port}/sbox-mcp";
	}

	static int StartOnFreePort( McpServer server )
	{
		var rand = new Random();
		for ( var attempt = 0; ; attempt++ )
		{
			var port = rand.Next( 20000, 49000 );
			try
			{
				server.Start( port );
				return port;
			}
			catch ( HttpListenerException ) when ( attempt < 10 )
			{
			}
		}
	}

	public void Dispose()
	{
		_server.Dispose();
		_client.Dispose();
	}

	async Task<(HttpResponseMessage Response, JsonElement Body)> Post( string json, string sessionId = null )
	{
		var msg = new HttpRequestMessage( HttpMethod.Post, _url )
		{
			Content = new StringContent( json, Encoding.UTF8, "application/json" )
		};
		if ( sessionId is not null )
			msg.Headers.Add( "Mcp-Session-Id", sessionId );

		var response = await _client.SendAsync( msg );
		var text = await response.Content.ReadAsStringAsync();
		var body = string.IsNullOrEmpty( text ) ? default : JsonDocument.Parse( text ).RootElement;
		return (response, body);
	}

	[Fact]
	public async Task Initialize_negotiates_version_and_issues_session()
	{
		var (response, body) = await Post(
			"""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","clientInfo":{"name":"test-client","version":"1"}}}""" );

		Assert.Equal( HttpStatusCode.OK, response.StatusCode );
		Assert.True( response.Headers.Contains( "Mcp-Session-Id" ) );
		var result = body.GetProperty( "result" );
		Assert.Equal( "2025-06-18", result.GetProperty( "protocolVersion" ).GetString() );
		Assert.Equal( "sbox-mcp", result.GetProperty( "serverInfo" ).GetProperty( "name" ).GetString() );
		Assert.Single( _server.Sessions );
		Assert.Equal( "test-client", _server.Sessions.First().ClientName );
	}

	[Fact]
	public async Task Notification_returns_202()
	{
		var (response, _) = await Post( """{"jsonrpc":"2.0","method":"notifications/initialized"}""" );
		Assert.Equal( HttpStatusCode.Accepted, response.StatusCode );
	}

	[Fact]
	public async Task Ping_returns_empty_object()
	{
		var (_, body) = await Post( """{"jsonrpc":"2.0","id":2,"method":"ping"}""" );
		Assert.Equal( JsonValueKind.Object, body.GetProperty( "result" ).ValueKind );
	}

	[Fact]
	public async Task Tools_list_returns_descriptors()
	{
		var (_, body) = await Post( """{"jsonrpc":"2.0","id":3,"method":"tools/list"}""" );
		var tools = body.GetProperty( "result" ).GetProperty( "tools" );

		Assert.True( tools.GetArrayLength() >= 3 );
		var greet = tools.EnumerateArray().First( t => t.GetProperty( "name" ).GetString() == "sample_greet" );
		Assert.Equal( "object", greet.GetProperty( "inputSchema" ).GetProperty( "type" ).GetString() );
	}

	[Fact]
	public async Task Tools_call_returns_text_content()
	{
		var (_, body) = await Post(
			"""{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"sample_greet","arguments":{"name":"ana"}}}""" );

		var result = body.GetProperty( "result" );
		Assert.False( result.GetProperty( "isError" ).GetBoolean() );
		Assert.Equal( "hi ana;", result.GetProperty( "content" )[0].GetProperty( "text" ).GetString() );
	}

	[Fact]
	public async Task Tools_call_failure_is_isError_result_not_protocol_error()
	{
		var (_, body) = await Post(
			"""{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"sample_void","arguments":{}}}""" );

		var result = body.GetProperty( "result" );
		Assert.True( result.GetProperty( "isError" ).GetBoolean() );
		Assert.Contains( "boom", result.GetProperty( "content" )[0].GetProperty( "text" ).GetString() );
	}

	[Fact]
	public async Task Tools_call_unknown_tool_is_invalid_params()
	{
		var (_, body) = await Post(
			"""{"jsonrpc":"2.0","id":6,"method":"tools/call","params":{"name":"nope_nothing","arguments":{}}}""" );

		Assert.Equal( -32602, body.GetProperty( "error" ).GetProperty( "code" ).GetInt32() );
	}

	[Fact]
	public async Task Unknown_method_is_method_not_found()
	{
		var (_, body) = await Post( """{"jsonrpc":"2.0","id":7,"method":"resources/list"}""" );
		Assert.Equal( -32601, body.GetProperty( "error" ).GetProperty( "code" ).GetInt32() );
	}

	[Fact]
	public async Task Bad_json_is_parse_error()
	{
		var (response, body) = await Post( "{garbage" );
		Assert.Equal( HttpStatusCode.BadRequest, response.StatusCode );
		Assert.Equal( -32700, body.GetProperty( "error" ).GetProperty( "code" ).GetInt32() );
	}

	[Fact]
	public async Task Get_is_method_not_allowed()
	{
		var response = await _client.GetAsync( _url );
		Assert.Equal( HttpStatusCode.MethodNotAllowed, response.StatusCode );
	}

	[Fact]
	public async Task Unknown_path_is_404()
	{
		var response = await _client.PostAsync( _url.Replace( "/sbox-mcp", "/nope" ),
			new StringContent( "{}", Encoding.UTF8, "application/json" ) );
		Assert.Equal( HttpStatusCode.NotFound, response.StatusCode );
	}

	[Fact]
	public async Task Evil_origin_is_forbidden()
	{
		var msg = new HttpRequestMessage( HttpMethod.Post, _url )
		{
			Content = new StringContent( """{"jsonrpc":"2.0","id":1,"method":"ping"}""", Encoding.UTF8, "application/json" )
		};
		msg.Headers.Add( "Origin", "https://evil.example.com" );

		var response = await _client.SendAsync( msg );
		Assert.Equal( HttpStatusCode.Forbidden, response.StatusCode );
	}

	[Fact]
	public async Task Session_id_reuse_tracks_calls()
	{
		var (init, _) = await Post(
			"""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","clientInfo":{"name":"c","version":"1"}}}""" );
		var sessionId = init.Headers.GetValues( "Mcp-Session-Id" ).First();

		await Post( """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""", sessionId );
		await Post( """{"jsonrpc":"2.0","id":3,"method":"ping"}""", sessionId );

		var session = _server.Sessions.First( s => s.Id == sessionId );
		Assert.True( session.CallCount >= 3 );
	}

	[Fact]
	public async Task Delete_removes_session()
	{
		var (init, _) = await Post(
			"""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18"}}""" );
		var sessionId = init.Headers.GetValues( "Mcp-Session-Id" ).First();

		var msg = new HttpRequestMessage( HttpMethod.Delete, _url );
		msg.Headers.Add( "Mcp-Session-Id", sessionId );
		var response = await _client.SendAsync( msg );

		Assert.Equal( HttpStatusCode.OK, response.StatusCode );
		Assert.DoesNotContain( _server.Sessions, s => s.Id == sessionId );
	}

	[Fact]
	public void Stop_and_restart_same_port()
	{
		var port = _server.Port;
		_server.Stop();
		Assert.False( _server.IsRunning );

		_server.Start( port );
		Assert.True( _server.IsRunning );
		Assert.Equal( port, _server.Port );
	}

	[Fact]
	public async Task Raw_result_passes_through()
	{
		// sample_struct returns object -> formatted as JSON text content
		var (_, body) = await Post(
			"""{"jsonrpc":"2.0","id":8,"method":"tools/call","params":{"name":"sample_struct","arguments":{"scale":1,"enabled":false,"tags":[]}}}""" );

		var text = body.GetProperty( "result" ).GetProperty( "content" )[0].GetProperty( "text" ).GetString();
		Assert.Equal( 1, JsonDocument.Parse( text ).RootElement.GetProperty( "scale" ).GetSingle() );
	}
}
