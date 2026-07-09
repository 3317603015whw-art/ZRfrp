# ZRfrp v1.0.0

首个开源版本。当前版本为 Windows 桌面端，后续可继续扩展其他平台客户端。

## 功能

- 可视化管理多个 frp 节点。
- 创建、编辑、启用、停用、删除 frp 隧道。
- 支持 `tcp`、`udp`、`http`、`https` 隧道配置。
- 自动生成 `frpc.toml`，并在启动前调用 `frpc verify` 校验配置。
- 启动、停止并监控 `frpc.exe` 进程。
- 彩色运行日志，隧道启动成功后显示可连接地址并支持点击复制。
- 节点 TCP 延迟测速，支持启动时自动测速和手动一键测速。
- 托盘常驻，支持后台运行、恢复主界面、彻底退出和通道开关。
- 暗色无边框界面，支持浮动设置/编辑窗口。
- 自动检测、选择或下载安装本机 `frpc.exe`。
- 软件联网代理设置：系统代理、不使用代理、手动代理。

## 运行环境

- Windows 10/11
- .NET 8 Desktop Runtime
- frp/frpc 0.69.x

## 发布包

Windows x64 发布包可由以下命令生成：

```powershell
dotnet publish FrpDesktop\FrpDesktop.csproj -c Release -r win-x64 --self-contained false
```
