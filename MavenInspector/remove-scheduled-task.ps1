# MavenInspector 开机自启动卸载脚本
# 使用方法：以管理员身份运行 PowerShell，执行 .\remove-scheduled-task.ps1

$ErrorActionPreference = "Stop"
$TaskName = "MavenInspector"

$existingTask = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if ($existingTask) {
    Write-Host "正在删除计划任务 '$TaskName'..." -ForegroundColor Yellow
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
    Write-Host "任务已删除!" -ForegroundColor Green
} else {
    Write-Host "未找到任务 '$TaskName'" -ForegroundColor Yellow
}
