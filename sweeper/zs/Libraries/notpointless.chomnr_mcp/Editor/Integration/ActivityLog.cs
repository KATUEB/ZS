using System;
using System.Collections.Generic;
using System.Linq;
using SboxMcp.Registry;

namespace SboxMcp.Integration;

public sealed class ActivityRecord
{
	public DateTime Time { get; init; } = DateTime.Now;
	public string ToolName { get; init; }
	public ToolCategory Category { get; init; }
	public string ArgsDigest { get; init; }
	public bool Ok { get; init; }
	public string Error { get; init; }
	public long DurationMs { get; init; }

	/// <summary>The undo entry this call created, when it mutated the scene -
	/// lets the UI revert this specific action.</summary>
	public Sandbox.Helpers.UndoSystem.Entry UndoEntry { get; set; }
}

/// <summary>
/// Ring buffer of recent tool calls; the dock's activity feed renders it.
/// </summary>
public static class ActivityLog
{
	const int Capacity = 500;

	static readonly LinkedList<ActivityRecord> _records = new();

	/// <summary>
	/// Raised on whatever thread recorded the call - usually a threadpool
	/// thread. Do NOT touch editor UI from a subscriber; poll Version from
	/// the frame tick instead (that is what the dock does).
	/// </summary>
	public static event Action<ActivityRecord> Added;
	public static event Action Cleared;

	public static IReadOnlyList<ActivityRecord> Records
	{
		get { lock ( _records ) return _records.ToArray(); }
	}

	public static int TotalCalls { get; private set; }

	/// <summary>Bumped on every change; UI polls this from the frame tick.</summary>
	public static int Version { get; private set; }

	internal static void Record( ActivityRecord record )
	{
		lock ( _records )
		{
			_records.AddFirst( record );
			while ( _records.Count > Capacity )
				_records.RemoveLast();
			TotalCalls++;
			Version++;
		}

		Added?.Invoke( record );
	}

	public static void Clear()
	{
		lock ( _records )
		{
			_records.Clear();
			Version++;
		}

		Cleared?.Invoke();
	}
}
