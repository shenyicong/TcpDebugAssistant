# TCP 调试助手

基于 C#（.NET 8 / WinForms）的 TCP 网络调试工具，支持服务端 / 客户端双模式、多客户端管理、十六进制收发、循环发送、断线自动重连、收发统计与日志保存。

## 功能特性

- **双模式**：服务端（监听 / 多客户端接入）与客户端（连接指定 IP:端口）
- **多客户端管理**：在线客户端列表、显示在线数量、可断开选中连接
- **文本 / HEX 收发**：支持 GBK / GB2312 / UTF-8 / ASCII 编码
- **循环发送**：可设发送间隔与次数（次数填 0 = 无限循环）
- **断线自动重连**：客户端模式下勾选后每 3 秒尝试重连
- **接收区视图**：列表视图 / 表格视图（时间、方向、长度、ASCII、HEX），带时间戳
- **统计与日志**：实时 TX / RX 字节统计；日志可保存为 TXT / CSV（表格视图下为 CSV）
- **主题切换**：深色 / 浅色主题一键切换

## 运行方式

**方式一：直接运行（推荐）**
下载 [Releases](releases) 中的 `TcpDebugAssistant.exe`，双击即可运行。
该程序为自包含单文件发布，无需在本机安装 .NET 运行时。

**方式二：源码编译**
```bash
dotnet restore
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```
编译产物位于 `发布版/TcpDebugAssistant.exe`。

## 环境要求

- .NET 8（`net8.0-windows`）
- Windows 10 / 11

## 许可证

本项目未指定开源许可证，保留所有权利。
