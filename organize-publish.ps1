param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDirectory
)

$ErrorActionPreference = 'Stop'
$publishRoot = (Resolve-Path -LiteralPath $PublishDirectory).Path
$appDirectory = Join-Path $publishRoot 'App'

# 只整理发布根目录中的项目文件，Plugins 内的程序集保持原有布局。
$projectFiles = @(Get-ChildItem -LiteralPath $publishRoot -File -Filter 'QQAIAgent.*')
$projectFileNames = @($projectFiles.Name)

foreach ($depsFile in $projectFiles | Where-Object { $_.Name.EndsWith('.deps.json') }) {
    $deps = Get-Content -LiteralPath $depsFile.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($target in $deps.targets.PSObject.Properties) {
        foreach ($library in $target.Value.PSObject.Properties) {
            foreach ($assetType in @('runtime', 'native', 'resources')) {
                foreach ($asset in $library.Value.$assetType.PSObject.Properties) {
                    $fileName = [IO.Path]::GetFileName($asset.Name)
                    if ($fileName -eq '_._') {
                        continue
                    }

                    # .NET 10 的 localPath 相对于主程序目录；包内路径不等于发布后的路径。
                    $localPath = $asset.Value.localPath
                    if ([string]::IsNullOrEmpty($localPath)) {
                        if ($deps.libraries.($library.Name).type -eq 'runtimepack') {
                            $localPath = $asset.Name
                        }
                        elseif ($assetType -eq 'resources') {
                            $culture = [IO.Path]::GetFileName([IO.Path]::GetDirectoryName($asset.Name))
                            $localPath = "$culture/$fileName"
                        }
                        else {
                            $localPath = $fileName
                        }
                    }

                    # 只有根目录内本次实际移动的文件留在 App，其余依赖仍从原位置加载。
                    if ($projectFileNames -notcontains $localPath) {
                        $localPath = "../$localPath"
                    }
                    $asset.Value | Add-Member -NotePropertyName localPath -NotePropertyValue $localPath -Force
                }
            }
        }
    }
    $json = $deps | ConvertTo-Json -Depth 100
    [IO.File]::WriteAllText($depsFile.FullName, $json, [Text.UTF8Encoding]::new($false))
}

New-Item -ItemType Directory -Path $appDirectory -Force | Out-Null
foreach ($file in $projectFiles) {
    Move-Item -LiteralPath $file.FullName -Destination (Join-Path $appDirectory $file.Name)
}
