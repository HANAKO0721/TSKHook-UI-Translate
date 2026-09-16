param([Parameter(Mandatory = $true)][string]$GamePath)
$ErrorActionPreference = 'Stop'
$tskPlugins = Join-Path $GamePath 'BepInEx\plugins'
if (-not (Test-Path -LiteralPath (Join-Path $tskPlugins 'TSKHook.dll'))) { throw 'Install the original TSKHook first. This package only updates the UI extension.' }
if (Get-Process -Name 'twinkle_starknightsX' -ErrorAction SilentlyContinue) { throw 'Close the game before updating the UI DLL.' }
# The original story plugin, shared config, and existing caches belong to this installation.
$tskSourcePlugins = Join-Path $PSScriptRoot 'BepInEx\plugins'
if ([IO.Path]::GetFullPath($tskSourcePlugins) -ne [IO.Path]::GetFullPath($tskPlugins)) {
    Copy-Item -LiteralPath (Join-Path $tskSourcePlugins 'TSKHook.UI.dll') -Destination $tskPlugins -Force
    Copy-Item -LiteralPath (Join-Path $tskSourcePlugins 'TSKHook.UI') -Destination $tskPlugins -Recurse -Force
}
# Retire only the translated PNGs removed from this release at the user's request.
# Keep other installed images, user files, caches and the original game resources.
$tskRetiredShopSprites = @(
    'btn_exchange_shop_1001.png',
    'btn_exchange_shop_1003.png',
    'btn_exchange_shop_1004.png',
    'btn_exchange_shop_1014.png',
    'btn_exchange_shop_1019.png',
    'btn_exchange_shop_1013.png',
    'btn_exchange_shop_1008.png',
    'btn_exchange_shop_2009.png',
    'btn_exchange_gacha_0015.png',
    'btn_exchange_gacha_0014.png',
    'btn_common_exchange_gacha_0025.png',
    'btn_exchange_event_20461.png',
    'btn_exchange_event_3040.png',
    'btn_exchange_event_0001.png',
    'btn_exchange_gacha_0005.png',
    'btn_exchange_shop_2006.png',
    'btn_exchange_shop_2004.png',
    'btn_exchange_shop_2008.png',
    'btn_exchange_shop_2005.png',
    'btn_exchange_shop_2007.png',
    'btn_exchange_shop_2011.png',
    'btn_exchange_shop_2010.png',
    'product_00986.png'
)
$tskInstalledSprites = Join-Path $tskPlugins 'TSKHook.UI\sprites'
foreach ($tskRetiredName in $tskRetiredShopSprites) {
    $tskRetiredPath = Join-Path $tskInstalledSprites $tskRetiredName
    if (Test-Path -LiteralPath $tskRetiredPath -PathType Leaf) {
        Remove-Item -LiteralPath $tskRetiredPath -Force
    }
}
Write-Output 'TSKHook UI updated. Existing TSKHook.dll and config.json were preserved. Restart the game to load the UI DLL.'
