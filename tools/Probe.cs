using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Mono.Cecil;

internal static class Probe
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        var path = args[0];
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        var asm = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { AssemblyResolver = resolver, ReadSymbols = false, InMemory = true });
        Console.WriteLine("Assembly: " + asm.Name);
        Console.WriteLine("Modules: " + asm.Modules.Count);
        foreach (var m in asm.Modules)
        {
            Console.WriteLine("  module " + m.Name + " types=" + m.Types.Count + " refs=" + m.AssemblyReferences.Count);
            foreach (var r in m.AssemblyReferences) Console.WriteLine("    asmref " + r.FullName);
        }
        Console.WriteLine("Resources: " + asm.MainModule.Resources.Count);
        foreach (var r in asm.MainModule.Resources)
        {
            Console.WriteLine("  res " + r.Name + " (" + r.ResourceType + ") size=" + r.GetType());
        }
        // 找 MeloongCore 类型定义在哪
        var refsOfMeloong = new List<string>();
        foreach (var m in asm.Modules)
        foreach (var t in m.Types)
        {
            if (t.FullName.StartsWith("MeloongCore", StringComparison.Ordinal)) { refsOfMeloong.Add(m.Name + " :: " + t.FullName); }
        }
        Console.WriteLine("MeloongCore types defined in modules: " + refsOfMeloong.Count);
        foreach (var s in refsOfMeloong.Take(10)) Console.WriteLine("  " + s);
        return 0;
    }
}
