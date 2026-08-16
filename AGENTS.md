# AGENTS.md

本文件是本仓库中自动化开发代理的统一工作说明，适用于整个仓库。若子目录以后出现更具体的 `AGENTS.md`，则以离目标文件最近的说明为准。

## 项目概览

这是一个基于 ASP.NET Core 和 Blazor Server 的 WebDAV 服务，目标框架为 .NET 10。解决方案包含三个项目：

- `webdav/`：主应用。负责配置加载、Basic Authentication、权限控制、Blazor 文件管理界面、缩略图/图片接口、服务托管及 WebDAV 管道组装。
- `NWebDav.Server/`：仓库内的 WebDAV 协议实现与磁盘存储库，包含各 HTTP/WebDAV 方法处理器、属性、锁和存储抽象。
- `testwebdav/`：基于 `WebDav.Client` 的命令行冒烟测试程序；它不是单元测试项目，需要连接已启动的 WebDAV 服务。

主要入口是 `webdav/Program.cs`。默认情况下，Web UI 位于 `/`，WebDAV 端点位于配置项 `WebDav:Prefix`（默认 `/dav`）。

## 信息来源与文档现状

- 以当前代码、项目文件和 `webdav.slnx` 为事实来源；文档描述与代码冲突时，以代码为准，并在改动相关区域时同步修正文档。
- `webdav/README.md` 和 `webdav/QUICKSTART.md` 是面向使用者的主要文档。
- `webdav/PROJECT_SUMMARY.md` 含有部分过时的目录、依赖和目标框架信息，不应未经核对直接引用。
- 旧说明提到的 `refs/webdav` 当前不在仓库中，不要假定该目录或 Go 参考实现可用。

## 常用命令

从仓库根目录执行：

```powershell
# 还原并构建整个解决方案
dotnet restore .\webdav.slnx
dotnet build .\webdav.slnx

# 启动开发环境
dotnet run --project .\webdav\webdav.csproj --environment Development

# Release 构建
dotnet build .\webdav.slnx -c Release

# 服务启动后运行命令行冒烟测试
dotnet run --project .\testwebdav\testwebdav.csproj -- http://localhost:6065/dav/ admin admin

# 构建容器镜像
docker build -t webdav-dotnet -f .\webdav\Dockerfile .
```

仓库目前没有基于 xUnit、NUnit 或 MSTest 的自动化测试项目，因此不要把 `dotnet test` 成功但未执行测试当成充分验证。涉及协议、认证、权限或文件操作的改动，应尽可能启动服务并做针对性的 HTTP/WebDAV 冒烟测试。

## 架构与改动边界

请求路径大致如下：

1. `webdav/Program.cs` 加载 `WebDav` 配置并注册服务。
2. 非 WebDAV 路径经过 Basic Authentication 后进入 Blazor UI 和图片接口。
3. `WebDav:Prefix` 下的请求依次经过路径前缀重写、Basic Authentication、权限中间件和 `NWebDav.Server`。
4. 单目录模式使用 `DiskStore`；配置 `RootPoints` 后使用 `MultiRootDiskStore`。

修改时遵守以下职责边界：

- 协议方法、DAV 属性、锁或通用存储行为放在 `NWebDav.Server/`。
- 应用配置、用户解析、授权策略、UI 与应用托管逻辑放在 `webdav/`。
- 权限变更需同时考虑源路径和 `COPY`/`MOVE` 的目标路径。
- 路径处理需同时覆盖单目录、多根目录、前缀重写、URL 编码和目录边界，不能只按字符串前缀判断文件系统归属。
- 修改中间件顺序前先检查 `Program.cs`；认证、路径重写和权限检查的先后会影响安全性与路由结果。

## 编码约定

- 遵循现有 C# 风格：启用 nullable reference types 和 implicit usings，使用 4 空格缩进，类型和公开成员使用 PascalCase，局部变量和参数使用 camelCase。
- 优先使用异步 API，并在已有调用链支持时传递 `CancellationToken`；避免在请求路径中使用 `.Result`、`.Wait()` 或不必要的同步文件 I/O。
- 保持改动聚焦，不顺手重排无关代码、批量改换行符或覆盖用户已有修改。
- 添加新配置项时，同步更新 `Models/WebDavConfig.cs`、相关 `appsettings*.json` 示例和用户文档，并为缺省值及无效输入定义明确行为。
- 面向协议的响应应使用正确的 HTTP/WebDAV 状态码、头和 XML 命名空间；不要用普通成功响应掩盖失败。
- 静态第三方资源位于 `webdav/wwwroot/vendor/`，除非明确升级依赖，否则不要直接编辑生成或压缩后的 vendor 文件。

## 安全与配置

- 不提交真实用户名、明文生产密码、证书私钥、访问令牌或机器专属绝对路径。
- 开发配置中的 `admin/admin` 仅用于本地测试；新增示例应明确其非生产用途。
- 敏感配置优先沿用 `{env}VARIABLE_NAME` 机制。日志不得记录密码、Authorization 头或完整凭据。
- 认证、权限、路径规范化、代理头、TLS、CORS、文件读取和上传相关改动均按安全敏感改动处理。
- `BehindProxy` 会影响对转发头的信任；不要在未明确代理边界时扩大可信范围。
- 文件系统路径必须规范化并验证仍位于授权根目录内，特别注意 `..`、编码分隔符、符号链接以及 `COPY`/`MOVE` 的 `Destination`。

## 验证要求

按改动范围采用最小但充分的验证：

- 所有代码改动：至少执行 `dotnet build .\webdav.slnx`。
- `NWebDav.Server/` 改动：验证相关 WebDAV 方法，并检查失败状态和边界路径。
- 认证或权限改动：至少覆盖正确凭据、错误/缺失凭据、只读用户写入失败，以及 `COPY`/`MOVE` 目标权限。
- 存储改动：同时验证单目录与 `RootPoints` 模式。
- UI 或静态资源改动：启动应用，检查页面加载、登录、文件浏览及相关浏览器控制台/服务端错误。
- 配置或部署改动：验证 Development 与 Production 的默认行为；涉及 Docker 时额外构建镜像。

如果受环境限制无法完成运行时验证，应在交付说明中准确列出已执行和未执行的检查，不能笼统声称“测试通过”。

## 完成改动前

1. 查看 `git diff`，确认只有任务相关变更，且没有覆盖用户工作。
2. 构建受影响项目或整个解决方案。
3. 执行与风险匹配的冒烟测试。
4. 更新因本次改动而失真的 README、快速开始或配置示例。
5. 在交付说明中概述行为变化、验证结果和仍存在的限制。
