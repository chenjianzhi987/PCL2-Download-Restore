using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class FindRef
{
    private static IEnumerable<TypeDefinition> AllTypes(ModuleDefinition mod)
    {
        var stack = new Stack<TypeDefinition>(mod.Types);
        while (stack.Count > 0)
        {
            var t = stack.Pop();
            yield return t;
            foreach (var n in t.NestedTypes) stack.Push(n);
        }
    }

    public static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        var path = args[0];
        var outPath = args[1];
        var pat = args[2];
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        var asm = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { AssemblyResolver = resolver, ReadSymbols = false, InMemory = true });
        var mod = asm.MainModule;
        var sb = new StringBuilder();
        var seen = new HashSet<string>();
        foreach (var t in AllTypes(mod))
        {
            foreach (var m in t.Methods)
            {
                if (!m.HasBody) continue;
                foreach (var i in m.Body.Instructions)
                {
                    var mr = i.Operand as MethodReference;
                    var fr = i.Operand as FieldReference;
                    var tr = i.Operand as TypeReference;
                    string key = null, full = null;
                    if (mr != null) { full = "M " + mr.FullName; }
                    else if (fr != null) { full = "F " + fr.FullName; }
                    else if (tr != null) { full = "T " + tr.FullName; }
                    if (full == null) continue;
                    if (full.IndexOf(pat, StringComparison.Ordinal) < 0) continue;
                    key = full;
                    if (!seen.Add(key)) continue;
                    sb.AppendLine(full + "   [in " + t.FullName + "::" + m.Name + "]");
                }
            }
        }
        File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
        Console.WriteLine("unique=" + seen.Count);
        return 0;
    }
}
