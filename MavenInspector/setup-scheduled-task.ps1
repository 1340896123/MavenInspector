# MavenInspector 开机自启动配置脚本
# 使用方法：以管理员身份运行 PowerShell，执行 .\setup-scheduled-task.ps1

$ErrorActionPreference = "Stop"

# 配置参数
$TaskName = "MavenInspector"
$ExePath = "D:\Source\MavenExplore\MavenInspector\bin\Release\net10.0\MavenInspector.exe"
$WorkingDirectory = "D:\Source\MavenExplore\MavenInspector\bin\Release\net10.0"

Write-Host "正在配置 MavenInspector 开机自启动任务..." -ForegroundColor Cyan

# 检查 exe 文件是否存在
if (-not (Test-Path $ExePath)) {
    Write-Host "错误: 找不到可执行文件: $ExePath" -ForegroundColor Red
    Write-Host "请确保项目已编译 (dotnet build -c Release)" -ForegroundColor Yellow
    exit 1
}

# 删除已存在的同名任务（如果有）
$existingTask = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if ($existingTask) {
    Write-Host "发现已存在的任务，正在删除..." -ForegroundColor Yellow
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
}

# 创建任务触发器：开机时启动
$Trigger = New-ScheduledTaskTrigger -AtStartup

# 创建任务动作：运行 exe
$Action = New-ScheduledTaskAction -Execute $ExePath -WorkingDirectory $WorkingDirectory

# 创建任务主体设置
# - 最高权限运行
# - 不管用户是否登录都运行
# - 不存储密码（使用系统账户）
$Principal = New-ScheduledTaskPrincipal -UserId "SYSTEM" -LogonType ServiceAccount -RunLevel Highest

# 任务设置
# - 允许按需运行
# - 失败后 1 分钟重启
# - 最多重试 3 次
$Settings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -StartWhenAvailable `
    -RestartCount 3 `
    -RestartInterval (New-TimeSpan -Minutes 1)

# 注册任务
try {
    Register-ScheduledTask `
        -TaskName $TaskName `
        -Action $Action `
        -Trigger $Trigger `
        -Principal $Principal `
        -Settings $Settings `
        -Description "MavenInspector MCP Server - Maven 项目依赖分析服务" | Out-Null

    Write-Host "成功创建计划任务!" -ForegroundColor Green
    Write-Host ""
    Write-Host "任务名称: $TaskName" -ForegroundColor White
    Write-Host "可执行文件: $ExePath" -ForegroundColor White
    Write-Host "运行账户: SYSTEM (系统账户)" -ForegroundColor White
    Write-Host ""
    Write-Host "常用命令:" -ForegroundColor Cyan
    Write-Host "  查看任务状态: Get-ScheduledTask -TaskName '$TaskName'" -ForegroundColor Gray
    Write-Host "  手动启动任务: Start-ScheduledTask -TaskName '$TaskName'" -ForegroundColor Gray
    Write-Host "  停止任务: Stop-ScheduledTask -TaskName '$TaskName'" -ForegroundColor Gray
    Write-Host "  删除任务: Unregister-ScheduledTask -TaskName '$TaskName' -Confirm:`$false" -ForegroundColor Gray
    Write-Host ""
    Write-Host "是否现在启动服务? (y/n): " -ForegroundColor Yellow -NoNewline
    $response = Read-Host

    if ($response -eq 'y' -or $response -eq 'Y') {
        Start-ScheduledTask -TaskName $TaskName
        Write-Host "服务已启动!" -ForegroundColor Green
        Write-Host "等待 2 秒后检查状态..." -ForegroundColor Gray
        Start-Sleep -Seconds 2
        Get-ScheduledTaskInfo -TaskName $TaskName
    }
}
catch {
    Write-Host "创建任务失败: $_" -ForegroundColor Red
    exit 1
}
