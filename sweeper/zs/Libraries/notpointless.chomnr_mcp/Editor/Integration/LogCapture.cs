using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;

namespace SboxMcp.Integration;

public sealed class CapturedLog
{
	public DateTime Time { get; init; } = DateTime.Now;
	public string Level { get; init; }
	public string Logger { get; init; }
	public string Message { get; init; }
	public bool IsDiagnostic { get; init; }
}

/// <summary>
/// Subscribes to the engine log stream so tools can read recent console
/// output (including compile diagnostics, which the editor logs).
/// </summary>
public static class LogCapture
{
	const int Capacity = 2000;

	static readonly LinkedList<CapturedLog> _logs = new();
	static bool _hooked;

	public static void Start()
	{
		if ( _hooked )
			return;

		_hooked = true;
		Editor.EditorUtility.AddLogger( OnMessage );
	}

	public static void Stop()
	{
		if ( !_hooked )
			return;

		_hooked = false;
		Editor.EditorUtility.RemoveLogger( OnMessage );
	}

	static void OnMessage( LogEvent ev )
	{
		var entry = new CapturedLog
		{
			Level = ev.Level.ToString(),
			Logger = ev.Logger,
			Message = ev.Message,
			IsDiagnostic = ev.IsDiagnostic
		};

		lock ( _logs )
		{
			_logs.AddFirst( entry );
			while ( _logs.Count > Capacity )
				_logs.RemoveLast();
		}
	}

	public static IReadOnlyList<CapturedLog> Recent( int count, string minLevel = null, bool diagnosticsOnly = false )
	{
		var threshold = Rank( minLevel );

		lock ( _logs )
		{
			return _logs
				.Where( l => Rank( l.Level ) >= threshold )
				.Where( l => !diagnosticsOnly || l.IsDiagnostic )
				.Take( count )
				.ToArray();
		}
	}

	public static void Clear()
	{
		lock ( _logs ) _logs.Clear();
	}

	static int Rank( string level ) => level?.ToLowerInvariant() switch
	{
		"error" => 4,
		"warn" or "warning" => 3,
		"info" => 2,
		"debug" or "trace" => 1,
		_ => 0
	};
}
