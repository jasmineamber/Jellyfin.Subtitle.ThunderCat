# ThunderCat Subtitles for Jellyfin

[English](./README.md) | 中文

ThunderCat 是面向 **Jellyfin 10.10.7** 的字幕检索插件，集成 **SubtitleCat** 与 **Thunder（迅雷）** 字幕源。

> ThunderCat 不托管媒体文件或字幕。搜索结果和字幕文件由第三方服务提供。

## 功能

- 通过 SubtitleCat 搜索多语言字幕，并按标题相似度排序。
- 通过 Thunder 搜索字幕，并结合本地文件 CID 改进匹配。
- 使用 `[CID]`、`[ID]` 和 `[TITLE]` 标识高置信度结果。
- 在结果进入 Jellyfin 前移除字幕源内部的精确重复项。
- 优先展示明确符合请求语言的 Thunder 结果。
- 编号检索没有可靠结果时，回退到原始标题。
- 从媒体文件名提取发行编号或目录编号。
- 规范化中文、英文、日文、韩文等常用语言元数据。
- 支持 `srt`、`ass`、`ssa`、`vtt` 字幕格式。
- 可独立启用或关闭 SubtitleCat 与 Thunder。
- 配置页支持连接测试、运行信息和安全诊断信息复制。

## 安全设计

- 使用短期、不可猜测的下载令牌，不在字幕 ID 中暴露原始下载地址。
- 仅访问 HTTPS、明确允许的域名、443 端口和公网地址。
- 每次跳转都会重新校验目标地址，降低 SSRF 风险。
- 网络请求具有单次尝试超时、有限重试、搜索总时限和响应大小限制。
- 下载内容会经过格式检查，拒绝 HTML、JSON 错误页和压缩包伪装。
- 诊断信息不会包含媒体路径、搜索关键词或下载地址。

## 兼容性

| 项目 | 值 |
|---|---|
| 目标框架 | `net8.0` |
| Jellyfin SDK | `10.10.7` |
| 插件 ABI | `10.10.7.0` |
| 目标服务器 | Jellyfin Server `10.10.7` |

本仓库已迁移为 Jellyfin-only。原 Emby 版插件不再由该分支维护。

## 安装

### 通过插件目录安装

1. 打开 **控制台 → 插件 → 插件仓库**。
2. 添加以下仓库地址：

   ```text
   https://raw.githubusercontent.com/jasmineamber/Jellyfin.Subtitle.ThunderCat/dist/manifest.json
   ```

3. 打开 **目录**，选择 **ThunderCat Subtitles**，安装最新版本。
4. 按提示重启 Jellyfin Server。
5. 打开 **控制台 → 插件 → ThunderCat Subtitles**，启用需要的字幕源并保存。
6. 在对应媒体库的 **字幕下载器** 设置中启用 SubtitleCat 和/或 Thunder。

必须使用上面的 raw 清单地址。GitHub 的 `blob` 页面不是 Jellyfin 所需的 JSON 清单。

### 通过 ZIP 安装

1. 从 [Releases](https://github.com/jasmineamber/Jellyfin.Subtitle.ThunderCat/releases) 下载 ZIP。
2. 将 `Jellyfin.Plugin.ThunderCat.dll` 放入 Jellyfin 的 `plugins/thundercat` 目录。
3. 重启 Jellyfin Server。
4. 完成上面的第 5、6 步。

常见插件目录：

- Windows：`%LOCALAPPDATA%\jellyfin\plugins`
- Linux：`/var/lib/jellyfin/plugins`
- Docker：`/config/plugins`

## 配置页

配置页包含：

- SubtitleCat 与 Thunder 全局开关；
- 保存状态和未保存更改提示；
- 两个字幕源的服务端连接测试；
- 插件版本、Jellyfin Server 版本和兼容基线；
- 不包含敏感媒体信息的诊断摘要。

连接测试由 Jellyfin Server 发起，并复用插件现有的域名白名单、HTTPS 和公网地址校验。测试成功只表示上游当前可以访问，不代表某个标题一定有字幕。

## 构建与测试

```powershell
dotnet restore Jellyfin.Plugin.ThunderCat.sln
dotnet test Jellyfin.Plugin.ThunderCat.sln -c Release --warnaserror
dotnet build Jellyfin.Plugin.ThunderCat.csproj -c Release -p:JellyfinVersion=10.10.7
```

编译产物：

```text
bin/Release/net8.0/Jellyfin.Plugin.ThunderCat.dll
```

推送版本 Tag 后，GitHub Actions 会编译并发布 ZIP、创建 GitHub Release，并更新
`dist` 分支中的 Jellyfin 插件仓库清单。

## 已知边界

- SubtitleCat 与 Thunder 都是第三方上游，接口和可用性可能随时变化。
- 上游迁移域名时，需要经过安全审查后更新域名白名单。
- 插件不保证所有媒体都能检索到字幕，也不会绕过上游访问限制。

## 许可证

MIT License © 2026 drunkleee
