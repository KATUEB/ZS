using System;
using System.Linq;
using Editor;
using Sandbox;
using SboxMcp.Integration;

namespace SboxMcp.UI;

/// <summary>
/// Live feed of tool calls, with pending approval cards pinned on top.
/// </summary>
public class ActivityPage : Widget
{
	string _signature;
	ScrollArea _scroll;

	public ActivityPage( Widget parent ) : base( parent )
	{
		Layout = Layout.Column();

		_scroll = new ScrollArea( this );
		_scroll.Canvas = new Widget( _scroll );
		_scroll.Canvas.Layout = Layout.Column();
		_scroll.Canvas.Layout.Margin = 8;
		_scroll.Canvas.Layout.Spacing = 4;
		_scroll.Canvas.VerticalSizeMode = SizeMode.CanGrow;
		_scroll.Canvas.HorizontalSizeMode = SizeMode.Flexible;

		Layout.Add( _scroll, 1 );
		Rebuild();
	}

	public void Tick()
	{
		var sig = $"{ActivityLog.Version}|{PermissionGate.Version}";
		if ( sig == _signature )
			return;

		_signature = sig;
		Rebuild();
	}

	void Rebuild()
	{
		var canvas = _scroll.Canvas;
		canvas.Layout.Clear( true );

		// ---- pending approvals -------------------------------------------
		foreach ( var request in PermissionGate.Pending )
		{
			var card = canvas.Layout.Add( new Card( canvas ) { EdgeAccent = Theme.Yellow } );

			var title = card.Layout.Add( new Label( $"Approve  {request.ToolName} ?", card ) );
			title.SetStyles( $"color: {Theme.Yellow.Hex}; font-size: 12px; font-weight: 700;" );

			if ( !string.IsNullOrEmpty( request.ArgsSummary ) )
			{
				var args = card.Layout.Add( new Label( request.ArgsSummary, card ) );
				args.SetStyles( $"color: {Theme.TextLight.Hex}; font-size: 10px; font-family: Consolas;" );
				args.WordWrap = true;
			}

			var buttons = card.Layout.AddRow();
			buttons.Spacing = 6;

			var approve = buttons.Add( new Button.Primary( "Approve" ) { Icon = "check" } );
			approve.Clicked = request.Approve;

			var deny = buttons.Add( new Button( "Deny", "close" ) );
			deny.Clicked = request.Deny;

			buttons.AddStretchCell();
		}

		// ---- header row ---------------------------------------------------
		var header = canvas.Layout.AddRow();
		header.Spacing = 6;

		var count = header.Add( new Label( $"{ActivityLog.TotalCalls} total calls", canvas ) );
		count.SetStyles( $"color: {Theme.TextLight.Hex}; font-size: 11px;" );
		header.AddStretchCell();

		var revert = header.Add( new Button( "Revert", "undo" ) );
		revert.ToolTip = "Undo the AI's most recent editor action.\nOnly works while an MCP action is the newest entry on the undo stack - your own edits are never reverted.";
		revert.Clicked = RevertLastMcpAction;

		var copy = header.Add( new Button( "Copy", "content_copy" ) );
		copy.ToolTip = "Copy the activity feed as text";
		copy.Clicked = CopyTranscript;

		var clear = header.Add( new Button( "Clear", "delete_sweep" ) );
		clear.Clicked = ActivityLog.Clear;

		// ---- feed ----------------------------------------------------------
		var records = ActivityLog.Records;

		if ( records.Count == 0 )
		{
			var empty = canvas.Layout.Add( new Label( "No tool calls yet. Connect a client and watch the AI work in here.", canvas ) );
			empty.SetStyles( $"color: {Theme.TextLight.Hex}; font-size: 11px;" );
		}

		foreach ( var record in records.Take( 100 ) )
			canvas.Layout.Add( new ActivityRow( record, canvas ) );

		canvas.Layout.AddStretchCell();
	}

	/// <summary>
	/// Undoes the newest undo entry, but only when it is one of ours (every
	/// MCP write runs in an undo scope named "MCP: ...") - never the user's
	/// own edits.
	/// </summary>
	static void RevertLastMcpAction()
	{
		var session = SceneEditorSession.Active;
		if ( session is null )
			return;

		if ( session.UndoSystem.Back.TryPeek( out var entry )
			&& (entry.Name?.StartsWith( "MCP", StringComparison.OrdinalIgnoreCase ) ?? false) )
		{
			session.UndoSystem.Undo();
			McpHost.Log.Info( $"Reverted '{entry.Name}'" );
		}
		else
		{
			McpHost.Log.Info( "Nothing to revert - the newest undo entry is not an MCP action" );
		}
	}

	/// <summary>
	/// Reverts one specific MCP undo entry, wherever it sits in the undo
	/// stack. Newer edits to the same objects can override the result -
	/// that is inherent to out-of-order undo.
	/// </summary>
	internal static void RevertEntry( Sandbox.Helpers.UndoSystem.Entry entry )
	{
		var session = SceneEditorSession.Active;
		if ( session is null || entry is null )
			return;

		var back = session.UndoSystem.Back;
		if ( !back.Contains( entry ) )
		{
			McpHost.Log.Info( "That action is no longer on the undo stack" );
			return;
		}

		entry.Undo?.Invoke();

		// Stack<T> has no Remove - rebuild it without the reverted entry
		var remaining = back.Where( e => !ReferenceEquals( e, entry ) ).ToList();
		back.Clear();
		for ( var i = remaining.Count - 1; i >= 0; i-- )
			back.Push( remaining[i] );

		McpHost.Log.Info( $"Reverted '{entry.Name}'" );
	}

