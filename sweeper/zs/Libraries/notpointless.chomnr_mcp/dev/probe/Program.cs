using System;
using System.IO;
using System.Linq;
using System.Reflection;

var managed = @"D:\SteamLibrary\steamapps\common\sbox\bin\managed";

AppDomain.CurrentDomain.AssemblyResolve += ( _, e ) =>
{
	var name = new AssemblyName( e.Name ).Name + ".dll";
	var path = Path.Combine( managed, name );
	return File.Exists( path ) ? Assembly.LoadFrom( path ) : null;
};

var queries = args.Length > 0 ? args : new[] { "LogEvent", "Logging" };

foreach ( var file in Directory.GetFiles( managed, "*.dll" ) )
{
	Type[] types;
	try { types = Assembly.LoadFrom( file ).GetExportedTypes(); }
	catch ( Exception e ) { Console.WriteLine( $"!! {Path.GetFileName( file )}: {e.GetType().Name} {e.Message.Split( '\n' )[0]}" ); continue; }

	foreach ( var t in types )
	{
		if ( !queries.Any( q => string.Equals( t.Name, q, StringComparison.OrdinalIgnoreCase ) ) )
			continue;

		Console.WriteLine( $"== {t.FullName}  ({Path.GetFileName( file )})" );
		foreach ( var m in t.GetMembers( BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly ).Take( 40 ) )
			Console.WriteLine( $"   {m.MemberType}: {m}" );
	}
}
