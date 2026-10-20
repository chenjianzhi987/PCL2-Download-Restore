using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class FindStr
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
            Console.Error.WriteLine("usage: FindStr <assembly> <out.txt> <keyword> [--il]");
            return 2;
        }
        var path = args[0];
        var outPath = args[1];
        var keyword = args[2];
        var showIl = args.Length > 3 && args[3] == "--il";

        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        var asm = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { AssemblyResolver = resolver, ReadSymbols = false, InMemory = true });
        var mod = asm.MainModule;

        var hits = new List<string>();
        var hitMethods = new List<MethodDefinition>();
        var ilSb = new StringBuilder();

        foreach (var t in AllTypes(mod))
        {
            foreach (var m in t.Methods)
            {
                if (!m.HasBody) continue;
                foreach (var i in m.Body.Instructions)
                {
                    var s = i.Operand as string;
                    if (i.OpCode == OpCodes.Ldstr && s != null && s.Contains(keyword))
                    {
                        hitMethods.Add(m);
                        hits.Add(t.FullName + "\t" + m.FullName + "\t#" + i.Offset + "\t" + s);
                        break;
                    }
                }
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine("=== ldstr matches: " + keyword + " (" + hits.Count + ") ===");
        foreach (var h in hits) sb.AppendLine(h);

        if (showIl)
        {
            foreach (var m in hitMethods)
            {
                ilSb.AppendLine("===== " + m.DeclaringType.FullName + " :: " + m.FullName + " =====");
                foreach (var i in m.Body.Instructions) ilSb.AppendLine("  " + i);
            }
        }

        // callers: 谁调用了这些方法
        var hitTokens = new HashSet<string>(hitMethods.Select(m => m.FullName));
        sb.AppendLine();
        sb.AppendLine("=== callers of matched methods ===");
        foreach (var t in AllTypes(mod))
        {
            foreach (var m in t.Methods)
            {
                if (!m.HasBody) continue;
                foreach (var i in m.Body.Instructions)
                {
                    var mr = i.Operand as MethodReference;
                    if (mr == null) continue;
                    if (!hitTokens.Contains(mr.FullName)) continue;
                    sb.AppendLine(t.FullName + "\t" + m.FullName + "\t-> calls\t" + mr.FullName);
                }
            }
        }

        // 反向：这些方法的调用者中，有没有来自 udt/page 事件
        File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
        if (showIl && ilSb.Length > 0) File.WriteAllText(outPath + ".il.txt", ilSb.ToString(), new UTF8Encoding(false));
        Console.WriteLine("hits=" + hits.Count);
        return 0;
    }
}