	static void CopyTranscript()
	{
		var sb = new System.Text.StringBuilder();

		// records are newest-first; export oldest-first for reading
		foreach ( var r in ActivityLog.Records.Reverse() )
		{
			sb.Append( r.Time.ToString( "HH:mm:ss" ) )
				.Append( r.Ok ? "  ok    " : "  ERROR " )
				.Append( r.Category ).Append( "  " )
				.Append( r.ToolName );

			if ( !string.IsNullOrEmpty( r.ArgsDigest ) )
				sb.Append( "  " ).Append( r.ArgsDigest );

			if ( r.Error is not null )
				sb.Append( "  -> " ).Append( r.Error );

			sb.Append( "  (" ).Append( r.DurationMs ).AppendLine( " ms)" );
		}

		EditorUtility.Clipboard.Copy( sb.Length > 0 ? sb.ToString() : "no activity yet" );
	}
}

/// <summary>
/// One feed row in the native list style: status icon, colored category chip,
/// tool name, args, duration.
/// </summary>
public class ActivityRow : Widget
{
	readonly ActivityRecord _record;

	public ActivityRow( ActivityRecord record, Widget parent ) : base( parent )
	{
		_record = record;
		FixedHeight = 30;
		ToolTip = record.Error ?? record.ArgsDigest;
	}

	protected override void OnContextMenu( ContextMenuEvent e )
	{
		var menu = new Menu( this );

		var revertable = _record.UndoEntry is not null
			&& (SceneEditorSession.Active?.UndoSystem.Back.Contains( _record.UndoEntry ) ?? false);

		var revert = menu.AddOption( "Revert this action", "undo",
			() => ActivityPage.RevertEntry( _record.UndoEntry ) );
		revert.Enabled = revertable;
		revert.StatusTip = revertable
			? "Undo exactly what this call changed (newer edits to the same objects may override the result)"
			: "Nothing to revert - this call made no undoable change, or it was already undone";

		menu.AddOption( "Copy row", "content_copy", () => EditorUtility.Clipboard.Copy(
			$"{_record.Time:HH:mm:ss} {(_record.Ok ? "ok" : "ERROR")} {_record.Category} {_record.ToolName} {_record.ArgsDigest} {_record.Error} ({_record.DurationMs} ms)" ) );

		menu.OpenAtCursor();
		e.Accepted = true;
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		Paint.ClearPen();

		var categoryColor = Palette.For( _record.Category );

		Paint.SetBrush( Paint.HasMouseOver ? Theme.ControlBackground.Lighten( 0.3f ) : Theme.ControlBackground );
		Paint.DrawRect( LocalRect, 4 );

		// status icon
		Paint.SetPen( _record.Ok ? Theme.Green : Theme.Red );
		Paint.DrawIcon( new Rect( LocalRect.Left + 6, LocalRect.Top, 18, LocalRect.Height ), _record.Ok ? "check_circle" : "error", 14, TextFlag.Center );

		// category chip (stays colorful)
		var chipText = _record.Category.ToString();
		Paint.SetDefaultFont( 7, 600 );
		var chipWidth = Paint.MeasureText( chipText ).x + 12;
		var chip = new Rect( LocalRect.Left + 28, LocalRect.Center.y - 8, chipWidth, 16 );
		Paint.ClearPen();
		Paint.SetBrush( categoryColor.WithAlpha( 0.2f ) );
		Paint.DrawRect( chip, 8 );
		Paint.SetPen( categoryColor );
		Paint.DrawText( chip, chipText, TextFlag.Center );

		// tool name
		var x = chip.Right + 8;
		Paint.SetPen( Palette.TextBright );
		Paint.SetDefaultFont( 8, 600 );
		var nameWidth = Paint.MeasureText( _record.ToolName ).x;
		Paint.DrawText( new Rect( x, LocalRect.Top, nameWidth + 4, LocalRect.Height ), _record.ToolName, TextFlag.LeftCenter );

		// args digest
		x += nameWidth + 12;
		var right = LocalRect.Right - 86;
		if ( right > x )
		{
			Paint.SetPen( Palette.TextDim );
			Paint.SetFont( "Consolas", 7 );
			var digest = _record.Error ?? _record.ArgsDigest;
			Paint.DrawText( new Rect( x, LocalRect.Top, right - x, LocalRect.Height ), digest ?? "", TextFlag.LeftCenter | TextFlag.SingleLine );
		}

		// duration + time
		Paint.SetPen( Palette.TextDim );
		Paint.SetDefaultFont( 7 );
		Paint.DrawText( new Rect( LocalRect.Right - 84, LocalRect.Top, 80, LocalRect.Height ),
			$"{_record.DurationMs} ms  {_record.Time:HH:mm:ss}", TextFlag.RightCenter );
	}
}
