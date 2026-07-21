# Contributing to ThunderCat Subtitles

## 本地验证

```bash
dotnet restore Emby.Subtitle.ThunderCat.sln
dotnet test Emby.Subtitle.ThunderCat.sln -c Release
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

1. 更新 `Emby.Subtitle.ThunderCat.csproj` 中的 `<Version>`。
2. 更新中英文 README 与发布说明。
3. 合并前等待 CI 的 `4.8.11`、稳定版和 Beta SDK 编译检查与测试通过。
4. 创建与项目版本完全一致的 Tag，例如 `v1.2.0`。
5. Release workflow 会使用最低支持 SDK `4.8.11` 再次构建和测试，然后只打包：

```text
Emby.Subtitle.ThunderCat.dll
```

6. workflow 同时生成 ZIP 的 SHA-256 文件。
7. 发布后在真实 Emby 兼容矩阵执行加载、搜索、下载、取消和升级验证。

禁止直接把 `dotnet publish` 整个目录压入 Release。
