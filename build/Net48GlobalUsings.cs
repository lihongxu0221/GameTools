// 仅在 net48 目标下编译（由 Directory.Build.props 引入）。
// 目的：net48 不支持 ImplicitUsings，此文件等价复现 .NET SDK 隐式全局 using 集合，
// 使同一份源码可在 net8.0-windows 与 net48 两个目标下编译，且不与 net8 的隐式 using 重复定义。

global using global::System;
global using global::System.Collections.Generic;
global using global::System.IO;
global using global::System.Linq;
global using global::System.Threading;
global using global::System.Threading.Tasks;