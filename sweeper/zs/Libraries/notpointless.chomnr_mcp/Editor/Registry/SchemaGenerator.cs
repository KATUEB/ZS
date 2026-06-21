using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SboxMcp.Registry;

/// <summary>
/// Reflects a tool method's parameters into a JSON Schema object.
/// </summary>
public static class SchemaGenerator
{
	public static JsonElement ForMethod( MethodInfo method )
	{
		var properties = new JsonObject();
		var required = new JsonArray();

		foreach ( var p in method.GetParameters() )
		{
			var prop = ForType( p.ParameterType );

			var desc = p.GetCustomAttribute<DescAttribute>()?.Text;
			if ( desc is not null )
				prop["description"] = desc;

			if ( p.HasDefaultValue )
			{
				if ( p.DefaultValue is not null )
					prop["default"] = JsonValue.Create( p.DefaultValue is Enum e ? e.ToString() : p.DefaultValue );
			}
			else
			{
				required.Add( p.Name );
			}

			properties[p.Name] = prop;
		}

		var schema = new JsonObject
		{
			["type"] = "object",
			["properties"] = properties
		};

		if ( required.Count > 0 )
			schema["required"] = required;

		return JsonSerializer.SerializeToElement( schema );
	}

	static JsonObject ForType( Type t )
	{
		t = Nullable.GetUnderlyingType( t ) ?? t;

		if ( t == typeof( string ) )
			return new JsonObject { ["type"] = "string" };

		if ( t == typeof( bool ) )
			return new JsonObject { ["type"] = "boolean" };

		if ( t == typeof( int ) || t == typeof( long ) || t == typeof( short ) || t == typeof( byte ) )
			return new JsonObject { ["type"] = "integer" };

		if ( t == typeof( float ) || t == typeof( double ) || t == typeof( decimal ) )
			return new JsonObject { ["type"] = "number" };

		if ( t.IsEnum )
		{
			var values = new JsonArray();
			foreach ( var name in Enum.GetNames( t ) )
				values.Add( name );

			return new JsonObject { ["type"] = "string", ["enum"] = values };
		}

		if ( t.IsArray )
			return new JsonObject { ["type"] = "array", ["items"] = ForType( t.GetElementType() ) };

		if ( t.IsGenericType && typeof( IEnumerable ).IsAssignableFrom( t ) )
			return new JsonObject { ["type"] = "array", ["items"] = ForType( t.GetGenericArguments()[0] ) };

		if ( t == typeof( JsonElement ) )
			return new JsonObject(); // accepts anything

		// fall back to a JSON-deserializable object
		return new JsonObject { ["type"] = "object" };
	}
}
