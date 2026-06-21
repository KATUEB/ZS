using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using SboxMcp.Registry;

namespace SboxMcp.Integration;

public enum PermissionMode
{
	/// <summary>Every tool runs without asking.</summary>
	FullAccess,

	/// <summary>Write tools wait for approval in the MCP dock (60s timeout).</summary>
	ApproveWrites,

	/// <summary>Write tools are rejected outright.</summary>
	ReadOnly
}

/// <summary>
/// A pending approval shown in the dock's activity page.
/// </summary>
public sealed class ApprovalRequest
{
	public string ToolName { get; init; }
	public string ArgsSummary { get; init; }
	public DateTime Created { get; } = DateTime.Now;

	internal TaskCompletionSource<bool> Decision { get; } = new( TaskCreationOptions.RunContinuationsAsynchronously );

	public void Approve() => Decision.TrySetResult( true );
	public void Deny() => Decision.TrySetResult( false );
}

/// <summary>
/// Gates write tools behind the configured permission mode.
/// </summary>
public static class PermissionGate
{
	static readonly TimeSpan ApprovalTimeout = TimeSpan.FromSeconds( 60 );
	static readonly List<ApprovalRequest> _pending = new();

	/// <summary>Raised when the pending list changes, so the UI can refresh.</summary>
	public static event Action Changed;

	/// <summary>Bumped on every pending-list change; UI polls this from the frame tick.</summary>
	public static int Version { get; private set; }

	public static IReadOnlyList<ApprovalRequest> Pending
	{
		get { lock ( _pending ) return _pending.ToArray(); }
	}

	/// <summary>
	/// Returns whether the tool may run. Throws nothing; the caller turns a
	/// denial into an error the AI can read.
	/// </summary>
	public static async Task<bool> RequestAsync( RegisteredTool tool, JsonElement? args )
	{
		if ( !tool.Meta.Writes )
			return true;

		switch ( McpSettings.Mode )
		{
			case PermissionMode.FullAccess:
				return true;

			case PermissionMode.ReadOnly:
				return false;

			case PermissionMode.ApproveWrites:
			default:
			{
				var request = new ApprovalRequest
				{
					ToolName = tool.Meta.Name,
					ArgsSummary = Summarize( args )
				};

				lock ( _pending ) { _pending.Add( request ); Version++; }
				Changed?.Invoke();

				try
				{
					var decided = await Task.WhenAny( request.Decision.Task, Task.Delay( ApprovalTimeout ) );
					return decided == request.Decision.Task && request.Decision.Task.Result;
				}
				finally
				{
					lock ( _pending ) { _pending.Remove( request ); Version++; }
					Changed?.Invoke();
				}
			}
		}
	}

	internal static string Summarize( JsonElement? args )
	{
		if ( args is null )
			return "";

		var text = args.Value.GetRawText();
		return text.Length <= 120 ? text : text[..117] + "...";
	}
}
