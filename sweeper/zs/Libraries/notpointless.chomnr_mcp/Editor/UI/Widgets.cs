using System;
using System.Collections.Generic;
using Editor;
using Sandbox;
using SboxMcp.Registry;

namespace SboxMcp.UI;

/// <summary>
/// Flat title bar in the native editor style, with a live status chip.
/// </summary>
public class HeaderBar : Widget
{
	public string Title = "s&box MCP";
	public string Subtitle = "Model Context Protocol server";

	public string StatusText = "stopped";
	public Color StatusColor = Palette.Stopped;
	public float Pulse; // 0..1, driven by the dock's frame tick

	public HeaderBar( Widget parent ) : base( parent )
	{
		FixedHeight = 44;
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		Paint.TextAntialiasing = true;
		Paint.ClearPen();

		Paint.SetBrush( Palette.CardBackground );
		Paint.DrawRect( LocalRect );

		var textRect = LocalRect.Shrink( 12, 6 );
		Paint.SetPen( Palette.TextBright );
		Paint.SetDefaultFont( 10, 700 );
		Paint.DrawText( textRect, Title, TextFlag.LeftTop );

		Paint.SetPen( Palette.TextDim );
		Paint.SetDefaultFont( 7 );
		Paint.DrawText( textRect, Subtitle, TextFlag.LeftBottom );

		// status chip: tone pill at 18% alpha with a glowing dot, tone text
		Paint.SetDefaultFont( 7, 600 );
		var label = StatusText;
		var width = Paint.MeasureText( label ).x + 30;
		var pill = new Rect( LocalRect.Right - width - 12, LocalRect.Center.y - 10, width, 20 );

		Paint.ClearPen();
		Paint.SetBrush( StatusColor.WithAlpha( 0.18f ) );
		Paint.DrawRect( pill, 10 );

		var dot = new Vector2( pill.Left + 11, pill.Center.y );
		Paint.SetBrush( StatusColor.WithAlpha( 0.3f ) );
		Paint.DrawCircle( dot, 9f + Pulse * 5f );
		Paint.SetBrush( StatusColor );
		Paint.DrawCircle( dot, 7f );

		Paint.SetPen( StatusColor );
		Paint.DrawText( new Rect( pill.Left + 20, pill.Top, pill.Width - 24, pill.Height ), label, TextFlag.LeftCenter );
	}
}

/// <summary>
/// Small rounded chip in a category's color (Tools page + activity feed).
/// </summary>
public class CategoryChip : Widget
{
	public ToolCategory Category;
	public bool Toggled = true;
	public Action OnToggled;
	public bool Clickable;

	public CategoryChip( ToolCategory category, Widget parent, bool clickable = false ) : base( parent )
	{
		Category = category;
		Clickable = clickable;
		FixedHeight = 20;
		FixedWidth = 12 + Category.ToString().Length * 6.5f;
		Cursor = clickable ? CursorShape.Finger : CursorShape.Arrow;
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		var color = Palette.For( Category );
		var active = !Clickable || Toggled;

		Paint.ClearPen();
		Paint.SetBrush( color.WithAlpha( active ? (Paint.HasMouseOver ? 0.35f : 0.22f) : 0.07f ) );
		Paint.DrawRect( LocalRect, 10 );

		Paint.SetPen( active ? color : Palette.TextDim );
		Paint.SetDefaultFont( 7, 600 );
		Paint.DrawText( LocalRect, Category.ToString(), TextFlag.Center );
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		base.OnMouseClick( e );

		if ( !Clickable )
			return;

		Toggled = !Toggled;
		OnToggled?.Invoke();
		Update();
	}
}

/// <summary>
/// A row of fixed-size widgets that wraps to new lines instead of squashing
/// when the panel is narrow.
/// </summary>
public class FlowRow : Widget
{
	readonly List<Widget> _items = new();

	public float Spacing = 4;

