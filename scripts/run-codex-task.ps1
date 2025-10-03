param([Parameter(Mandatory=$true)][string]$TaskId)

switch ($TaskId) {

  "T-0001" {
    # 示例：创建 hello 脚本 + workflow（你已经跑通）
    New-Item -ItemType Directory -Force -Path "scripts" | Out-Null
    @'
Write-Output "Hello from codex"
'@ | Set-Content -Encoding UTF8 "scripts\say-hello.ps1"

    New-Item -ItemType Directory -Force -Path ".github\workflows" | Out-Null
    @'
name: hello
on: { workflow_dispatch: {} }
jobs:
  hello:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - name: say Hello
        shell: pwsh
        run: pwsh ./scripts/say-hello.ps1
'@ | Set-Content -Encoding UTF8 ".github\workflows\hello.yml"
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
