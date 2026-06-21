using System;
using System.Text.Json;

namespace SboxMcp.Server;

/// <summary>
/// Thrown when an incoming message is not a valid JSON-RPC 2.0 request.
/// </summary>
public sealed class JsonRpcParseException : Exception
{
	public JsonRpcParseException( string message, Exception inner = null ) : base( message, inner ) { }
}

/// <summary>
/// A parsed JSON-RPC 2.0 request or notification.
/// </summary>
public sealed class JsonRpcRequest
{
	public JsonElement? Id { get; private set; }
	public string Method { get; private set; }
	public JsonElement? Params { get; private set; }

	public bool IsNotification => Id is null;

	public static JsonRpcRequest Parse( string json )
	{
		JsonElement root;
		try
		{
			using var doc = JsonDocument.Parse( json );
			root = doc.RootElement.Clone();
		}
		catch ( JsonException e )
		{
			throw new JsonRpcParseException( "Invalid JSON", e );
		}

		if ( root.ValueKind != JsonValueKind.Object )
			throw new JsonRpcParseException( "Request must be a JSON object" );

		if ( !root.TryGetProperty( "method", out var method ) || method.ValueKind != JsonValueKind.String )
			throw new JsonRpcParseException( "Request is missing a string 'method'" );

		var request = new JsonRpcRequest { Method = method.GetString() };

		if ( root.TryGetProperty( "id", out var id ) )
		{
			// JSON-RPC 2.0 forbids null ids; silently treating one as a
			// notification would hang the client waiting for a response
			if ( id.ValueKind == JsonValueKind.Null )
				throw new JsonRpcParseException( "'id' must not be null" );

			request.Id = id;
		}

		if ( root.TryGetProperty( "params", out var p ) )
			request.Params = p;

		return request;
	}
}

public static class JsonRpcError
{
	public const int ParseError = -32700;
	public const int InvalidRequest = -32600;
	public const int MethodNotFound = -32601;
	public const int InvalidParams = -32602;
	public const int InternalError = -32603;
}

/// <summary>
/// Serializes JSON-RPC 2.0 response envelopes.
/// </summary>
public static class JsonRpcWriter
{
	internal static readonly JsonSerializerOptions Options = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	public static string Result( JsonElement? id, object result )
	{
		return JsonSerializer.Serialize( new Envelope { Id = id, Result = result }, Options );
	}

	public static string Error( JsonElement? id, int code, string message )
	{
		return JsonSerializer.Serialize( new Envelope { Id = id, Error = new { code, message } }, Options );
	}

	sealed class Envelope
	{
		public string Jsonrpc { get; set; } = "2.0";
		public JsonElement? Id { get; set; }
		[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull )]
		public object Result { get; set; }
		[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull )]
		public object Error { get; set; }
	}
}
