# 500 错误排查指南

## 可能的原因

### 1. 目录创建失败
**位置**: `WebDavRequestHandler.cs` 第 36-45 行
**日志**: `Failed to create directory: {Directory}`
**原因**: 
- 权限不足
- 磁盘空间不足
- 路径包含非法字符

### 2. NWebDav Dispatcher 异常
**位置**: `WebDavRequestHandler.cs` 第 92 行
**日志**: `Unexpected error handling WebDAV request: {Method} {Path}`
**原因**:
- NWebDav 库内部错误
- 文件系统访问错误
- 并发访问冲突

### 3. Permission Middleware 异常
**位置**: `WebDavPermissionMiddleware.cs` 第 96 行
**日志**: `Unexpected error in WebDavPermissionMiddleware for path: {Path}`
**原因**:
- 权限检查逻辑错误
- 文件路径解析失败
- 空引用异常

### 4. 文件系统访问错误
**原因**:
- 文件被其他进程锁定
- 符号链接指向不存在的目标
- 权限问题

## 排查步骤

### 1. 启用详细日志
在 `appsettings.json` 或 `appsettings.Development.json` 中：

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft": "Information",
      "Microsoft.AspNetCore": "Warning",
      "WebDav": "Debug"
    }
  }
}
```

### 2. 查看日志输出
运行应用后，查看控制台输出，关注以下关键字：
- `LogError`
- `Failed to create directory`
- `Unexpected error`
- `Permission denied`
- 异常堆栈跟踪

### 3. 检查文件系统权限
确保运行应用的用户有权限：
- 读取配置的目录
- 创建新目录和文件
- 修改和删除文件

### 4. 测试特定场景
使用日志中的信息，复现具体请求：
```bash
# 例如，如果是 PROPFIND 请求失败
curl -X PROPFIND http://localhost:5000/webdav/ \
  -u username:password \
  -H "Depth: 1"
```

### 5. 检查 NWebDav 兼容性
可能的问题：
- .NET 9 兼容性问题
- DiskStore 路径问题
- 特殊字符处理

## 常见解决方案

### 1. 权限问题
```bash
# Linux/Mac
chmod -R 755 /path/to/webdav/directory
chown -R user:group /path/to/webdav/directory

# Windows - 以管理员身份运行或检查文件夹权限
```

### 2. 路径问题
检查 `appsettings.json`:
```json
{
  "WebDav": {
    "Directory": "./webdav-data",  // 使用相对路径
    "Prefix": "/webdav"
  }
}
```

### 3. 用户目录问题
确保用户配置的目录存在或可创建：
```json
{
  "WebDav": {
    "Users": [
      {
        "Username": "user1",
        "Directory": "./user1-data"  // 确保此路径可访问
      }
    ]
  }
}
```

## 需要提供的信息

如果需要进一步帮助，请提供：

1. **完整的错误日志**（包括异常堆栈）
2. **请求详情**：
   - HTTP 方法（GET, PUT, PROPFIND 等）
   - 请求路径
   - 用户名（如果适用）
3. **环境信息**：
   - 操作系统
   - .NET 版本
   - 是否在容器中运行
4. **配置文件**（隐藏敏感信息）

## 添加诊断代码

如果问题持续，可以临时添加更详细的日志：

```csharp
// 在 WebDavRequestHandler.cs 的 DispatchRequestAsync 调用前后
_logger.LogInformation("About to dispatch: Method={Method}, Path={Path}, Directory={Dir}", 
    context.Request.Method, context.Request.Path, directory);

try 
{
    await dispatcher.DispatchRequestAsync(webDavContext);
}
catch (Exception ex)
{
    _logger.LogError(ex, "Dispatcher failed - Type: {ExceptionType}, Message: {Message}, StackTrace: {StackTrace}", 
        ex.GetType().FullName, ex.Message, ex.StackTrace);
    throw;
}
```
