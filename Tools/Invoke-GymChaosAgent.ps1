[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('init', 'status', 'add', 'claim', 'submit', 'feed', 'reject', 'block', 'requeue', 'report', 'watch')]
    [string]$Command,

    [ValidateSet('worker', 'executor', 'system')]
    [string]$Role = '',

    [string]$Actor = '',
    [string]$TaskId = '',
    [string]$PayloadPath = '',
    [string]$NextPayloadPath = '',
    [string]$ReportPath = '',
    [string]$Feedback = '',
    [string]$Title = '',
    [string]$Instructions = '',
    [string[]]$AcceptanceCriteria = @(),
    [string[]]$Checks = @(),
    [ValidateSet('critical', 'high', 'normal', 'low')]
    [string]$Priority = 'normal',
    [string]$NextId = '',
    [string]$NextTitle = '',
    [string]$NextInstructions = '',
    [string[]]$NextAcceptanceCriteria = @(),
    [string[]]$NextChecks = @(),
    [ValidateSet('critical', 'high', 'normal', 'low')]
    [string]$NextPriority = 'normal',
    [int]$TimeoutSeconds = 300,
    [switch]$Json
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$agentRoot = Join-Path $repositoryRoot '.agents'
$runtimeRoot = Join-Path $agentRoot 'runtime'
$reportsRoot = Join-Path $runtimeRoot 'reports'
$queuePath = Join-Path $runtimeRoot 'queue.json'
$lockPath = Join-Path $runtimeRoot 'queue.lock'

function Get-UtcTimestamp
{
    return [DateTime]::UtcNow.ToString('o')
}

function Resolve-ProjectPath
{
    param([Parameter(Mandatory = $true)][string]$Path)

    if ([IO.Path]::IsPathRooted($Path))
    {
        return $Path
    }

    return Join-Path $repositoryRoot $Path
}

function Ensure-Role
{
    param(
        [Parameter(Mandatory = $true)][string]$Expected,
        [Parameter(Mandatory = $true)][string]$Operation
    )

    if ($Role -ne $Expected)
    {
        throw "Operation '$Operation' requires -Role $Expected."
    }
}

function Ensure-Queue
{
    if (-not (Test-Path -LiteralPath $queuePath))
    {
        throw "Queue is not initialized. Run: .\Tools\Invoke-GymChaosAgent.ps1 -Command init"
    }
}

function Read-Queue
{
    Ensure-Queue
    $raw = Get-Content -LiteralPath $queuePath -Raw
    $state = $raw | ConvertFrom-Json
    if ($null -eq $state.tasks)
    {
        $state.tasks = @()
    }
    else
    {
        $state.tasks = @($state.tasks)
    }
    return $state
}

function Write-Queue
{
    param([Parameter(Mandatory = $true)]$State)

    $State.updatedAt = Get-UtcTimestamp
    $jsonText = $State | ConvertTo-Json -Depth 30
    $temporaryPath = "$queuePath.$PID.tmp"
    Set-Content -LiteralPath $temporaryPath -Value $jsonText -Encoding UTF8
    Move-Item -LiteralPath $temporaryPath -Destination $queuePath -Force
}

function Invoke-QueueMutation
{
    param([Parameter(Mandatory = $true)][scriptblock]$Mutation)

    Ensure-Queue
    if (-not (Test-Path -LiteralPath $runtimeRoot))
    {
        New-Item -ItemType Directory -Path $runtimeRoot -Force | Out-Null
    }

    $deadline = (Get-Date).AddSeconds(30)
    $lockStream = $null
    while ($null -eq $lockStream)
    {
        try
        {
            $lockStream = [IO.File]::Open(
                $lockPath,
                [IO.FileMode]::OpenOrCreate,
                [IO.FileAccess]::ReadWrite,
                [IO.FileShare]::None)
        }
        catch [IO.IOException]
        {
            if ((Get-Date) -ge $deadline)
            {
                throw "Timed out waiting for queue lock: $lockPath"
            }
            Start-Sleep -Milliseconds 150
        }
    }

    try
    {
        $state = Read-Queue
        $result = & $Mutation $state
        Write-Queue -State $state
        return $result
    }
    finally
    {
        if ($null -ne $lockStream)
        {
            $lockStream.Dispose()
        }
    }
}

