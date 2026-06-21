using System;
using Editor;
using Sandbox;
using SboxMcp.Integration;

namespace SboxMcp.UI;

/// <summary>
/// Port, autostart, permission mode and maintenance actions.
/// </summary>
public class SettingsPage : Widget
{
	readonly LineEdit _port;

	public SettingsPage( Widget parent ) : base( parent )
	{
		Layout = Layout.Column();
		Layout.Margin = 8;
		Layout.Spacing = 8;

		// ---- permission mode ----------------------------------------------
		var permGroup = AddGroup( "Permissions", "shield" );

		var hint = permGroup.Layout.Add( new Label( "Controls what connected AI clients may do. Write tools modify your project.", permGroup ) );
		hint.SetStyles( $"color: {Theme.TextLight.Hex}; font-size: 11px;" );
		hint.WordWrap = true;

		var modeRow = permGroup.Layout.AddRow();
		modeRow.Spacing = 6;

		var modeCombo = modeRow.Add( new ComboBox( permGroup ) { MinimumWidth = 180 } );
		modeCombo.AddItem( "Full access", "bolt", () => McpSettings.Mode = PermissionMode.FullAccess,
			selected: McpSettings.Mode == PermissionMode.FullAccess );
		modeCombo.AddItem( "Approve writes", "how_to_reg", () => McpSettings.Mode = PermissionMode.ApproveWrites,
			selected: McpSettings.Mode == PermissionMode.ApproveWrites );
		modeCombo.AddItem( "Read-only", "visibility", () => McpSettings.Mode = PermissionMode.ReadOnly,
			selected: McpSettings.Mode == PermissionMode.ReadOnly );
		modeRow.AddStretchCell();

		// ---- server settings ------------------------------------------------
		var serverGroup = AddGroup( "Server", "settings_ethernet" );

		var portRow = serverGroup.Layout.AddRow();
		portRow.Spacing = 6;

		var portLabel = portRow.Add( new Label( "Port", serverGroup ) );
		portLabel.SetStyles( $"color: {Theme.TextLight.Hex}; font-size: 11px;" );

		_port = portRow.Add( new LineEdit( serverGroup ) { Text = McpSettings.Port.ToString() } );
		_port.FixedWidth = 80;

		var apply = portRow.Add( new Button.Primary( "Apply & restart" ) { Icon = "save" } );
		apply.FixedWidth = 136; // auto-sizing clips the label slightly
		apply.Clicked = ApplyPort;
		portRow.AddStretchCell();

		var autoStart = serverGroup.Layout.Add( new Checkbox( "Start server when the editor opens" ) { Value = McpSettings.AutoStart } );
		autoStart.Clicked = () => McpSettings.AutoStart = autoStart.Value;

		// ---- maintenance ---------------------------------------------------
		var maintGroup = AddGroup( "Maintenance", "cleaning_services" );

		var maintRow = maintGroup.Layout.AddRow();
		maintRow.Spacing = 6;

		var clearActivity = maintRow.Add( new Button( "Clear activity", "delete_sweep" ) );
		clearActivity.Clicked = ActivityLog.Clear;

		var clearLogs = maintRow.Add( new Button( "Clear logs", "playlist_remove" ) );
		clearLogs.Clicked = LogCapture.Clear;

		maintRow.AddStretchCell();

		Layout.AddStretchCell();
	}

	GroupBox AddGroup( string title, string icon )
	{
		return Layout.Add( new GroupBox( this ) { Title = title, Icon = icon } );
	}

	void ApplyPort()
	{
		if ( !int.TryParse( _port.Text, out var port ) || port is < 1024 or > 65535 )
		{
			_port.Text = McpSettings.Port.ToString();
			return;
		}

		McpSettings.Port = port;
		McpHost.Restart();
	}
}
