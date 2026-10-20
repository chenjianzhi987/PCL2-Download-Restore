# PCL2 原版下载恢复补丁

针对 **PCL 2.99.0.0**（2026-10-20 发布的正式版）移除的「Minecraft 原版下载」功能做定点修复，
让下载页重新加载版本列表、重新可以安装原版游戏。

- 补丁对象：官方 `Plain Craft Launcher 2.exe`（2.99.0.0，SHA-256 `6b6e1260…7386`）
- 交付形态：单文件 exe + 两个一键 bat，**不需要 Python / SDK / 联网**，只用 Windows 自带的 .NET Framework 4.x
- 修改方式：二进制原地修补（只改下载页初始化方法里的一段指令），**不碰元数据、不移动其它方法**

> ⚠️ **请先读最后的「风险与合规」**。这个补丁恢复的是作者**主动**移除的功能，
> 公开分发可能给作者带来麻烦，建议只作个人/小范围使用。

---

## 一、下载渠道是在哪一步被阻断的（调研结果）

结论先说：**不是网络/接口被封，也不是服务端下发了关停开关，而是 2.99.0.0 在客户端代码里
把「下载页初始化时使用的 LoaderTask」换成了一个会抛异常的替身。**

证据链：

1. **服务端没有关停**。PCL 的配置接口 `pcl2-server-1253424809.file.myqcloud.com/minecraft/download.json`
   现在返回 403，但旧版 2.13.1.1（本机实测）**仍然能正常下载**，说明下载能力不依赖这个文件。
   BMCLAPI 与 Mojang 的版本清单接口也都正常（实测 `version_manifest.json` 均 200）。

2. **2.99.0.0 里新增了限制提示语**。官方 exe（从上游仓库固定提交的 `最新正式版.zip` 解出，
   SHA-256 与官方一致）里出现：
   `为遵守相关法律法规，PCL 暂时不再提供 MC 下载功能，非常抱歉 qwq……`
   全文件只有 5 处引用它。

