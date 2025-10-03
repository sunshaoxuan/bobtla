param([Parameter(Mandatory=$true)][string]$TaskId)

function Write-FileUtf8([string]$path, [string]$content) {
  $dir = [System.IO.Path]::GetDirectoryName($path)
  if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
  # 带 UTF-8 BOM，避免少数工具乱码；不想要 BOM 可把 ($true) 改成 ($false)
  $enc = New-Object System.Text.UTF8Encoding($true)
  [System.IO.File]::WriteAllText($path, $content, $enc)
}

if ($TaskId -eq "T-0001") {
  # 只生成演示文件，不动 .github/workflows/**
  Write-FileUtf8 "scripts\say-hello.ps1" @'
# PowerShell demo
Write-Output "Hello from codex"
'@

  Write-FileUtf8 "docs\hello-demo.md" @'
# Hello demo
This file is created by the PM executor for task T-0001.
'@
}
elseif ($TaskId -eq "T-0101") {
  # 示例：真实任务的占位（你后面按需改成你的逻辑）
  Write-FileUtf8 "src\featureA\add.ts" @'
export function add(a,b){ return a+b }
'@
  Write-FileUtf8 "tests\featureA\add.test.ts" @'
import { add } from "../../src/featureA/add";
console.log(add(1,2) === 3 ? "ok" : "fail");
'@
}
else {
  Write-Host "No generator implemented for $TaskId"
  exit 2
}
