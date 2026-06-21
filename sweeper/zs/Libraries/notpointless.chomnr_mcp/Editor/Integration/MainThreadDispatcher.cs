using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace SboxMcp.Integration;

/// <summary>
/// Marshals tool execution from HttpListener worker threads onto the editor
/// main thread. McpHost drains the queue every editor frame.
/// </summary>
public static class MainThreadDispatcher
{
	static readonly ConcurrentQueue<Action> Queue = new();

	public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds( 30 );

	public static Task<object> Run( Func<object> work, TimeSpan? timeout = null )
	{
		var tcs = new TaskCompletionSource<object>( TaskCreationOptions.RunContinuationsAsynchronously );

		Queue.Enqueue( () =>
		{
			// the timeout may already have failed this task - never run stale
			// work, or a client that retried gets the side effect twice
			if ( tcs.Task.IsCompleted )
				return;

			try
			{
				tcs.TrySetResult( work() );
			}
			catch ( Exception e )
			{
				tcs.TrySetException( e );
			}
		} );

		var limit = timeout ?? DefaultTimeout;
		_ = Task.Delay( limit ).ContinueWith( _ =>
			tcs.TrySetException( new TimeoutException(
				$"Tool did not complete within {limit.TotalSeconds:0}s - the editor may be busy or blocked" ) ) );

		return tcs.Task;
	}

	/// <summary>Runs queued work. Must be called from the editor main thread.</summary>
	internal static void Pump()
	{
		while ( Queue.TryDequeue( out var action ) )
			action();
	}
}
