# Stage5SmokeTests 自测指南

本文档说明如何验证 `reply` 命令在默认离线模式与新增的 `--use-remote-api` 模式下均能正常运行，以便在修改脚本后快速自测。

## 前置准备

1. 安装 .NET 8 SDK，并在仓库根目录执行 `dotnet restore`。
2. 若需要演示远程模式，请在新的终端窗口启动本地 API：
   ```bash
   dotnet run --project src/TlaPlugin/TlaPlugin.csproj --urls https://localhost:5001
   ```
   服务器会监听 `https://localhost:5001`，使用默认自签名证书。首次访问时可接受证书提示。

## 离线模式验证

该模式复用脚本内置的 Stub Provider 与模拟 Graph，确保历史流程不受影响：

```bash
dotnet run --project scripts/SmokeTests/Stage5SmokeTests -- reply \
  --tenant contoso.onmicrosoft.com \
  --user smoke-user \
  --thread smoke-thread \
  --language ja \
  --text "Stage 5 离线验证"
```

预期输出包括：

- 控制台提示“已生成模拟 JWT”；
- `TeamsReplyClient 调用成功` 以及 Graph 调用诊断；
- 指标摘要与审计样例。若命令返回非零退出码，则离线流程被破坏，应先回归。

## 远程模式验证

1. 确保上文提到的本地 API 已启动，并可通过 `curl -k https://localhost:5001/healthz` 获得 200。
2. 使用以下一次性脚本生成与工具内部逻辑一致的模拟用户断言（若已有真实用户断言，可跳过本步并替换后续命令中的变量）：
   ```bash
   export ASSERTION=$(python - <<'PY'
import base64, json, time
header = base64.urlsafe_b64encode(b'{"alg":"none","typ":"JWT"}').rstrip(b'=')
payload = {
    "aud": "api://tla-plugin",
    "tid": "contoso.onmicrosoft.com",
    "sub": "smoke-user",
    "upn": "smoke-user",
    "iss": "Stage5SmokeTests",
    "iat": int(time.time()),
    "exp": int(time.time() + 1800),
    "ver": "1.0"
}
payload_json = json.dumps(payload, separators=(',', ':')).encode()
payload_segment = base64.urlsafe_b64encode(payload_json).rstrip(b'=')
signature = base64.urlsafe_b64encode(b'stage5-smoke').rstrip(b'=')
print(f"{header.decode()}.{payload_segment.decode()}.{signature.decode()}")
PY
)
   ```
3. 执行远程模式命令：
   ```bash
   dotnet run --project scripts/SmokeTests/Stage5SmokeTests -- reply \
     --tenant contoso.onmicrosoft.com \
     --user smoke-user \
     --thread smoke-thread \
     --language ja \
     --text "Stage 5 远程验证" \
     --use-remote-api \
     --assertion "$ASSERTION"
   ```

预期输出：

- `远程 API 模式已启用` 提示以及翻译、回帖响应摘要；
- `/api/metrics` 与 `/api/audit` 的 JSON 片段，且包含当前租户的记录；
- 若远程接口返回 401/403/429，会打印对应的退出码（21/22/23），便于快速定位权限或配额问题。

## 常见问题排查

- **证书错误**：本地 HTTPS 服务器使用自签名证书，可在命令中追加 `DOTNET_SYSTEM_NET_HTTP_USESOCKETSHTTPHANDLER=0` 环境变量或改用 `http://localhost:5000` 自定义端口。
- **返回 401/403**：检查 `--assertion` 是否为有效 JWT，以及部署环境是否识别该用户；脚本会同时输出退出码映射帮助定位。
- **返回 429**：说明服务端触发速率限制，可稍后重试或调整负载；远程模式会直接展示服务端返回的正文。

按照上述步骤可快速确认新增开关没有破坏原有流程，并在需要时模拟部署环境的远程调用链路。
