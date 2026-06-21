using System.Collections.Generic;
using Editor;
using Sandbox;
using SboxMcp.Registry;

namespace SboxMcp.UI;

/// <summary>
/// Visual language: native editor Theme tones for chrome (matching tools like
/// the Humanoid Retargeter), plus a color per tool category — the Tools page
/// and activity chips keep their colorful identity.
/// </summary>
public static class Palette
{
	// native theme tones - chrome follows the editor's look
	public static Color Running => Theme.Green;
	public static Color Stopped => Theme.TextLight;
	public static Color Error => Theme.Red;
	public static Color Warning => Theme.Yellow;
	public static Color Info => Theme.Blue;
	public static Color Accent => Theme.Primary;

	public static Color CardBackground => Theme.ControlBackground;
	public static Color TextBright => Theme.Text;
	public static Color TextDim => Theme.TextLight;
	public static Color SnippetBackground => Theme.ControlBackground.Darken( 0.35f );

	// per-category colors (Tools page + activity feed chips)
	static readonly Dictionary<ToolCategory, Color> Categories = new()
	{
		[ToolCategory.Scene] = (Color)"#4F8DFD",
		[ToolCategory.GameObject] = (Color)"#6AA5FF",
		[ToolCategory.Component] = (Color)"#8FB8FF",
		[ToolCategory.Prefab] = (Color)"#3DCFB6",
		[ToolCategory.Asset] = (Color)"#FFA94D",
		[ToolCategory.ModelDoc] = (Color)"#A78BFA",
		[ToolCategory.AnimGraph] = (Color)"#F472B6",
		[ToolCategory.ShaderGraph] = (Color)"#2DD4BF",
		[ToolCategory.ActionGraph] = (Color)"#A3E635",
		[ToolCategory.Code] = (Color)"#4ADE80",
		[ToolCategory.Editor] = (Color)"#FACC15",
		[ToolCategory.Retargeter] = (Color)"#FB7185",
		[ToolCategory.Cloud] = (Color)"#38BDF8",
		[ToolCategory.Imported] = (Color)"#94A3B8"
	};

	public static Color For( ToolCategory category ) =>
		Categories.TryGetValue( category, out var c ) ? c : TextDim;

	// per-client accents for the config snippet edges
	public static readonly Color ClaudeAccent = (Color)"#D97757";
	public static readonly Color CursorAccent = (Color)"#7C8AFF";
	public static readonly Color VsCodeAccent = (Color)"#3EA7FF";
}
