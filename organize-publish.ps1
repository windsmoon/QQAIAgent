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

# 普通 apphost 在当前目录存在 runtimeconfig、而运行时根目录没有同名 deps 时，
# 会进入旧版 split/FX 模式，双击 App 内的 EXE 因而直接退出。
# 使用 SDK 的 Bundler 仅嵌入启动清单，让 EXE 明确走 bundle 启动入口；所有 DLL 仍保持外置。
$bundleProject = Join-Path $PSScriptRoot 'bundle-app.proj'

foreach ($executable in $projectFiles | Where-Object { $_.Extension -eq '.exe' }) {
    # Windows PowerShell 不提供 SDK 的程序集绑定环境，封装任务必须交给 dotnet MSBuild 执行。
    & dotnet msbuild $bundleProject -nologo -verbosity:minimal -target:BundleApp `
        "-property:SourceDirectory=$publishRoot" `
        "-property:BundleOutputDirectory=$appDirectory" `
        "-property:AppName=$($executable.BaseName)"
    if ($LASTEXITCODE -ne 0) {
        throw "EXE 封装失败：$($executable.Name)，退出码：$LASTEXITCODE"
    }
}

foreach ($file in $projectFiles) {
    if ($file.Extension -eq '.exe') {
        # App 中已有 SDK 生成的可执行文件，此处只删除根目录中的原始 apphost。
        Remove-Item -LiteralPath $file.FullName
    }
    else {
        Move-Item -LiteralPath $file.FullName -Destination (Join-Path $appDirectory $file.Name)
    }
}