	public FlowRow( Widget parent ) : base( parent )
	{
		HorizontalSizeMode = SizeMode.Flexible;
	}

	public T AddItem<T>( T widget ) where T : Widget
	{
		widget.Parent = this;
		_items.Add( widget );
		Arrange();
		return widget;
	}

	protected override void OnResize()
	{
		base.OnResize();
		Arrange();
	}

	void Arrange()
	{
		var available = MathF.Max( Width, 60 );
		float x = 0, y = 0, rowHeight = 0;

		foreach ( var item in _items )
		{
			var w = item.FixedWidth;
			var h = item.FixedHeight;

			if ( x > 0 && x + w > available )
			{
				x = 0;
				y += rowHeight + Spacing;
				rowHeight = 0;
			}

			item.Position = new Vector2( x, y );
			x += w + Spacing;
			rowHeight = MathF.Max( rowHeight, h );
		}

		FixedHeight = y + rowHeight;
	}
}

/// <summary>
/// Dark monospace code block with a one-click copy that flashes confirmation.
/// </summary>
public class CodeSnippet : Widget
{
	public string Code;
	public Color Accent;

	RealTimeSince _copiedFlash = 999;

	public CodeSnippet( string code, Color accent, Widget parent ) : base( parent )
	{
		Code = code;
		Accent = accent;

		var lines = code.Split( '\n' ).Length;
		FixedHeight = lines * 14 + 18; // 9px padding above and below the text
		Cursor = CursorShape.Finger;
		ToolTip = "Click to copy";
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		Paint.ClearPen();

		Paint.SetBrush( Palette.SnippetBackground );
		Paint.DrawRect( LocalRect, 4 );

		// accent edge
		Paint.SetBrush( Accent );
		Paint.DrawRect( new Rect( LocalRect.Left, LocalRect.Top + 6, 3, LocalRect.Height - 12 ), 1.5f );

		// code text
		Paint.SetPen( Paint.HasMouseOver ? Palette.TextBright : Palette.TextDim );
		Paint.SetFont( "Consolas", 8 );

		var y = LocalRect.Top + 9;
		foreach ( var line in Code.Split( '\n' ) )
		{
			Paint.DrawText( new Rect( LocalRect.Left + 14, y, LocalRect.Width - 50, 14 ), line, TextFlag.LeftCenter | TextFlag.SingleLine );
			y += 14;
		}

		// copy affordance; keep repainting while the flash is visible so it
		// actually expires (nothing else schedules a repaint)
		var justCopied = _copiedFlash < 1.2f;
		if ( justCopied )
			Update();

		Paint.SetPen( justCopied ? Palette.Running : (Paint.HasMouseOver ? Palette.TextBright : Palette.TextDim) );

		if ( justCopied )
		{
			Paint.SetDefaultFont( 7, 600 );
			Paint.DrawText( new Rect( LocalRect.Right - 70, LocalRect.Top + 6, 60, 16 ), "Copied ✓", TextFlag.RightCenter );
		}
		else
		{
			Paint.DrawIcon( new Rect( LocalRect.Right - 28, LocalRect.Top + 6, 18, 18 ), "content_copy", 13, TextFlag.Center );
		}
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		base.OnMouseClick( e );
		EditorUtility.Clipboard.Copy( Code );
		_copiedFlash = 0;
		Update();
	}
}

/// <summary>
/// Titled section box matching the editor's native Group widget (which lives
/// in the tools addon and is replicated here so the offline compile gate
/// keeps working): subtle rounded background, icon + title header.
/// </summary>
public class GroupBox : Widget
{
	public string Title { get; set; } = "";
	public string Icon { get; set; }

	public GroupBox( Widget parent ) : base( parent )
	{
		Layout = Layout.Column();
		Layout.Margin = new Sandbox.UI.Margin( 14, 30, 14, 12 ); // top clears the header
		Layout.Spacing = 6;
	}

