using System;
using Editor;
using Sandbox;
using SboxMcp.Integration;

namespace SboxMcp.UI;

/// <summary>
/// Tiny MCP indicator in the editor's status bar: a status dot, the label and
/// the connected-client count. Click to open the dashboard.
/// </summary>
public class McpStatusPill : Widget
{
	string _signature;

	public McpStatusPill() : base( null )
	{
		FixedWidth = 70;
		FixedHeight = 20;
		Cursor = CursorShape.Finger;
		ToolTip = "s&box MCP - click to open the dashboard";
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		Paint.ClearPen();

		var server = McpHost.Server;
		var running = server?.IsRunning ?? false;
		var sessions = server?.Sessions.Count ?? 0;
		var color = running ? Palette.Running : (McpHost.LastError is null ? Palette.Stopped : Palette.Error);

		if ( Paint.HasMouseOver )
		{
			Paint.SetBrush( Color.White.WithAlpha( 0.06f ) );
			Paint.DrawRect( LocalRect, 4 );
		}

		Paint.SetBrush( color );
		Paint.DrawCircle( new Vector2( LocalRect.Left + 9, LocalRect.Center.y ), 7 );

		Paint.SetPen( Palette.TextDim );
		Paint.SetDefaultFont( 7, 600 );
		Paint.DrawText( new Rect( LocalRect.Left + 17, LocalRect.Top, LocalRect.Width - 19, LocalRect.Height ),
			running && sessions > 0 ? $"MCP · {sessions}" : "MCP", TextFlag.LeftCenter );
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		base.OnMouseClick( e );
		McpDock.Open();
	}

	// widgets are auto-registered for editor events; repaint when state changes
	[EditorEvent.Frame]
	public void Tick()
	{
		if ( !IsValid )
			return;

		var server = McpHost.Server;
		var sig = $"{server?.IsRunning}|{server?.Sessions.Count}|{McpHost.LastError is not null}";

		if ( sig == _signature )
			return;

		_signature = sig;
		Update();
	}
}
