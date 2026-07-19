using System;
using System.IO;
using System.Linq;
using System.Reflection;

var managed = @"C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data\Managed";
AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
{
    var name = new AssemblyName(args.Name).Name + ".dll";
    var path = Path.Combine(managed, name);
    return File.Exists(path) ? Assembly.LoadFrom(path) : null;
};

var asm = Assembly.LoadFrom(Path.Combine(managed, "publicized_assemblies", "assembly_valheim_publicized.dll"));
foreach (var typeName in new[] { "Terminal", "Console", "Chat" })
{
    var t = asm.GetType(typeName);
    Console.WriteLine("=== " + typeName + " ===");
    if (t == null) continue;
    foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
        .Where(m => m.Name.Contains("Init") || m.Name.Contains("Awake") || m.Name.Contains("TryRun") || m.Name.Contains("Input") || m.Name.Contains("Send")))
        Console.WriteLine(m.ToString());
}
