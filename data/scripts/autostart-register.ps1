param(
	[Parameter(Mandatory)] [string]$TaskName,
	[Parameter(Mandatory)] [string]$ExePath,
	[Parameter(Mandatory)] [string]$Description
)

# Invoked elevated (via runas) by TuringMonitor.Platform.AutostartManager to register a scheduled
# task that launches the app with highest privileges at logon. schtasks.exe /Create has no way to
# set a task Description, so this goes through the ScheduledTasks module instead. ExecutionTimeLimit
# is forced to zero because Task Scheduler kills tasks after 72 hours by default, which would
# silently stop this app.

$ErrorActionPreference = 'Stop'

$action = New-ScheduledTaskAction -Execute $ExePath
$trigger = New-ScheduledTaskTrigger -AtLogOn
$principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -RunLevel Highest -LogonType Interactive
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero)

Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Description $Description -Force | Out-Null
