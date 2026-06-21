using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using SboxMcp.Registry;

namespace SboxMcp.Server;

/// <summary>
/// Wrap a tool's return value in this to send a pre-built MCP result payload
/// (e.g. image content) instead of formatted text.
/// </summary>
public sealed record RawMcpResult( object Payload );

/// <summary>
/// MCP server speaking Streamable HTTP (single JSON response mode) on
/// 127.0.0.1. Tool execution is delegated to an injected invoker so the
/// hosting layer controls threading and permissions.
/// </summary>
public sealed class McpServer : IDisposable
{
	public const string EndpointPath = "/sbox-mcp";

	readonly ToolRegistry _registry;
	readonly Func<RegisteredTool, JsonElement?, Task<object>> _invoker;
	readonly ConcurrentDictionary<string, McpSession> _sessions = new();

	HttpListener _listener;

	public int Port { get; private set; }
	public bool IsRunning => _listener?.IsListening ?? false;
	public IReadOnlyCollection<McpSession> Sessions => _sessions.Values.ToArray();
	public string Url => $"http://127.0.0.1:{Port}{EndpointPath}";

	/// <summary>Raised on start/stop and whenever the session list changes.</summary>
	public event Action StateChanged;

	public McpServer( ToolRegistry registry, Func<RegisteredTool, JsonElement?, Task<object>> invoker )
	{
		_registry = registry;
		_invoker = invoker;
	}

	public void Start( int port )
	{
		if ( IsRunning )
			Stop();

		var listener = new HttpListener();
		listener.Prefixes.Add( $"http://127.0.0.1:{port}/" );
		listener.Start(); // throws HttpListenerException if the port is taken

		_listener = listener;
		Port = port;
		_ = AcceptLoop( listener );
		StateChanged?.Invoke();
	}

	public void Stop()
	{
		var listener = _listener;
		_listener = null;

		if ( listener is not null )
		{
			try { listener.Close(); }
			catch ( ObjectDisposedException ) { }
		}

		_sessions.Clear();
		StateChanged?.Invoke();
	}

	public void Dispose() => Stop();

	async Task AcceptLoop( HttpListener listener )
	{
		while ( listener.IsListening )
		{
			HttpListenerContext context;
			try
			{
				context = await listener.GetContextAsync();
			}
			catch ( Exception e ) when ( e is HttpListenerException or ObjectDisposedException or InvalidOperationException )
			{
				return; // listener stopped
			}

			_ = Task.Run( () => HandleRequest( context ) );
		}
	}

	async Task HandleRequest( HttpListenerContext context )
	{
		try
		{
			await Route( context );
		}
		catch ( Exception e )
		{
			try
			{
				await WriteJson( context.Response, 500,
					JsonRpcWriter.Error( null, JsonRpcError.InternalError, e.Message ) );
			}
			catch
			{
				// response already gone; nothing useful to do
			}
		}
	}

	async Task Route( HttpListenerContext context )
	{
		var request = context.Request;
		var response = context.Response;

		if ( !IsAllowedOrigin( request.Headers["Origin"] ) )
		{
			await WriteJson( response, 403, JsonRpcWriter.Error( null, JsonRpcError.InvalidRequest, "Forbidden origin" ) );
			return;
		}

		if ( !string.Equals( request.Url?.AbsolutePath, EndpointPath, StringComparison.Ordinal ) )
		{
			await WriteJson( response, 404, JsonRpcWriter.Error( null, JsonRpcError.InvalidRequest, "Unknown path" ) );
			return;
		}

		switch ( request.HttpMethod )
		{
			case "POST":
				await HandlePost( context );
				break;

			case "DELETE":
				var sessionId = request.Headers["Mcp-Session-Id"];
				if ( sessionId is not null && _sessions.TryRemove( sessionId, out _ ) )
					StateChanged?.Invoke();
				response.StatusCode = 200;
				response.Close();
				break;

			default:
				response.AddHeader( "Allow", "POST, DELETE" );
				await WriteJson( response, 405, JsonRpcWriter.Error( null, JsonRpcError.InvalidRequest, "Use POST" ) );
				break;
		}
	}

	static bool IsAllowedOrigin( string origin )
	{
		if ( string.IsNullOrEmpty( origin ) || origin == "null" )
			return true;

		return Uri.TryCreate( origin, UriKind.Absolute, out var uri )
			&& (uri.IsLoopback || uri.Host == "localhost");
	}

