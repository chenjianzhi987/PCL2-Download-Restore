using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class Ildump
{
    private static string Normalize(MethodDefinition m)
    {
        var sb = new StringBuilder();
        if (!m.HasBody) return "<nobody>";
        foreach (var i in m.Body.Instructions)
        {
            sb.Append(i.OpCode.Name);
            var s = i.Operand as string;
            var mr = i.Operand as MethodReference;
            var fr = i.Operand as FieldReference;
            var tr = i.Operand as TypeReference;
            var v = i.Operand as VariableDefinition;
            var p = i.Operand as ParameterDefinition;
            if (s != null) sb.Append(' ').Append(s);
            else if (mr != null) sb.Append(' ').Append(mr.FullName);
            else if (fr != null) sb.Append(' ').Append(fr.FullName);
            else if (tr != null) sb.Append(' ').Append(tr.FullName);
            else if (v != null) sb.Append(" V").Append(v.Index);
            else if (p != null) sb.Append(" P").Append(p.Index);
            else if (i.Operand != null) sb.Append(' ').Append(i.Operand);
            sb.Append('\n');
        }
        // 去掉 PDB 相关的无意义差异：方法体本身即指纹
        return sb.ToString();
    }

    private static string Sha(string text)
    {
        using (var sha = SHA256.Create())
        {
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant();
        }
    }

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
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: Ildump <assembly> <out.txt> [--il <TypeFullName>]");
            return 2;
        }
        var path = args[0];
        var outPath = args[1];
        var dumpType = args.Length >= 4 && args[2] == "--il" ? args[3] : null;

        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        var rp = new ReaderParameters { AssemblyResolver = resolver, ReadSymbols = false, InMemory = true };
        var asm = AssemblyDefinition.ReadAssembly(path, rp);

        var lines = new List<string>();
        var ilDump = new StringBuilder();

        foreach (var t in AllTypes(asm.MainModule).OrderBy(x => x.FullName))
        {
            foreach (var m in t.Methods.OrderBy(x => x.Name))
            {
                var norm = Normalize(m);
                var ilLen = m.HasBody ? m.Body.Instructions.Count : 0;
                lines.Add(string.Join("\t", new[] {
                    t.FullName,
                    m.Name,
                    m.Parameters.Count.ToString(),
                    ilLen.ToString(),
                    Sha(norm)
                }));
                if (dumpType != null && (t.FullName == dumpType || t.FullName.EndsWith("." + dumpType, StringComparison.OrdinalIgnoreCase)))
                {
                    ilDump.AppendLine("===== " + t.FullName + " :: " + m.FullName + " =====");
                    if (m.HasBody)
                    {
                        foreach (var i in m.Body.Instructions) ilDump.AppendLine("  " + i);
                        foreach (var v in m.Body.Variables) ilDump.AppendLine("  .local " + v.Index + " " + v.VariableType);
                    }
                    else ilDump.AppendLine("  <no body>");
                }
            }
            // 字段与常量（下载限制常量可能在此）
            foreach (var f in t.Fields)
            {
                string val = "";
                if (f.HasConstant) val = " = " + (f.Constant ?? "null");
                lines.Add(string.Join("\t", new[] { t.FullName, "[field] " + f.Name, f.FieldType.FullName, f.IsLiteral ? "literal" : (f.IsStatic ? "static" : "inst"), val }));
            }
        }

        File.WriteAllLines(outPath, lines, new UTF8Encoding(false));
        if (dumpType != null)
        {
            var ilPath = outPath + ".il.txt";
            File.WriteAllText(ilPath, ilDump.ToString(), new UTF8Encoding(false));
            Console.WriteLine("IL -> " + ilPath);
        }
        Console.WriteLine("methods/fields -> " + outPath + " (" + lines.Count + " lines)");
        return 0;
    }
}