function Get-StringArray
{
    param($Value)

    if ($null -eq $Value)
    {
        return @()
    }

    return @($Value | ForEach-Object { [string]$_ } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

function Get-PriorityRank
{
    param([string]$Value)

    switch ($Value)
    {
        'critical' { return 0 }
        'high' { return 1 }
        'normal' { return 2 }
        'low' { return 3 }
        default { return 2 }
    }
}

function Add-HistoryEntry
{
    param(
        [Parameter(Mandatory = $true)]$Task,
        [string]$From,
        [Parameter(Mandatory = $true)][string]$To,
        [string]$ActorName,
        [string]$Note
    )

    $entry = [PSCustomObject]@{
        at = Get-UtcTimestamp
        actor = $ActorName
        from = $From
        to = $To
        note = $Note
    }
    $Task.history = @($Task.history) + @($entry)
    $Task.updatedAt = $entry.at
}

function Get-TaskById
{
    param(
        [Parameter(Mandatory = $true)]$State,
        [Parameter(Mandatory = $true)][string]$Id
    )

    $matches = @($State.tasks | Where-Object { $_.id -eq $Id })
    if ($matches.Count -eq 0)
    {
        throw "Task not found: $Id"
    }
    if ($matches.Count -gt 1)
    {
        throw "Duplicate task id in queue: $Id"
    }
    return $matches[0]
}

function New-TaskObject
{
    param(
        [string]$InputPath,
        [string]$InputId,
        [string]$InputTitle,
        [string]$InputInstructions,
        [string[]]$InputAcceptanceCriteria,
        [string[]]$InputChecks,
        [string]$InputPriority
    )

    $payload = $null
    if (-not [string]::IsNullOrWhiteSpace($InputPath))
    {
        $resolvedPayloadPath = Resolve-ProjectPath -Path $InputPath
        if (-not (Test-Path -LiteralPath $resolvedPayloadPath))
        {
            throw "Task payload not found: $resolvedPayloadPath"
        }
        $payload = (Get-Content -LiteralPath $resolvedPayloadPath -Raw) | ConvertFrom-Json
    }

    $id = if ($payload -and $payload.id) { [string]$payload.id } else { $InputId }
    $title = if ($payload -and $payload.title) { [string]$payload.title } else { $InputTitle }
    $taskInstructions = if ($payload -and $payload.instructions) { [string]$payload.instructions } else { $InputInstructions }
    $acceptance = if ($payload -and $null -ne $payload.acceptanceCriteria) { Get-StringArray $payload.acceptanceCriteria } else { Get-StringArray $InputAcceptanceCriteria }
    $taskChecks = if ($payload -and $null -ne $payload.checks) { Get-StringArray $payload.checks } else { Get-StringArray $InputChecks }
    $scope = if ($payload -and $null -ne $payload.scope) { Get-StringArray $payload.scope } else { @() }
    $constraints = if ($payload -and $null -ne $payload.constraints) { Get-StringArray $payload.constraints } else { @() }
    $priority = if ($payload -and $payload.priority) { [string]$payload.priority } else { $InputPriority }

    if ([string]::IsNullOrWhiteSpace($id))
    {
        $id = 'GYM-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff')
    }
    if ([string]::IsNullOrWhiteSpace($title))
    {
        throw 'A task title is required.'
    }
    if ([string]::IsNullOrWhiteSpace($taskInstructions))
    {
        throw 'Task instructions are required.'
    }
    if ($acceptance.Count -eq 0)
    {
        throw "Task '$id' needs at least one acceptance criterion."
    }
    if ($priority -notin @('critical', 'high', 'normal', 'low'))
    {
        throw "Unsupported priority '$priority'. Use critical, high, normal or low."
    }

    $timestamp = Get-UtcTimestamp
    return [PSCustomObject]@{
        id = $id
        title = $title
        instructions = $taskInstructions
        acceptanceCriteria = @($acceptance)
        checks = @($taskChecks)
        scope = @($scope)
        constraints = @($constraints)
        priority = $priority
        status = 'ready'
        attempts = 0
        claimedBy = $null
        claimedAt = $null
        createdAt = $timestamp
        updatedAt = $timestamp
        workerFeedback = $null
        report = $null
        history = @([PSCustomObject]@{
            at = $timestamp
            actor = if ($Actor) { $Actor } else { 'worker' }
            from = $null
            to = 'ready'
            note = 'Task created.'
        })
    }
}

function Write-Result
{
    param($Value)

    if ($Json)
    {
        $Value | ConvertTo-Json -Depth 30
        return
    }

    if ($Value -is [string])
    {
        Write-Output $Value
        return
    }

    $Value | Format-List | Out-String | Write-Output
}

switch ($Command)
{
    'init'
    {
        if (-not (Test-Path -LiteralPath $runtimeRoot))
        {
            New-Item -ItemType Directory -Path $runtimeRoot -Force | Out-Null
        }
        if (-not (Test-Path -LiteralPath $reportsRoot))
        {
            New-Item -ItemType Directory -Path $reportsRoot -Force | Out-Null
        }

        if (Test-Path -LiteralPath $queuePath)
        {
            Write-Result ([PSCustomObject]@{ status = 'already_initialized'; queue = $queuePath })
            break
        }

        $timestamp = Get-UtcTimestamp
        $initialState = [PSCustomObject]@{
            schemaVersion = 1
            project = 'GymChaos'
            createdAt = $timestamp
            updatedAt = $timestamp
            tasks = @()
        }
        Write-Queue -State $initialState
        Write-Result ([PSCustomObject]@{ status = 'initialized'; queue = $queuePath })
        break
    }

    'status'
    {
        $state = Read-Queue
        $summary = [PSCustomObject]@{
            project = $state.project
            updatedAt = $state.updatedAt
            total = @($state.tasks).Count
            ready = @($state.tasks | Where-Object { $_.status -eq 'ready' }).Count
            running = @($state.tasks | Where-Object { $_.status -eq 'running' }).Count
            awaitingWorker = @($state.tasks | Where-Object { $_.status -eq 'awaiting_worker' }).Count
            blocked = @($state.tasks | Where-Object { $_.status -eq 'blocked' }).Count
            done = @($state.tasks | Where-Object { $_.status -eq 'done' }).Count
            tasks = @($state.tasks | Select-Object id, title, priority, status, attempts, claimedBy, updatedAt)
        }
        Write-Result $summary
        break
    }

    'add'
    {
        Ensure-Role -Expected 'worker' -Operation 'add'
        $newTask = New-TaskObject -InputPath $PayloadPath -InputId $TaskId -InputTitle $Title -InputInstructions $Instructions -InputAcceptanceCriteria $AcceptanceCriteria -InputChecks $Checks -InputPriority $Priority
        $result = Invoke-QueueMutation {
            param($state)
            if (@($state.tasks | Where-Object { $_.id -eq $newTask.id }).Count -gt 0)
            {
                throw "Task id already exists: $($newTask.id)"
            }
            $state.tasks = @($state.tasks) + @($newTask)
            return $newTask
        }
        Write-Result $result
        break
    }

    'claim'
    {
        Ensure-Role -Expected 'executor' -Operation 'claim'
        $actorName = if ($Actor) { $Actor } else { 'executor' }
        $result = Invoke-QueueMutation {
            param($state)
            $candidate = @(
                $state.tasks |
                    Where-Object { $_.status -eq 'ready' } |
                    Sort-Object @{ Expression = { Get-PriorityRank $_.priority } }, createdAt
            )
            if ($candidate.Count -eq 0)
            {
                return [PSCustomObject]@{ status = 'empty'; message = 'No ready task is available.' }
            }

            $task = $candidate[0]
            $previousStatus = $task.status
            $task.status = 'running'
            $task.claimedBy = $actorName
            $task.claimedAt = Get-UtcTimestamp
            $task.attempts = [int]$task.attempts + 1
            Add-HistoryEntry -Task $task -From $previousStatus -To 'running' -ActorName $actorName -Note 'Executor claimed task.'
            return $task
        }
        Write-Result $result
        break
    }

    'submit'
    {
        Ensure-Role -Expected 'executor' -Operation 'submit'
        if ([string]::IsNullOrWhiteSpace($TaskId)) { throw 'TaskId is required for submit.' }
        if ([string]::IsNullOrWhiteSpace($ReportPath)) { throw 'ReportPath is required for submit.' }
        $resolvedReportPath = Resolve-ProjectPath -Path $ReportPath
        if (-not (Test-Path -LiteralPath $resolvedReportPath)) { throw "Report not found: $resolvedReportPath" }
        $report = (Get-Content -LiteralPath $resolvedReportPath -Raw) | ConvertFrom-Json
        if ([string]$report.taskId -ne $TaskId) { throw "Report taskId '$($report.taskId)' does not match '$TaskId'." }
        if ([string]::IsNullOrWhiteSpace([string]$report.result)) { throw 'Report result is required.' }
        if ([string]::IsNullOrWhiteSpace([string]$report.summary)) { throw 'Report summary is required.' }

        $actorName = if ($Actor) { $Actor } else { 'executor' }
        $result = Invoke-QueueMutation {
            param($state)
            $task = Get-TaskById -State $state -Id $TaskId
            if ($task.status -ne 'running') { throw "Task '$TaskId' is not running; current state is '$($task.status)'." }
            $previousStatus = $task.status
            $task.report = $report
            $task.status = if ([string]$report.result -eq 'blocked') { 'blocked' } else { 'awaiting_worker' }
            $task.claimedBy = if ($task.claimedBy) { $task.claimedBy } else { $actorName }
            Add-HistoryEntry -Task $task -From $previousStatus -To $task.status -ActorName $actorName -Note 'Executor submitted report.'
            return $task
        }
        Write-Result $result
        break
    }

    'feed'
    {
        Ensure-Role -Expected 'worker' -Operation 'feed'
        if ([string]::IsNullOrWhiteSpace($TaskId)) { throw 'TaskId is required for feed.' }
        if ([string]::IsNullOrWhiteSpace($Feedback)) { throw 'Feedback is required for feed.' }
        $nextTask = $null
        if (-not [string]::IsNullOrWhiteSpace($NextPayloadPath) -or -not [string]::IsNullOrWhiteSpace($NextTitle))
        {
            $nextTask = New-TaskObject -InputPath $NextPayloadPath -InputId $NextId -InputTitle $NextTitle -InputInstructions $NextInstructions -InputAcceptanceCriteria $NextAcceptanceCriteria -InputChecks $NextChecks -InputPriority $NextPriority
        }

        $actorName = if ($Actor) { $Actor } else { 'worker' }
        $result = Invoke-QueueMutation {
            param($state)
            $task = Get-TaskById -State $state -Id $TaskId
            if ($task.status -ne 'awaiting_worker') { throw "Task '$TaskId' is not awaiting worker review; current state is '$($task.status)'." }
            if ($nextTask -and @($state.tasks | Where-Object { $_.id -eq $nextTask.id }).Count -gt 0) { throw "Task id already exists: $($nextTask.id)" }

            $previousStatus = $task.status
            $task.workerFeedback = $Feedback
            $task.status = 'done'
            Add-HistoryEntry -Task $task -From $previousStatus -To 'done' -ActorName $actorName -Note $Feedback
            if ($nextTask)
            {
                $state.tasks = @($state.tasks) + @($nextTask)
            }
            return [PSCustomObject]@{ approvedTask = $task; nextTask = $nextTask }
        }
        Write-Result $result
        break
    }

    'reject'
    {
        Ensure-Role -Expected 'worker' -Operation 'reject'
        if ([string]::IsNullOrWhiteSpace($TaskId)) { throw 'TaskId is required for reject.' }
        if ([string]::IsNullOrWhiteSpace($Feedback)) { throw 'Feedback is required for reject.' }
        $actorName = if ($Actor) { $Actor } else { 'worker' }
        $result = Invoke-QueueMutation {
            param($state)
            $task = Get-TaskById -State $state -Id $TaskId
            if ($task.status -ne 'awaiting_worker') { throw "Task '$TaskId' is not awaiting worker review; current state is '$($task.status)'." }
            $previousStatus = $task.status
            $task.workerFeedback = $Feedback
            $task.status = 'ready'
            $task.claimedBy = $null
            $task.claimedAt = $null
            Add-HistoryEntry -Task $task -From $previousStatus -To 'ready' -ActorName $actorName -Note $Feedback
            return $task
        }
        Write-Result $result
        break
    }

    'block'
    {
        Ensure-Role -Expected 'executor' -Operation 'block'
        if ([string]::IsNullOrWhiteSpace($TaskId)) { throw 'TaskId is required for block.' }
        if ([string]::IsNullOrWhiteSpace($Feedback)) { throw 'Feedback is required for block.' }
        $actorName = if ($Actor) { $Actor } else { 'executor' }
        $result = Invoke-QueueMutation {
            param($state)
            $task = Get-TaskById -State $state -Id $TaskId
            if ($task.status -ne 'running') { throw "Task '$TaskId' is not running; current state is '$($task.status)'." }
            $previousStatus = $task.status
            $task.workerFeedback = $Feedback
            $task.status = 'blocked'
            Add-HistoryEntry -Task $task -From $previousStatus -To 'blocked' -ActorName $actorName -Note $Feedback
            return $task
        }
        Write-Result $result
        break
    }

    'requeue'
    {
        Ensure-Role -Expected 'worker' -Operation 'requeue'
        if ([string]::IsNullOrWhiteSpace($TaskId)) { throw 'TaskId is required for requeue.' }
        $actorName = if ($Actor) { $Actor } else { 'worker' }
        $result = Invoke-QueueMutation {
            param($state)
            $task = Get-TaskById -State $state -Id $TaskId
            if ($task.status -notin @('blocked', 'ready')) { throw "Task '$TaskId' cannot be requeued from '$($task.status)'." }
            $previousStatus = $task.status
            $task.status = 'ready'
            $task.claimedBy = $null
            $task.claimedAt = $null
            if ($Feedback) { $task.workerFeedback = $Feedback }
            $note = if ($Feedback) { $Feedback } else { 'Task requeued.' }
            Add-HistoryEntry -Task $task -From $previousStatus -To 'ready' -ActorName $actorName -Note $note
            return $task
        }
        Write-Result $result
        break
    }

    'report'
    {
        if ([string]::IsNullOrWhiteSpace($TaskId)) { throw 'TaskId is required for report.' }
        $state = Read-Queue
        $task = Get-TaskById -State $state -Id $TaskId
        Write-Result $task
        break
    }

    'watch'
    {
        if ([string]::IsNullOrWhiteSpace($TaskId)) { throw 'TaskId is required for watch.' }
        $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
        $task = $null
        do
        {
            $state = Read-Queue
            $task = Get-TaskById -State $state -Id $TaskId
            if ($task.status -in @('awaiting_worker', 'blocked', 'done'))
            {
                Write-Result $task
                break
            }
            Start-Sleep -Seconds 2
        }
        while ((Get-Date) -lt $deadline)

        if ((Get-Date) -ge $deadline -and $task.status -notin @('awaiting_worker', 'blocked', 'done'))
        {
            throw "Timed out waiting for task '$TaskId'; current state is '$($task.status)'."
        }
        break
    }
}
