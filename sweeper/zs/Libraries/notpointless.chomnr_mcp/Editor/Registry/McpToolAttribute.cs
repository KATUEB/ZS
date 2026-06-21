using System;

namespace SboxMcp.Registry;

public enum ToolCategory
{
	Scene,
	GameObject,
	Component,
	Prefab,
	Asset,
	ModelDoc,
	AnimGraph,
	ShaderGraph,
	ActionGraph,
	Code,
	Editor,
	Retargeter,
	Cloud,
	Imported
}

/// <summary>
/// Marks a static method as an MCP tool. The registry reflects the method's
/// parameters into a JSON Schema and exposes it via tools/list.
/// </summary>
[AttributeUsage( AttributeTargets.Method )]
public sealed class McpToolAttribute : Attribute
{
	public string Name { get; }
	public string Description { get; }
	public ToolCategory Category { get; }

	/// <summary>Write tools are subject to the permission gate (approve-writes / read-only modes).</summary>
	public bool Writes { get; init; }

	/// <summary>
	/// Optional requirement key (e.g. an integration's library ident). The host
	/// resolves it via ToolRegistry.RequirementResolver; unresolved tools are
	/// hidden from clients and shown disabled in the tool browser.
	/// </summary>
	public string Requires { get; init; }

	/// <summary>
	/// Ships disabled; the user must enable it in the tool browser. Used for
	/// tools with external effects (e.g. downloading cloud assets).
	/// </summary>
	public bool DisabledByDefault { get; init; }

	public McpToolAttribute( string name, string description, ToolCategory category )
	{
		Name = name;
		Description = description;
		Category = category;
	}
}

/// <summary>
/// Optional description for a tool parameter, surfaced in the JSON Schema.
/// </summary>
[AttributeUsage( AttributeTargets.Parameter )]
public sealed class DescAttribute : Attribute
{
	public string Text { get; }
	public DescAttribute( string text ) { Text = text; }
}

/// <summary>
/// Thrown when tool arguments are missing or cannot be bound; surfaced to the
/// MCP client as an isError tool result.
/// </summary>
public sealed class ToolArgumentException : Exception
{
	public ToolArgumentException( string message, Exception inner = null ) : base( message, inner ) { }
}
