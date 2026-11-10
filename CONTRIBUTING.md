# Contributing to ThunderCat Subtitles

## 本地验证

```bash
dotnet restore Jellyfin.Plugin.ThunderCat.sln
dotnet test Jellyfin.Plugin.ThunderCat.sln -c Release --warnaserror
```

提交前必须保证：

- Release 构建无警告；
- 全部单元测试通过；
- 不新增 raw URL 字幕 ID；
- 不放宽 URL 白名单而缺少测试与风险说明；
- 不把 MediaBrowser、System 或 Microsoft 宿主 DLL 放进发布包。

## 提交信息

建议使用中文说明问题与方案，例如：

```text
fix: 修复迅雷响应解析失效

问题或需求描述：正常的 code=0 响应被误判为空结果
修复或实现思路：改用强类型 JSON 模型并补充协议 fixture 测试
复现路径：搜索任意有迅雷字幕的媒体
```

## 发布流程

1. 更新 `Jellyfin.Plugin.ThunderCat.csproj` 与 `build.yaml` 中的版本。
2. 更新中英文 README 与发布说明。
3. 合并前等待 Jellyfin `10.10.7` 的 Release 编译检查和全部测试通过。
4. 创建与项目版本完全一致的 Tag，例如 `v2.0.2`。
5. Release workflow 会使用 Jellyfin SDK `10.10.7` 再次构建和测试，然后只打包：

```text
Jellyfin.Plugin.ThunderCat.dll
```

6. workflow 同时生成 ZIP 的 SHA-256 文件、创建 GitHub Release，并更新 `dist`
   分支中的 `manifest.json`。
7. Jellyfin 插件仓库地址为：

```text
https://raw.githubusercontent.com/jasmineamber/Jellyfin.Subtitle.ThunderCat/dist/manifest.json
```

8. 发布后在真实 Jellyfin `10.10.7` 执行插件加载、provider 识别、搜索、下载、取消和升级验证。

禁止直接把 `dotnet publish` 整个目录压入 Release。
