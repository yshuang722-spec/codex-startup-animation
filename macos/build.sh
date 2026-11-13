#!/bin/zsh
set -euo pipefail
project_dir="${0:A:h}"
final_app="$project_dir/dist/Codex 启动器.app"
cache_dir="${CODEX_STARTUP_BUILD_CACHE:-${TMPDIR:-/tmp}/codex-startup-mac-module-cache-$UID}"
mkdir -p "$project_dir/dist"
staging_dir=$(mktemp -d "$project_dir/dist/.macos-build.XXXXXX")
trap '/bin/rm -rf "$staging_dir"' EXIT
app_dir="$staging_dir/Codex 启动器.app"
mkdir -p "$app_dir/Contents/MacOS" "$app_dir/Contents/Resources" "$cache_dir"
/usr/bin/xcrun swift -module-cache-path "$cache_dir" "$project_dir/Tools/MakeIcon.swift" \
  "$project_dir/Resources/logo-mark.path" "$cache_dir/Launcher.iconset" "$project_dir/previews"
/usr/bin/iconutil --convert icns --output "$project_dir/Resources/Launcher.icns" "$cache_dir/Launcher.iconset"
/usr/bin/python3 - "$project_dir" "$app_dir" <<'PY'
import json, pathlib, plistlib, shutil, sys
project, app = map(pathlib.Path, sys.argv[1:])
for resource in (project / 'Resources').iterdir():
    if resource.is_file(): shutil.copy2(resource, app / 'Contents/Resources' / resource.name)
path = (project / 'Resources/logo-mark.path').read_text().strip()
(app / 'Contents/Resources/logo.js').write_text('window.UPSTREAM_LOGO_PATH = ' + json.dumps(path) + ';\n')
for name in ['LICENSE', 'UPSTREAM-NOTICE.md']:
    shutil.copy2(project / name, app / 'Contents/Resources' / name)
info = dict(CFBundleIdentifier='io.github.codex-startup-animation.macos', CFBundleName='Codex 启动器',
            CFBundleDisplayName='Codex 启动器', CFBundleExecutable='CodexStartupMac',
            CFBundleIconFile='Launcher.icns',
            CFBundlePackageType='APPL', CFBundleShortVersionString='1.0.0', CFBundleVersion='5',
            LSMinimumSystemVersion='13.0', LSUIElement=False, NSHighResolutionCapable=True,
            NSPrincipalClass='NSApplication')
with (app / 'Contents/Info.plist').open('wb') as stream: plistlib.dump(info, stream)
PY
/usr/bin/xcrun swiftc -O -target arm64-apple-macosx13.0 -module-cache-path "$cache_dir" \
  -framework AppKit -framework WebKit "$project_dir/Sources/SessionPolicy.swift" "$project_dir/Sources/WindowLocator.swift" \
  "$project_dir/Sources/PointerTracking.swift" "$project_dir/Sources/OverlayPresenter.swift" "$project_dir/Sources/main.swift" \
  -o "$app_dir/Contents/MacOS/CodexStartupMac"
/usr/bin/codesign --force --sign - "$app_dir"
/usr/bin/codesign --verify --deep --strict "$app_dir"
/usr/bin/python3 - "$app_dir" "$final_app" "$staging_dir" <<'PY'
from pathlib import Path
import sys
source, target, staging = map(Path, sys.argv[1:])
previous = staging / 'previous.app'
if target.exists():
    target.rename(previous)
try:
    source.rename(target)
except Exception:
    if previous.exists(): previous.rename(target)
    raise
PY
print "Built: $final_app"