	async Task HandlePost( HttpListenerContext context )
	{
		string body;
		using ( var reader = new StreamReader( context.Request.InputStream, Encoding.UTF8 ) )
			body = await reader.ReadToEndAsync();

		JsonRpcRequest rpc;
		try
		{
			rpc = JsonRpcRequest.Parse( body );
		}
		catch ( JsonRpcParseException e )
		{
			await WriteJson( context.Response, 400, JsonRpcWriter.Error( null, JsonRpcError.ParseError, e.Message ) );
			return;
		}

		TouchSession( context.Request.Headers["Mcp-Session-Id"] );

		if ( rpc.IsNotification )
		{
			context.Response.StatusCode = 202;
			context.Response.Close();
			return;
		}

		var (status, json, newSessionId) = await Dispatch( rpc );

		if ( newSessionId is not null )
			context.Response.AddHeader( "Mcp-Session-Id", newSessionId );

		await WriteJson( context.Response, status, json );
	}

	void TouchSession( string sessionId )
	{
		if ( sessionId is not null && _sessions.TryGetValue( sessionId, out var session ) )
			session.Touch();
	}

	/// <summary>
	/// Clients rarely send DELETE; drop sessions idle for over 30 minutes so
	/// the list doesn't grow forever in a long editor session.
	/// </summary>
	void PruneStaleSessions()
	{
		var cutoff = DateTime.Now - TimeSpan.FromMinutes( 30 );

		foreach ( var stale in _sessions.Values.Where( s => s.LastSeen < cutoff ).ToArray() )
			_sessions.TryRemove( stale.Id, out _ );
	}

	async Task<(int Status, string Json, string NewSessionId)> Dispatch( JsonRpcRequest rpc )
	{
		switch ( rpc.Method )
		{
			case "initialize":
			{
				var requested = rpc.Params is { ValueKind: JsonValueKind.Object } p
					&& p.TryGetProperty( "protocolVersion", out var v ) ? v.GetString() : null;

				var session = new McpSession();
				if ( rpc.Params is { ValueKind: JsonValueKind.Object } pi
					&& pi.TryGetProperty( "clientInfo", out var ci )
					&& ci.ValueKind == JsonValueKind.Object
					&& ci.TryGetProperty( "name", out var name )
					&& name.ValueKind == JsonValueKind.String )
				{
					session.ClientName = name.GetString();
				}

				session.Touch();
				_sessions[session.Id] = session;
				PruneStaleSessions();
				StateChanged?.Invoke();

				return (200, JsonRpcWriter.Result( rpc.Id, McpResults.Initialize( McpVersion.Negotiate( requested ) ) ), session.Id);
			}

			case "ping":
				return (200, JsonRpcWriter.Result( rpc.Id, new { } ), null);

			case "tools/list":
				return (200, JsonRpcWriter.Result( rpc.Id,
					McpResults.ToolsList( _registry.Tools.Where( t => t.IsAvailable ).Select( t => t.Descriptor ) ) ), null);

			case "tools/call":
				return (200, await CallTool( rpc ), null);

			default:
				return (200, JsonRpcWriter.Error( rpc.Id, JsonRpcError.MethodNotFound, $"Method '{rpc.Method}' is not supported" ), null);
		}
	}

	async Task<string> CallTool( JsonRpcRequest rpc )
	{
		if ( rpc.Params is not { ValueKind: JsonValueKind.Object } p
			|| !p.TryGetProperty( "name", out var nameEl ) || nameEl.ValueKind != JsonValueKind.String )
		{
			return JsonRpcWriter.Error( rpc.Id, JsonRpcError.InvalidParams, "tools/call requires params.name" );
		}

		var name = nameEl.GetString();
		var tool = _registry.Find( name );
		if ( tool is null )
			return JsonRpcWriter.Error( rpc.Id, JsonRpcError.InvalidParams, $"Unknown tool '{name}'" );

		if ( tool.UnavailableReason is string reason )
		{
			var detail = tool.Meta.Requires is null ? reason : $"{reason} (requires '{tool.Meta.Requires}')";
			return JsonRpcWriter.Error( rpc.Id, JsonRpcError.InvalidParams, $"'{name}' is unavailable: {detail}" );
		}

		JsonElement? args = p.TryGetProperty( "arguments", out var a ) && a.ValueKind == JsonValueKind.Object
			? a : null;

		try
		{
			var result = await _invoker( tool, args );

			return result is RawMcpResult raw
				? JsonRpcWriter.Result( rpc.Id, raw.Payload )
				: JsonRpcWriter.Result( rpc.Id, McpResults.TextContent( ToolRegistry.FormatResult( result ) ) );
		}
		catch ( Exception e )
		{
			return JsonRpcWriter.Result( rpc.Id, McpResults.TextContent( e.Message, isError: true ) );
		}
	}

	static async Task WriteJson( HttpListenerResponse response, int status, string json )
	{
		var bytes = Encoding.UTF8.GetBytes( json );
		response.StatusCode = status;
		response.ContentType = "application/json";
		response.ContentLength64 = bytes.Length;
		await response.OutputStream.WriteAsync( bytes );
		response.Close();
	}
}