	protected override void OnPaint()
	{
		Paint.ClearPen();
		Paint.SetBrush( Theme.ButtonBackground.WithAlpha( 0.1f ) );
		Paint.DrawRect( LocalRect.Shrink( 0, 1 ), 4.0f );
		Paint.ClearBrush();

		var headerRect = new Rect( 0, 0, Width, 28 );
		var left = 14f;

		if ( !string.IsNullOrWhiteSpace( Icon ) )
		{
			Paint.SetPen( Theme.Text.WithAlpha( 0.8f ) );
			Paint.DrawIcon( headerRect.Shrink( left, 0, 0, 0 ), Icon, 18, TextFlag.LeftCenter );
			left += 24;
		}

		Paint.SetDefaultFont( 8, 400 );
		Paint.SetPen( Theme.Text );
		Paint.DrawText( headerRect.Shrink( left, 0, 0, 0 ), Title, TextFlag.LeftCenter );
	}
}

/// <summary>
/// Rounded container in the editor's control background, optionally with a
/// tone-colored edge (used for approval cards).
/// </summary>
public class Card : Widget
{
	public Color? EdgeAccent;

	public Card( Widget parent ) : base( parent )
	{
		Layout = Layout.Column();
		Layout.Margin = 12;
		Layout.Spacing = 6;
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		Paint.ClearPen();

		Paint.SetBrush( Palette.CardBackground );
		Paint.DrawRect( LocalRect, 4 );

		if ( EdgeAccent is not null )
		{
			Paint.SetBrush( EdgeAccent.Value );
			Paint.DrawRect( new Rect( LocalRect.Left, LocalRect.Top + 8, 3, LocalRect.Height - 16 ), 1.5f );
		}
	}
}

/// <summary>
/// One tab in the dock's tab bar - neutral editor styling, primary-color
/// underline when active.
/// </summary>
public class TabButton : Widget
{
	public string Text;
	public string Icon;
	public bool Active;
	public Action Clicked;
	public int Badge;

	public TabButton( string text, string icon, Widget parent ) : base( parent )
	{
		Text = text;
		Icon = icon;
		FixedHeight = 32;
		FixedWidth = 46 + text.Length * 7f;
		Cursor = CursorShape.Finger;
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		Paint.ClearPen();

		if ( Paint.HasMouseOver && !Active )
		{
			Paint.SetBrush( Color.White.WithAlpha( 0.04f ) );
			Paint.DrawRect( LocalRect, 4 );
		}

		var fg = Active ? Palette.TextBright : Palette.TextDim;

		Paint.SetPen( fg );
		Paint.DrawIcon( new Rect( LocalRect.Left + 8, LocalRect.Top, 16, LocalRect.Height ), Icon, 14, TextFlag.Center );

		Paint.SetDefaultFont( 8, Active ? 700 : 400 );
		Paint.DrawText( new Rect( LocalRect.Left + 28, LocalRect.Top, LocalRect.Width - 30, LocalRect.Height ), Text, TextFlag.LeftCenter );

		if ( Badge > 0 )
		{
			var b = new Rect( LocalRect.Right - 16, LocalRect.Top + 6, 14, 14 );
			Paint.ClearPen();
			Paint.SetBrush( Palette.Warning );
			Paint.DrawCircle( b.Center, 14 );
			Paint.SetPen( Color.Black );
			Paint.SetDefaultFont( 7, 700 );
			Paint.DrawText( b, Badge > 9 ? "9" : Badge.ToString(), TextFlag.Center );
		}

		if ( Active )
		{
			Paint.ClearPen();
			Paint.SetBrush( Palette.Accent );
			Paint.DrawRect( new Rect( LocalRect.Left + 6, LocalRect.Bottom - 3, LocalRect.Width - 12, 3 ), 1.5f );
		}
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		base.OnMouseClick( e );
		Clicked?.Invoke();
	}
}
