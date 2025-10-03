param([Parameter(Mandatory=$true)][string]$TaskId)

switch ($TaskId) {

  "T-0001" {
    # 只生成文件，不碰 .github/workflows/**
    New-Item -ItemType Directory -Force -Path "scripts" | Out-Null
    @'
  # PowerShell demo
  Write-Output "Hello from codex"
  '@ | Set-Content -Encoding UTF8 "scripts\say-hello.ps1"
  
    # 可选：加一份说明，方便在 PR 里看到改动
    New-Item -ItemType Directory -Force -Path "docs" | Out-Null
    @"# Hello demo
  This file is created by the PM executor for task T-0001.
  "@ | Set-Content -Encoding UTF8 "docs\hello-demo.md"
  }

  "T-0101" {
    # 你的下一个真实任务放这里：
    # 只改 plan.yaml 里这个任务允许的目录（scope.allow_paths）
    # 例如：生成 src/featureA 的骨架与单测
    New-Item -ItemType Directory -Force -Path "src\featureA" | Out-Null
    @'
export function add(a,b){ return a+b }
'@ | Set-Content -Encoding UTF8 "src\featureA\add.ts"

    New-Item -ItemType Directory -Force -Path "tests\featureA" | Out-Null
    @'
import { add } from "../../src/featureA/add";
console.log(add(1,2) === 3 ? "ok" : "fail");
'@ | Set-Content -Encoding UTF8 "tests\featureA\add.test.ts"
  }

  default {
    Write-Host "No generator implemented for $TaskId"
    exit 2  # 不实现就失败退出，避免误合并
  }
}