3. **5 处引用里，真正卡住功能的是下载页入口**。用 Mono.Cecil 反编译后：

   ```
   PCL.PageDownloadInstall::LoaderInit()
     IL_0000: ldarg.0
     IL_0001: call   ControlInit()
     ...
     IL_0013: ldarg.0
     IL_0014: callvirt get_PanAllBack()
     IL_0019: ldnull
     IL_001a: ldstr  "关闭下载"                         ← 受限 loader 的名字
     IL_001f: ldsfld $IR3-3
     ...
     IL_0032: ldftn  _Lambda$__R3-3
     ...
     IL_003d: dup
     IL_003e: stsfld $IR3-3
     IL_0043: ldnull
     IL_0044: ldc.i4.2
     IL_0045: newobj LoaderTask`2::.ctor               ← 临时造一个 loader
     ...
     IL_0058: call   MyPageRight::PageLoaderInit(...)
   ```

   而这个受限 loader 的完成回调是：

   ```vb
   '_Lambda$__3-0
   ldstr "为遵守相关法律法规，PCL 暂时不再提供 MC 下载功能，非常抱歉 qwq……"
   newobj System.Exception::.ctor(string)
   throw
   ```

   对比 2.13.1.1（仍可下载）的同名方法：它把 **共享的 `ModDownload.DlClientListLoader`**
   直接传给 `PageLoaderInit`，没有任何替身 loader：

   ```
   2.13: PageLoaderInit(..., ModDownload::DlClientListLoader, _Lambda$__R2-3, null, 1)
   2.99: PageLoaderInit(..., new LoaderTask("关闭下载", <throw回调>, null, 2), _Lambda$__R3-4, null, 1)
   ```

4. **下载模块本体没有被删**。2.99.0.0 里 `PCL.ModDownload` 的
   `DlClientListLoader` / `DlClientListMojangLoader` / `DlClientListBmclapiLoader` /
   Forge / NeoForge / Fabric / OptiFine / LiteLoader 等 loader 字段与方法**全部还在**，
   `ModDownload` 的静态构造也仍在初始化 `DlClientListLoader`。被拿掉的只是「页面指回它」这一根线。

所以修复点只有一个：**把 `LoaderInit` 里那个替身 loader 换回 `DlClientListLoader`**。

### 另外 4 处引用（本补丁未处理）

| 位置 | 2.99 里的形态 | 影响 |
|---|---|---|
| `PCL.ModModpack::ModpackInstall()` | 整个方法被换成「弹提示后返回」 | 手动安装整合包入口不可用 |
| `PCL.ModModpack::ModpackInstall(3参)` 的 catch 分支 | 兜底异常提示语被换成限制文案 | 只影响错误提示文字 |
| `PCL.FormMain/_Closure$__25-0::_Lambda$__0` | 拖入文件安装整合包时，若异常消息等于限制文案就提示 | 拖入流程本身仍在 |
| `PCL.FormMain::ShowUpdateLog` | 更新日志里的「删除：…」条目 | 仅文本 |

也就是说：**原版游戏下载（本补丁的核心目标）已恢复；整合包手动安装入口仍受限**。

---

## 二、使用方法

1. 关闭 PCL（托盘/任务栏退出，避免文件被占用）。
2. 把官方 **2.99.0.0** 的 `Plain Craft Launcher 2.exe` 放在本目录，或双击 `fix.bat` 后把 exe 拖进去。
3. 运行 `fix.bat`，看到 `SUCCESS` 即可。
4. 启动 PCL → 「下载」→「原版游戏」，版本列表应能正常加载。

命令行等价用法：

```bat
PclDownloadRestore.exe                         自动找同目录的 PCL
PclDownloadRestore.exe "D:\PCL\Plain Craft Launcher 2.exe"
PclDownloadRestore.exe "D:\PCL\Plain Craft Launcher 2.exe" --check    只检查不写文件
PclDownloadRestore.exe --restore "D:\PCL\Plain Craft Launcher 2.exe"  还原官方原版
```

退出码：`0` 修复成功 / `10` 已修复无需改动 / `1` 失败 / `2` 找不到 PCL / `3` 版本不支持 / `5` 复核失败（未改动文件）。

### 还原

补丁会在 PCL 同级目录创建 `PCL-download-restore-backup\<原文件SHA256>.exe`，
运行 `restore.bat`（或 `--restore`）即可逐字节还原官方文件。

---

## 三、实现原理

`LoaderInit` 里那个受限 loader 的构造指令段（IL_001A..IL_004A，48 字节）被替换为：

```
ldsfld PCL.ModDownload::DlClientListLoader   (0x7E + FieldDef token = 6 字节)
+ 42 个 nop                                  (0x00)
```

要点：

- **等长替换**：新序列不足原长度时用 `nop` 填满，因此方法体内后续指令地址完全不变，
  分支偏移无需重算，也把方法体截断/偏移错误的风险降为零。
- **不重写 PE 元数据**：只用 Cecil 读出 `LoaderInit` 的方法体位置与 `DlClientListLoader`
  的 FieldDef RID，然后直接改文件字节；不重新序列化整个程序集，也不触碰其它方法的 IL 与资源。
- **结构校验**：替换前确认区间内没有外部跳转引用、首字节为 `ldstr`、字段确实存在；
  替换后用 Cecil 重新读取校验，失败则删除临时文件、原文件不动。
- **备份 + 原子替换**：先落盘完整备份并校验，再 `File.Replace` 原子替换。

补丁后 `LoaderInit` 与 2.13.1.1 的语义一致（下载页拿到的是真正的共享 loader）。

---

## 四、构建

```powershell
pwsh -File build.ps1
```

需要：Windows 自带的 `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`，
以及 `vendor/Mono.Cecil.dll`（net40，0.11.5）。Mono.Cecil 会被嵌入 exe，
运行时通过 `AppDomain.AssemblyResolve` 从内存加载，所以发布物只有一个 exe。

---

## 五、验证记录

| 项目 | 结果 |
|---|---|
| 输入文件哈希 | 与上游 2.99.0.0 官方 `最新正式版.zip` 内 exe 一致（`6b6e1260…7386`） |
| 补丁后文件字节 diff | 仅 `LoaderInit` 方法体内 48 字节区域变化 |
| 修复后再跑 `--check` | 提示「下载入口已处于启用状态，无需修改」（幂等） |
| `--restore` | 逐字节还原回官方 SHA-256 |
| 补丁版启动日志 | `加载器 LoaderTask\`2 DlClientList Mojang 状态改变：Loading` → 主动请求 `launchermeta.mojang.com/mc/game/version_manifest.json`（原版 2.99 不会走到这一步） |

> 说明：本机测试时 PCL 的缓存目录 `%LOCALAPPDATA%\Temp\PCL\Cache\Http` 被沙箱环境限权，
> 导致网络缓存写入失败（官方原版 2.99 在同一环境下报同样的错），所以未能在本机完成「真正下载完一个版本」。
> 在线下载的最终确认请在你的正常桌面环境（直接双击 PCL）里做。

---

## 六、风险与合规

- PCL 的版权归作者 **龙腾猫跃** 所有；本仓库**不包含 PCL 代码或修改后的 PCL**，
  只提供「在你自己的文件上做本地修改」的工具。
- PCL 2.99.0.0 移除下载功能的原因是**应监管要求/被举报后的合规处置**。
  恢复该功能并公开分发，可能与作者的处境相冲突，也可能让作者再次被追责。
  **建议只作个人备份使用，不要公开传播修改后的启动器本体**。
- 使用本工具造成的任何问题（启动异常、更新失败、账号风险）由使用者自行承担。
- 不适用于「必须在线校验文件哈希」的场合：补丁后 exe 的哈希必然与官方不同。

## 许可

本工具代码以 MIT 许可发布（见 `LICENSE`）；Mono.Cecil 为 MIT（见其仓库）。
「Plain Craft Launcher」「PCL」等名称与代号归原作者所有，本项目与作者无关。
