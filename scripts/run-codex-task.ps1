param([Parameter(Mandatory=$true)][string]$TaskId)

function Ensure-Dir($path) {
  $dir = Split-Path -Parent $path
  if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
}

if ($TaskId -eq "T-0001") {
  # 只生成演示文件，不修改 .github/workflows/**
  Ensure-Dir "scripts\say-hello.ps1"
  @'
# PowerShell demo
Write-Output "Hello from codex"
'@ | Set-Content -Encoding UTF8 -Force "scripts\say-hello.ps1"

  Ensure-Dir "docs\hello-demo.md"
  @"# Hello demo
This file is created by the PM executor for task T-0001.
"@ | Set-Content -Encoding UTF8 -Force "docs\hello-demo.md"
}
elseif ($TaskId -eq "T-0101") {
  # 你的后续真实任务（示例）
  Ensure-Dir "src\featureA\add.ts"
  @'
export function add(a,b){ return a+b }
'@ | Set-Content -Encoding UTF8 -Force "src\featureA\add.ts"

  Ensure-Dir "tests\featureA\add.test.ts"
  @'
import { add } from "../../src/featureA/add";
console.log(add(1,2) === 3 ? "ok" : "fail");
'@ | Set-Content -Encoding UTF8 -Force "tests\featureA\add.test.ts"
}
else {
  Write-Host "No generator implemented for $TaskId"
  exit 2
}
