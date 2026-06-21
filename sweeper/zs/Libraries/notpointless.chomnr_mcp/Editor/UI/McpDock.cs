using System;
using Editor;
using Sandbox;
using static Sandbox.Internal.GlobalToolsNamespace;
using SboxMcp.Integration;

namespace SboxMcp.UI;

/// <summary>
/// Top-level "MCP" menu in the editor menu bar (lands next to Help).
/// </summary>
public static class McpMenu
{
	[Menu( "Editor", "MCP/Open Dashboard", "hub" )]
	public static void OpenDashboard() => McpDock.Open();

	[Menu( "Editor", "MCP/Start Server", "play_arrow" )]
	public static void StartServer() => McpHost.Start();

	[Menu( "Editor", "MCP/Stop Server", "stop" )]
	public static void StopServer() => McpHost.Stop();
}

/// <summary>
/// The MCP dashboard: header with live status, tab bar, and the four pages.
/// Open it from the MCP menu in the menu bar.
/// </summary>
public class McpDock : Widget
{
	static McpDock _instance;

	/// <summary>The open dashboard instance, if any.</summary>
	public static McpDock Instance => _instance.IsValid() ? _instance : null;

	readonly HeaderBar _header;
	readonly TabButton[] _tabs;
	readonly Widget[] _pages;
	readonly OverviewPage _overview;
	readonly ActivityPage _activity;
	readonly ToolsPage _tools;

	int _active;
	readonly RealTimeSince _sinceCreated = 0;

	// Widget.MinimumWidth is a no-op for docks; Qt asks this instead
	protected override Vector2 MinimumSizeHint() => new( 360, 220 );

	protected override void OnResize()
	{
		base.OnResize();

		// remember the user's size for future sessions; the settle delay keeps
		// the initial open/layout resizes from clobbering the saved value
		if ( _sinceCreated > 1f && Width > 100 && Height > 100 )
			McpSettings.DockSize = Size;
	}

	/// <summary>Opens (or raises) the dashboard.</summary>
	public static McpDock Open()
	{
		var dock = Instance;

		if ( dock is null )
		{
			dock = new McpDock( EditorWindow );

			// restore the last size the user resized it to
			dock.Size = McpSettings.DockSize;

			// floating by default - the user can dock it wherever they like
			EditorWindow.DockManager.AddDock( null, dock, DockArea.Floating );
			dock.Size = McpSettings.DockSize;
		}

		EditorWindow.DockManager.RaiseDock( dock );
		return dock;
	}

	public McpDock( Widget parent ) : base( parent )
	{
		_instance ??= this;

		Name = "McpDock";
		WindowTitle = "MCP";
		SetWindowIcon( "hub" );

		Layout = Layout.Column();

		_header = Layout.Add( new HeaderBar( this ) );

		var tabRow = Layout.AddRow();
		tabRow.Margin = new Sandbox.UI.Margin( 8, 4, 8, 0 );
		tabRow.Spacing = 2;

		_tabs = new[]
		{
			new TabButton( "Overview", "dashboard", this ),
			new TabButton( "Activity", "bolt", this ),
			new TabButton( "Tools", "construction", this ),
			new TabButton( "Settings", "tune", this )
		};

		for ( var i = 0; i < _tabs.Length; i++ )
		{
			var index = i;
			_tabs[i].Clicked = () => SetActive( index );
			tabRow.Add( _tabs[i] );
		}

		tabRow.AddStretchCell();

		var content = Layout.Add( new Widget( this ), 1 );
		content.Layout = Layout.Column();

		_overview = new OverviewPage( content );
		_activity = new ActivityPage( content );
		_tools = new ToolsPage( content );
		var settings = new SettingsPage( content );

		_pages = new Widget[] { _overview, _activity, _tools, settings };

		foreach ( var page in _pages )
			content.Layout.Add( page, 1 );

		SetActive( 0 );
		// no EditorEvent.Register(this) - QObject already registers every
		// widget; doing it again would run Tick twice per frame
	}

	public override void OnDestroyed()
	{
		base.OnDestroyed();
		if ( _instance == this )
			_instance = null;
	}

	void SetActive( int index )
	{
		_active = index;

		for ( var i = 0; i < _pages.Length; i++ )
		{
			_pages[i].Visible = i == index;
			_tabs[i].Active = i == index;
			_tabs[i].Update();
		}
	}

	[EditorEvent.Frame]
	public void Tick()
	{
		if ( !IsValid )
			return;

		var server = McpHost.Server;
		var running = server?.IsRunning ?? false;
		var sessions = server?.Sessions.Count ?? 0;

		_header.StatusColor = running ? Palette.Running : (McpHost.LastError is null ? Palette.Stopped : Palette.Error);
		_header.StatusText = !running
			? (McpHost.LastError is null ? "stopped" : "error")
			: sessions > 0 ? $"running · {sessions} client{(sessions == 1 ? "" : "s")}" : "running";
		_header.Pulse = running ? (MathF.Sin( RealTime.Now * 3f ) + 1f) * 0.5f : 0f;
		_header.Update();

		// badge pending approvals on the Activity tab
		var pending = PermissionGate.Pending.Count;
		if ( _tabs[1].Badge != pending )
		{
			_tabs[1].Badge = pending;
			_tabs[1].Update();
		}

		_overview.Tick();
		_activity.Tick();
		_tools.Tick();
	}
}
