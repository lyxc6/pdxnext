$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe = Join-Path $root '..\src\PdxLauncher.Mcp\bin\Debug\net11.0\win-x64\PdxLauncher.Mcp.exe'
if (-not (Test-Path -LiteralPath $exe)) {
    throw "MCP executable not found: $exe"
}

$inputText = @'
{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","clientInfo":{"name":"smoke","version":"1"},"capabilities":{}}}
{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}
{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"create_change_plan","arguments":{"domain":"playsets","operation":"rename_play_set","gameId":"victoria3","targetId":"11111111-1111-1111-1111-111111111111","expectedHash":"old","patch":{"name":"new"}}}}
'@
$outputText = $inputText | & $exe --data-root (Join-Path $root '..\data') --enabled true --draft-write true 2>$null
$lines = @($outputText | Where-Object { $_ -and $_.Trim().StartsWith('{') })
if ($lines.Count -lt 2) {
    throw "Expected two JSON-RPC responses, got $($lines.Count)"
}
$tools = ($lines[1] | ConvertFrom-Json).result.tools
$names = @($tools | ForEach-Object { $_.name })
Write-Output "MCP_TOOLS=$($names -join ',')"
if ($names -notcontains 'get_playsets_page' -or $names -notcontains 'create_change_plan') {
    throw 'New MCP tools are missing from tools/list'
}
if ($names -contains 'apply_change_plan') {
    throw 'Direct execution tool must not be exposed'
}
if ($lines.Count -lt 3) {
    throw 'Missing create_change_plan response'
}
$planResponse = ($lines[2] | ConvertFrom-Json).result
if ($planResponse.isError -or $planResponse.content[0].text -notmatch '"planId"') {
    throw 'create_change_plan did not return a persisted plan id'
}
Write-Output 'MCP_SMOKE=PASS'
