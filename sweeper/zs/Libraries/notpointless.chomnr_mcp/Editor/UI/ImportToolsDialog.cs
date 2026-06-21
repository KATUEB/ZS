using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Editor;
using Sandbox;
using SboxMcp.Integration;

namespace SboxMcp.UI;

/// <summary>
/// Pick public static methods from installed libraries (and other loaded
/// code) to expose as MCP tools. Searchable; libraries are listed separately
/// from everything else. Choices apply immediately and persist.
/// </summary>
public class ImportToolsDialog : Dialog
{
	readonly LineEdit _search;
	readonly ScrollArea _scroll;

	public ImportToolsDialog( Widget parent ) : base( parent )
	{
		Window.WindowTitle = "Import Tools From Library";
		Window.SetWindowIcon( "library_add" );
		Window.SetModal( true, true );
		Window.MinimumWidth = 560;
		Window.MinimumHeight = 480;

		Layout = Layout.Column();
		Layout.Margin = 16;
		Layout.Spacing = 8;

		var hint = Layout.Add( new Label(
			"Expose public static methods from installed libraries as MCP tools. "
			+ "Imported tools persist, re-bind every session, and are write-gated by approvals.", this ) );
		hint.SetStyles( $"color: {Theme.TextLight.Hex}; font-size: 11px;" );
		hint.WordWrap = true;

		_search = Layout.Add( new LineEdit( this ) { PlaceholderText = "Search methods, types or libraries..." } );
		_search.TextEdited += _ => Rebuild();

		_scroll = new ScrollArea( this );
		_scroll.Canvas = new Widget( _scroll );
		_scroll.Canvas.Layout = Layout.Column();
		_scroll.Canvas.Layout.Spacing = 2;
		_scroll.Canvas.Layout.Margin = 4;
		_scroll.Canvas.VerticalSizeMode = SizeMode.CanGrow;
		_scroll.Canvas.HorizontalSizeMode = SizeMode.Flexible;
		Layout.Add( _scroll, 1 );

		var buttons = Layout.AddRow();
		buttons.AddStretchCell();
		var done = buttons.Add( new Button.Primary( "Done" ) { Icon = "check" } );
		done.Clicked = Close; // Dialog.Close closes the host window (Destroy leaves it black)

		Rebuild();
	}

	void Rebuild()
	{
		var canvas = _scroll.Canvas;
		canvas.Layout.Clear( true );

		var query = _search.Text;
		var candidates = ToolImporter.CandidateAssemblies().ToList();

		AddSection( canvas, "Libraries", "extension",
			candidates.Where( ToolImporter.IsLibraryAssembly ).ToList(), query );

		AddSection( canvas, "Project & Other", "folder",
			candidates.Where( a => !ToolImporter.IsLibraryAssembly( a ) ).ToList(), query );

		canvas.Layout.AddStretchCell();
	}

	void AddSection( Widget canvas, string title, string icon, List<Assembly> assemblies, string query )
	{
		var header = canvas.Layout.Add( new Label( title, canvas ) );
		header.SetStyles( $"color: {Theme.Blue.Hex}; font-size: 12px; font-weight: 700; margin-top: 8px;" );

		var any = false;

		foreach ( var assembly in assemblies )
		{
			var methods = ToolImporter.CandidateMethods( assembly )
				.Where( m => Matches( assembly, m, query ) )
				.Take( 60 )
				.ToList();

			if ( methods.Count == 0 )
				continue;

			any = true;

			var name = canvas.Layout.Add( new Label( ToolImporter.FriendlyName( assembly ), canvas ) );
			name.SetStyles( $"color: {Theme.Text.Hex}; font-size: 11px; font-weight: 600; margin-top: 4px; margin-left: 6px;" );

			foreach ( var method in methods )
			{
				var parameters = string.Join( ", ", method.GetParameters().Select( p => p.Name ) );
				var check = canvas.Layout.Add( new Checkbox( $"{method.DeclaringType?.Name}.{method.Name}({parameters})", canvas )
				{
					Value = ToolImporter.IsImported( method )
				} );
				check.ToolTip = method.DeclaringType?.FullName;

				var captured = method;
				check.Clicked = () =>
				{
					if ( check.Value )
						ToolImporter.Import( captured );
					else
						ToolImporter.Unimport( captured );
				};
			}
		}

		if ( !any )
		{
			var empty = canvas.Layout.Add( new Label(
				string.IsNullOrWhiteSpace( query ) ? "Nothing importable found." : "No matches.", canvas ) );
			empty.SetStyles( $"color: {Theme.TextLight.Hex}; font-size: 11px; margin-left: 6px;" );
		}
	}

	static bool Matches( Assembly assembly, MethodInfo method, string query )
	{
		if ( string.IsNullOrWhiteSpace( query ) )
			return true;

		return method.Name.Contains( query, StringComparison.OrdinalIgnoreCase )
			|| (method.DeclaringType?.Name.Contains( query, StringComparison.OrdinalIgnoreCase ) ?? false)
			|| ToolImporter.FriendlyName( assembly ).Contains( query, StringComparison.OrdinalIgnoreCase );
	}
}
