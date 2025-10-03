param([Parameter(Mandatory=$true)][string]$TaskId)

# === 配置：Secrets/模型 ===
$apiKey   = $env:OPENAI_API_KEY      # 在 repo Settings → Secrets → Actions 配置
$baseUrl  = $env:OPENAI_BASE_URL     # 例如 https://api.openai.com
$model    = $env:OPENAI_MODEL        # 例如 gpt-4o-mini 或你实际可用的模型
if (-not $apiKey -or -not $baseUrl -or -not $model) {
  Write-Error "OPENAI_API_KEY / OPENAI_BASE_URL / OPENAI_MODEL 缺失"; exit 2
}

# === 读取计划：拿 allow_paths、prompt 模板 ===
$planText = Get-Content ".pm/plan.yaml" -Raw
# 用 Python 读 YAML，吐 JSON（避免 PowerShell 装模块）
$planJson = & python - <<'PY' "$planText"
import sys, yaml, json
print(json.dumps(yaml.safe_load(sys.argv[1])))
PY
$plan = $planJson | ConvertFrom-Json
$task = $plan.tasks | Where-Object { $_.id -eq $TaskId }
if (-not $task) { Write-Error "Task $TaskId 不在 plan.yaml 里"; exit 2 }

$allow = @()
if ($task.scope -and $task.scope.allow_paths) { $allow = @($task.scope.allow_paths) }

# 任务 prompt：优先 .pm/templates/task-prompts/{TaskId}.md；否则用 plan.yaml 的 prompt 字段
$promptFile = ".pm/templates/task-prompts/$TaskId.md"
if (Test-Path $promptFile) { $prompt = Get-Content $promptFile -Raw }
elseif ($task.prompt) { $prompt = $task.prompt }
else { Write-Error "Task $TaskId 缺少 prompt"; exit 2 }

# === 组 system & user 提示，要求输出 unified diff ===
$system = @"
You are a senior engineer. Generate a minimal, correct UNIFIED DIFF (git patch)
that applies cleanly on the current repo. Only modify files that match the given
allowlist globs. Do not include explanations. Wrap the patch between lines:
<<<PATCH
...unified diff...
PATCH>>>
"@

$user = @"
TASK: $TaskId

ALLOWED_GLOBS:
$(($allow -join "`n"))

INSTRUCTIONS:
$prompt

REPO CONTEXT HINTS:
- Project is a .NET 7 Teams plugin with webapp dashboard.
- Keep Japanese UI text as default; Chinese overrides allowed.
- Do not touch .github/workflows unless explicitly allowed.
"@

# === 请求 LLM ===
$body = @{
  model = $model
  messages = @(
    @{ role = "system"; content = $system },
    @{ role = "user";   content = $user   }
  )
  temperature = 0
} | ConvertTo-Json -Depth 8

$headers = @{
  "Authorization" = "Bearer $apiKey"
  "Content-Type"  = "application/json"
}

try {
  $resp = Invoke-RestMethod -Uri "$baseUrl/v1/chat/completions" -Method Post -Headers $headers -Body $body
} catch {
  Write-Error "调用模型失败：$($_.Exception.Message)"; exit 2
}

# === 提取补丁 ===
$content = $resp.choices[0].message.content
if (-not $content) { Write-Error "模型返回为空"; exit 2 }
$patch = [regex]::Match($content, "(?s)<<<PATCH\s*(.+?)\s*PATCH>>>").Groups[1].Value
if (-not $patch) { Write-Error "未找到 PATCH 区块"; exit 2 }

New-Item -ItemType Directory -Force -Path ".pm/patches" | Out-Null
$patchPath = ".pm/patches/$TaskId.patch"
$patch | Set-Content -Encoding UTF8 $patchPath

# === 预检：只允许修改 allow_paths ===
git apply --numstat $patchPath | ForEach-Object {
  $parts = $_ -split "`t"
  if ($parts.Length -ge 3) {
    $file = $parts[2]
    $ok = $false
    foreach ($g in $allow) { if ([bool](git ls-files "$g" -- "$file")) { $ok = $true; break } }
    if (-not $ok) {
      Write-Error "越界修改：$file 不匹配 allow_paths"; exit 3
    }
  }
}

# === 应用补丁（进索引） ===
git apply --index --whitespace=fix $patchPath || (Write-Error "git apply 失败"; exit 2)

Write-Host "Patch applied: $patchPath"
