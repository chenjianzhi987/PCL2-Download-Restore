using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class DumpMethod
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
        if (args.Length < 3)
        {
            Console.Error.WriteLine("usage: DumpMethod <asm> <out.txt> <typeSubstring> [methodSubstring]");
            return 2;
        }
        var path = args[0];
        var outPath = args[1];
        var typePat = args[2];
        var methodPat = args.Length > 3 ? args[3] : null;

        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        var asm = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { AssemblyResolver = resolver, ReadSymbols = false, InMemory = true });
        var sb = new StringBuilder();
        int found = 0;

        foreach (var t in AllTypes(asm.MainModule))
        {
            if (t.FullName.IndexOf(typePat, StringComparison.OrdinalIgnoreCase) < 0) continue;
            found++;
            sb.AppendLine("########## TYPE " + t.FullName + " ##########");
            foreach (var f in t.Fields)
            {
                string val = f.HasConstant ? " = " + f.Constant : "";
                sb.AppendLine("  FIELD " + f.Name + " : " + f.FieldType.FullName + val);
            }
            foreach (var m in t.Methods.OrderBy(x => x.Name))
            {
                if (methodPat != null && m.Name.IndexOf(methodPat, StringComparison.OrdinalIgnoreCase) < 0) continue;
                sb.AppendLine();
                sb.AppendLine("===== " + m.FullName + " =====");
                if (!m.HasBody) { sb.AppendLine("  <no body>"); continue; }
                foreach (var v in m.Body.Variables) sb.AppendLine("  .local V" + v.Index + " " + v.VariableType);
                foreach (var i in m.Body.Instructions) sb.AppendLine("  " + i);
            }
        }
        File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
        Console.WriteLine("types=" + found + " -> " + outPath);
        return 0;
    }
}
