using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace PCLDownloadRestore
{
    /// <summary>
    /// PCL 2.99.0.0 原版下载功能恢复工具。
    ///
    /// 背景：PCL 2.99.0.0（2026-10-20）为遵守相关法律法规，移除了 Minecraft 本体与资源包的下载功能。
    /// 实现方式是在 PageDownloadInstall.LoaderInit() 里，把页面本来要用的共享 LoaderTask
    /// （ModDownload.DlClientListLoader）替换成一个新建的、名字叫「关闭下载」的临时 LoaderTask，
    /// 它的完成回调直接 throw new Exception("为遵守相关法律法规，PCL 暂时不再提供 MC 下载功能…")，
    /// 于是下载页一加载就失败。下载模块本体（ModDownload 里所有 loader、BMCLAPI/Mojang 源、
    /// 安装流程）在 2.99 里全部保留，只是不再被页面引用。
    ///
    /// 本工具把这些被替换的入口方法原地还原成官方原版等价实现：
    ///   P1. PageDownloadInstall.LoaderInit：受限 loader → ldsfld ModDownload.DlClientListLoader
    ///   注意：本版只恢复「下载」页入口；ModModpack.ModpackInstall()（手动安装整合包）等其它入口未修复，见 README。
    ///
    /// 采用二进制原地修补：新指令序列与原序列等长（不足补 nop），因此
    ///   * 不重写 PE 元数据表，不移动任何其它方法；
    ///   * 后续指令地址全部不变；
    ///   * 未修改字节逐一保持原样。
    /// </summary>
    internal static class Program
    {
        // 内嵌 Mono.Cecil：发布成单文件 exe 时由这里在内存中加载
        static Program()
        {
            AppDomain.CurrentDomain.AssemblyResolve += (sender, e) =>
            {
                var name = new AssemblyName(e.Name).Name;
                if (name != "Mono.Cecil") return null;
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("PCLDownloadRestore.Mono.Cecil.dll"))
                {
                    if (stream == null) return null;
                    var buffer = new byte[stream.Length];
                    var read = 0;
                    while (read < buffer.Length)
                    {
                        var n = stream.Read(buffer, read, buffer.Length - read);
                        if (n <= 0) break;
                        read += n;
                    }
                    return Assembly.Load(buffer);
                }
            };
        }

        private const string OfficialSha256 = "6b6e1260cb5a1d6a303855f1c60cab3d0bc1aebba7e40a964180188b271e7386";
        private const string RestrictionText = "为遵守相关法律法规，PCL 暂时不再提供 MC 下载功能";
        private const string BackupDirName = "PCL-download-restore-backup";

        private static string Sha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var fs = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
        }

        private struct PeSection
        {
            public uint VirtualAddress;
            public uint VirtualSize;
            public uint RawPointer;
            public uint RawSize;
        }

        private static List<PeSection> ReadSections(byte[] pe)
        {
            var eLfanew = BitConverter.ToInt32(pe, 0x3C);
            var numSections = BitConverter.ToUInt16(pe, eLfanew + 6);
            var sizeOptHeader = BitConverter.ToUInt16(pe, eLfanew + 20);
            var secStart = eLfanew + 24 + sizeOptHeader;
            var list = new List<PeSection>();
            for (int i = 0; i < numSections; i++)
            {
                var off = secStart + i * 40;
                list.Add(new PeSection
                {
                    VirtualSize = BitConverter.ToUInt32(pe, off + 8),
                    VirtualAddress = BitConverter.ToUInt32(pe, off + 12),
                    RawSize = BitConverter.ToUInt32(pe, off + 16),
                    RawPointer = BitConverter.ToUInt32(pe, off + 20),
                });
            }
            return list;
        }

        private static int RvaToOffset(List<PeSection> sections, uint rva)
        {
            foreach (var s in sections)
            {
                if (rva >= s.VirtualAddress && rva < s.VirtualAddress + Math.Max(s.VirtualSize, s.RawSize))
                    return (int)(s.RawPointer + (rva - s.VirtualAddress));
            }
            throw new InvalidOperationException("RVA 不在任何节内：0x" + rva.ToString("X"));
        }

        private static string FindPcl(string arg)
        {
            if (!string.IsNullOrEmpty(arg) && File.Exists(arg)) return Path.GetFullPath(arg);
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            foreach (var n in new[] { "Plain Craft Launcher 2.exe", "PCL2.exe", "PCL.exe" })
            {
                var p = Path.Combine(baseDir, n);
                if (File.Exists(p)) return p;
            }
            // 常见安装位置
            foreach (var root in new[] { @"D:\PCL", @"C:\PCL", @"E:\PCL", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "PCL") })
            {
                foreach (var n in new[] { "Plain Craft Launcher 2.exe", "PCL2.exe", "PCL.exe" })
                {
                    var p = Path.Combine(root, n);
                    if (File.Exists(p)) return p;
                }
            }
            return null;
        }

        public static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            try
            {
                if (args.Length >= 1 && (args[0] == "--help" || args[0] == "/?" || args[0] == "-h"))
                {
                    PrintHelp();
                    return 0;
                }
                if (args.Length >= 1 && (args[0] == "--restore" || args[0] == "/restore"))
                {
                    return Restore(args.Length >= 2 ? args[1] : null);
                }
                var target = FindPcl(args.Length >= 1 && !args[0].StartsWith("-") ? args[0] : null);
                if (target == null)
                {
                    Console.Error.WriteLine("找不到 PCL 启动器。");
                    Console.Error.WriteLine("请把本工具放到 PCL.exe（Plain Craft Launcher 2.exe）旁边，");
                    Console.Error.WriteLine("或运行：PCL-下载恢复.exe \"D:\\PCL\\Plain Craft Launcher 2.exe\"");
                    return 2;
                }
                return Patch(target, args.Contains("--check"));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FAILED: " + ex.Message);
                return 1;
            }
        }

        private static void PrintHelp()
        {
            Console.WriteLine("PCL 2.99.0.0 原版下载恢复工具");
            Console.WriteLine();
            Console.WriteLine("用法：");
            Console.WriteLine("  PCL-下载恢复.exe                      自动查找旁边的 PCL 并修复");
            Console.WriteLine("  PCL-下载恢复.exe \"路径\\Plain Craft Launcher 2.exe\"");
            Console.WriteLine("  PCL-下载恢复.exe --check               只检查，不写文件");
            Console.WriteLine("  PCL-下载恢复.exe --restore             从备份还原官方原版");
        }

        private static int Patch(string target, bool checkOnly)
        {
            Console.WriteLine("PCL 原版下载恢复工具 · 目标：" + target);
            var sha = Sha256(target);
            Console.WriteLine("当前 SHA-256：" + sha);
            var official = sha == OfficialSha256;
            Console.WriteLine(official ? "版本校验：官方 PCL 2.99.0.0 基准文件 ✓" : "版本校验：非官方基准文件（将按方法结构特征判断）");

            var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(Path.GetDirectoryName(target));
            AssemblyDefinition asm;
            try
            {
                asm = AssemblyDefinition.ReadAssembly(target, new ReaderParameters { AssemblyResolver = resolver, ReadSymbols = false, InMemory = true });
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("无法读取 PCL 文件（可能被占用或不是 .NET 程序）：" + ex.Message);
                return 3;
            }
            var mod = asm.MainModule;
            var pageType = mod.GetType("PCL.PageDownloadInstall");
            if (pageType == null) { Console.Error.WriteLine("版本不受支持：找不到 PCL.PageDownloadInstall。"); return 3; }

            var changes = new List<PatchChange>();
            var p1 = PlanDownloadPageLoader(mod, pageType);
            if (p1 != null) changes.Add(p1);

            if (changes.Count == 0)
            {
                Console.WriteLine("结果：下载入口已处于启用状态，无需修改。");
                return 10;
            }

            foreach (var c in changes)
                Console.WriteLine("  · " + c.Description);

            if (checkOnly)
            {
                Console.WriteLine("CHECK_OK：结构与替换方案校验通过，未写入任何文件。");
                return 0;
            }

            var pe = File.ReadAllBytes(target);
            var sections = ReadSections(pe);
            var patched = (byte[])pe.Clone();
            foreach (var c in changes)
            {
                int methodOff = RvaToOffset(sections, (uint)c.Method.RVA);
                int headerByte = pe[methodOff];
                int codeStart;
                if ((headerByte & 3) == 2) codeStart = methodOff + 1;
                else if ((headerByte & 3) == 3) codeStart = methodOff + 12;
                else throw new InvalidOperationException("无法解析方法头：" + c.Method.FullName);
                int at = codeStart + c.StartIl;
                if (pe[at] != c.ExpectedFirstOpcode)
                    throw new InvalidOperationException("目标位置字节不符（" + c.Method.FullName + "），已中止。");
                for (int i = 0; i < c.NewBytes.Length; i++)
                {
                    if (i >= c.OldLength) throw new InvalidOperationException("替换序列过长，已中止。");
                    patched[at + i] = c.NewBytes[i];
                }
                for (int i = c.NewBytes.Length; i < c.OldLength; i++) patched[at + i] = 0x00; // nop 填充
            }

            // 备份
            var backupDir = Path.Combine(Path.GetDirectoryName(target), BackupDirName);
            Directory.CreateDirectory(backupDir);
            var backup = Path.Combine(backupDir, sha + ".exe");
            if (!File.Exists(backup)) File.Copy(target, backup);

            var temp = target + ".restore-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            File.WriteAllBytes(temp, patched);

            // 复核
            try
            {
                Verify(temp, changes);
            }
            catch (Exception ex)
            {
                File.Delete(temp);
                Console.Error.WriteLine("补丁复核失败，未改动目标文件：" + ex.Message);
                return 5;
            }

            try
            {
                File.Replace(temp, target, null);
            }
            catch (Exception)
            {
                File.Delete(target);
                File.Move(temp, target);
            }

            var newSha = Sha256(target);
            Console.WriteLine("还原备份：" + backup);
            Console.WriteLine("输出 SHA-256：" + newSha);
            Console.WriteLine("SUCCESS：原版 MC 下载入口已恢复。启动 PCL 后进入「下载 → 原版游戏」即可。");
            Console.WriteLine("需要还原官方原版时运行：PCL-下载恢复.exe --restore");
            return 0;
        }

        private class PatchChange
        {
            public MethodDefinition Method;
            public int StartIl;
            public int OldLength;
            public byte[] NewBytes;
            public byte ExpectedFirstOpcode;
            public string Description;
        }

        private static byte[] EncodeI4(int value)
        {
            return BitConverter.GetBytes(value);
        }

        private static PatchChange PlanDownloadPageLoader(ModuleDefinition mod, TypeDefinition pageType)
        {
            var loaderInit = pageType.Methods.FirstOrDefault(x => x.Name == "LoaderInit" && x.Parameters.Count == 0);
            if (loaderInit == null || !loaderInit.HasBody) return null;
            var ins = loaderInit.Body.Instructions;

            bool alreadyFixed = !ins.Any(x => x.OpCode == OpCodes.Ldstr && (string)x.Operand == "关闭下载");
            if (alreadyFixed) return null;

            int newobjIdx = -1;
            for (int i = 0; i < ins.Count; i++)
            {
                if (ins[i].OpCode != OpCodes.Newobj) continue;
                var ctor = ins[i].Operand as MethodReference;
                if (ctor != null && ctor.DeclaringType.FullName.StartsWith("PCL.ModLoader/LoaderTask`2", StringComparison.Ordinal))
                { newobjIdx = i; break; }
            }
            if (newobjIdx < 0) throw new InvalidOperationException("LoaderInit 结构不符：找不到 LoaderTask 构造。");

            int startIdx = -1;
            for (int i = newobjIdx - 1; i >= 0; i--)
            {
                if (ins[i].OpCode == OpCodes.Ldstr && (string)ins[i].Operand == "关闭下载") { startIdx = i; break; }
            }
            if (startIdx < 0) throw new InvalidOperationException("LoaderInit 结构不符：找不到受限 loader。");

            int startIl = ins[startIdx].Offset;
            int endIl = ins[newobjIdx].Offset + ins[newobjIdx].GetSize();
            var removed = new HashSet<Instruction>();
            for (int i = startIdx; i <= newobjIdx; i++) removed.Add(ins[i]);
            foreach (var i0 in ins)
            {
                if (removed.Contains(i0)) continue;
                var t1 = i0.Operand as Instruction;
                if (t1 != null && removed.Contains(t1)) throw new InvalidOperationException("受限 loader 段被外部跳转引用。");
                var tArr = i0.Operand as Instruction[];
                if (tArr != null && tArr.Any(removed.Contains)) throw new InvalidOperationException("受限 loader 段被 switch 引用。");
            }

            var mdType = mod.GetType("PCL.ModDownload");
            var field = mdType == null ? null : mdType.Fields.FirstOrDefault(f => f.Name == "DlClientListLoader");
            if (field == null) throw new InvalidOperationException("找不到 ModDownload.DlClientListLoader。");

            uint fieldToken = 0x04000000u | field.MetadataToken.RID;
            var bytes = new List<byte> { 0x7E }; // ldsfld
            bytes.AddRange(EncodeI4((int)fieldToken));
            int oldLen = endIl - startIl;
            if (bytes.Count > oldLen) throw new InvalidOperationException("替换指令过长。");

            return new PatchChange
            {
                Method = loaderInit,
                StartIl = startIl,
                OldLength = oldLen,
                NewBytes = bytes.ToArray(),
                ExpectedFirstOpcode = 0x72,
                Description = "下载页入口 PageDownloadInstall.LoaderInit：把「关闭下载」loader 换成 ModDownload.DlClientListLoader",
            };
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

        private static void Verify(string path, List<PatchChange> changes)
        {
            var asm = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { ReadSymbols = false, InMemory = true });
            var mod = asm.MainModule;
            var loaderInit = mod.GetType("PCL.PageDownloadInstall").Methods.First(x => x.Name == "LoaderInit" && x.Parameters.Count == 0);
            var ins = loaderInit.Body.Instructions;
            if (ins.Any(x => x.OpCode == OpCodes.Ldstr && (string)x.Operand == "关闭下载"))
                throw new InvalidOperationException("LoaderInit 仍包含受限 loader。");
            if (!ins.Any(x => x.OpCode == OpCodes.Ldsfld && ((FieldReference)x.Operand).Name == "DlClientListLoader"))
                throw new InvalidOperationException("LoaderInit 未指向 DlClientListLoader。");
        }

        private static int Restore(string arg)
        {
            var target = FindPcl(arg);
            if (target == null) { Console.Error.WriteLine("找不到 PCL 启动器。"); return 2; }
            var dir = Path.Combine(Path.GetDirectoryName(target), BackupDirName);
            if (!Directory.Exists(dir)) { Console.Error.WriteLine("找不到备份目录：" + dir); return 2; }
            var current = Sha256(target);
            var candidates = Directory.GetFiles(dir, "*.exe").Where(f => Sha256(f) != current).ToList();
            if (candidates.Count == 0) { Console.WriteLine("没有可用的备份。"); return 2; }
            var backup = candidates.OrderByDescending(File.GetLastWriteTimeUtc).First();
            var temp = target + ".restore-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            File.Copy(backup, temp);
            File.Replace(temp, target, null);
            Console.WriteLine("已还原：" + target);
            Console.WriteLine("还原后 SHA-256：" + Sha256(target));
            return 0;
        }
    }
}
