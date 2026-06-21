using System;

namespace SboxMcp.Server;

/// <summary>
/// One connected MCP client, identified by the Mcp-Session-Id header.
/// </summary>
public sealed class McpSession
{
	int _callCount;
	long _lastSeenTicks = DateTime.Now.Ticks;

	public string Id { get; } = Guid.NewGuid().ToString( "N" );
	public string ClientName { get; internal set; } = "unknown";
	public DateTime ConnectedAt { get; } = DateTime.Now;
	public DateTime LastSeen => new( System.Threading.Interlocked.Read( ref _lastSeenTicks ) );
	public int CallCount => _callCount;

	internal void Touch()
	{
		System.Threading.Interlocked.Exchange( ref _lastSeenTicks, DateTime.Now.Ticks );
		System.Threading.Interlocked.Increment( ref _callCount );
	}
}
