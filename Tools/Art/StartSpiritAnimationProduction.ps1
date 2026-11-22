$ErrorActionPreference = 'Stop'
$taskWorkspace = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$taskOutput = Join-Path $taskWorkspace 'artifacts/pixellab/SpiritAnimationProduction'
if (Test-Path -LiteralPath (Join-Path $taskOutput 'STOP')) {
    throw '작업 중지 표시가 있습니다. 재개하기 전에 중지 사유를 확인하세요.'
}
$taskNode = (Get-Command node -ErrorAction Stop).Source
$taskDependencies = Split-Path (Split-Path (Split-Path $taskNode))
$taskPython = Join-Path $taskDependencies 'python/python.exe'
if (-not (Test-Path -LiteralPath $taskPython)) {
    throw 'Codex에 포함된 실행 도구를 찾지 못했습니다. Codex에서 이 작업의 재개를 요청하세요.'
}
$taskProcess = Start-Process -FilePath $taskPython -WorkingDirectory $taskWorkspace -WindowStyle Hidden `
    -ArgumentList @('Tools/Art/pixellab_spirit_batch.py', 'run') `
    -RedirectStandardOutput (Join-Path $taskOutput 'Worker.log') `
    -RedirectStandardError (Join-Path $taskOutput 'WorkerErrors.log') -PassThru
Write-Output ('원혼 애니메이션 순차 제작 시작. 진행 기록: ' + (Join-Path $taskOutput 'Progress.md'))
Write-Output ('작업 번호: ' + $taskProcess.Id)
