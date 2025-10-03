param([Parameter(Mandatory=$true)][string]$TaskId)

# 读取 plan.yaml（简单粗暴：不做 YAML 解析，按 TaskId 判断）
if ($TaskId -eq "T-0001") {
  New-Item -ItemType Directory -Force -Path "scripts" | Out-Null
  @"# PowerShell demo
Write-Output 'Hello from codex'
"@ | Set-Content -Encoding UTF8 "scripts/say-hello.ps1"

  New-Item -ItemType Directory -Force -Path ".github/workflows" | Out-Null
  @"name: hello
on: { workflow_dispatch: {} }
jobs:
  hello:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - name: say Hello
        run: pwsh ./scripts/say-hello.ps1
"@ | Set-Content -Encoding UTF8 ".github/workflows/hello.yml"
} else {
  Write-Host "No generator implemented for $TaskId"
  exit 2
}
