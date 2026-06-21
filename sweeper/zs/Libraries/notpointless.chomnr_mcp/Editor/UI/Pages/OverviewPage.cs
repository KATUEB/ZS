using System;
using System.Linq;
using Editor;
using Sandbox;
using SboxMcp.Integration;

namespace SboxMcp.UI;

/// <summary>
/// Server status, start/stop, and copy-paste configs for every client.
/// </summary>
public class OverviewPage : Widget
{
	string _signature;
	ScrollArea _scroll;

	public OverviewPage( Widget parent ) : base( parent )
	{
		Layout = Layout.Column();

		_scroll = new ScrollArea( this );
		_scroll.Canvas = new Widget( _scroll );
		_scroll.Canvas.Layout = Layout.Column();
		_scroll.Canvas.Layout.Margin = 8;
		_scroll.Canvas.Layout.Spacing = 8;
		_scroll.Canvas.VerticalSizeMode = SizeMode.CanGrow;
		_scroll.Canvas.HorizontalSizeMode = SizeMode.Flexible;

		Layout.Add( _scroll, 1 );
		Rebuild();
	}

	public void Tick()
	{
		var server = McpHost.Server;
		var sig = $"{server?.IsRunning}|{server?.Port}|{server?.Sessions.Count}|{McpHost.LastError}|{string.Join( ",", server?.Sessions.Select( s => $"{s.ClientName}:{s.CallCount}" ) ?? Enumerable.Empty<string>() )}";

		if ( sig == _signature )
			return;

		_signature = sig;
		Rebuild();
	}

	GroupBox AddGroup( Widget canvas, string title, string icon )
	{
		return canvas.Layout.Add( new GroupBox( canvas ) { Title = title, Icon = icon } );
	}

	void Rebuild()
	{
		var canvas = _scroll.Canvas;
		canvas.Layout.Clear( true );

		var server = McpHost.Server;
		var url = server?.Url ?? $"http://127.0.0.1:{McpSettings.Port}/sbox-mcp";
		var running = server?.IsRunning ?? false;

		// ---- server group -------------------------------------------------
		var serverGroup = AddGroup( canvas, "Server", "dns" );

		serverGroup.Layout.Add( new CodeSnippet( url, running ? Palette.Running : Palette.Stopped, serverGroup ) );

		var controls = serverGroup.Layout.AddRow();
		controls.Spacing = 6;

		if ( running )
		{
			var stop = controls.Add( new Button.Primary( "Stop" ) { Icon = "stop", Tint = Theme.Red } );
			stop.Clicked = McpHost.Stop;

			var restart = controls.Add( new Button( "Restart", "refresh" ) );
			restart.Clicked = McpHost.Restart;
		}
		else
		{
			var start = controls.Add( new Button.Primary( "Start server" ) { Icon = "play_arrow" } );
			start.FixedWidth = 136; // match the settings page's Apply & restart
			start.Clicked = McpHost.Start;
		}

		controls.AddStretchCell();

		if ( McpHost.LastError is not null )
		{
			var error = serverGroup.Layout.Add( new Label( McpHost.LastError, serverGroup ) );
			error.SetStyles( $"color: {Theme.Red.Hex}; font-size: 11px;" );
			error.WordWrap = true;

			var tryPort = serverGroup.Layout.Add( new Button( $"Try port {McpSettings.Port + 1}", "swap_horiz" ) );
			tryPort.Clicked = () =>
			{
				McpSettings.Port += 1;
				McpHost.Restart();
			};
		}

		// ---- sessions ----------------------------------------------------
		var sessions = server?.Sessions;
		if ( running )
		{
			var label = sessions is { Count: > 0 }
				? $"{sessions.Count} connected client{(sessions.Count == 1 ? "" : "s")}"
				: "No clients connected yet - copy a config below into your AI tool";

			var l = serverGroup.Layout.Add( new Label( label, serverGroup ) );
			l.SetStyles( $"color: {Theme.TextLight.Hex}; font-size: 11px;" );

			if ( sessions is not null )
			{
				foreach ( var s in sessions )
				{
					var row = serverGroup.Layout.Add( new Label( $"●  {s.ClientName}   ·   {s.CallCount} calls   ·   last seen {s.LastSeen:HH:mm:ss}", serverGroup ) );
					row.SetStyles( $"color: {Theme.Green.Hex}; font-size: 11px;" );
				}
			}
		}

		// ---- client config groups ------------------------------------------
		AddConfigGroup( canvas, "Claude Code", Palette.ClaudeAccent,
			$"# terminal\nclaude mcp add --transport http sbox {url}\n\n# or .mcp.json\n{{\n  \"mcpServers\": {{\n    \"sbox\": {{ \"type\": \"http\", \"url\": \"{url}\" }}\n  }}\n}}" );

		AddConfigGroup( canvas, "Claude Desktop", Palette.ClaudeAccent,
			$"// claude_desktop_config.json (needs Node.js for npx)\n{{\n  \"mcpServers\": {{\n    \"sbox\": {{\n      \"command\": \"npx\",\n      \"args\": [\"-y\", \"mcp-remote\", \"{url}\"]\n    }}\n  }}\n}}" );

		AddConfigGroup( canvas, "Cursor", Palette.CursorAccent,
			$"// .cursor/mcp.json\n{{\n  \"mcpServers\": {{\n    \"sbox\": {{ \"url\": \"{url}\" }}\n  }}\n}}" );

		AddConfigGroup( canvas, "VS Code", Palette.VsCodeAccent,
			$"// .vscode/mcp.json\n{{\n  \"servers\": {{\n    \"sbox\": {{ \"type\": \"http\", \"url\": \"{url}\" }}\n  }}\n}}" );

		canvas.Layout.AddStretchCell();
	}

	void AddConfigGroup( Widget canvas, string client, Color accent, string snippet )
	{
		var group = AddGroup( canvas, client, "integration_instructions" );
		group.Layout.Add( new CodeSnippet( snippet, accent, group ) );
	}
}
