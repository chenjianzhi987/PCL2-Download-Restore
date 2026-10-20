# 分析工具（源码，需自行用 csc 编译并引用 Mono.Cecil.dll）

- Ildump.cs      列出全部类型/方法/字段，输出方法 IL 规范化哈希（版本对比用）
- FindStr.cs     搜索引用指定字符串（ldstr）的方法，并列出调用者
- DumpMethod.cs  按类型/方法名导出完整 IL
- Probe.cs       打印程序集模块、资源的引用结构
- FindRef.cs     查找可复用的 MethodReference/FieldReference（跨版本移植用）

编译示例：

```powershell
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $csc /nologo /target:exe /codepage:65001 /out:Ildump.exe /reference:..\vendor\Mono.Cecil.dll Ildump.cs
```
