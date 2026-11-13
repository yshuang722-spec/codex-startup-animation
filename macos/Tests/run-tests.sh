#!/bin/zsh
set -euo pipefail
project_dir="${0:A:h:h}"
binary="$project_dir/dist/Codex 启动器.app/Contents/MacOS/CodexStartupMac"
mkdir -p "$project_dir/test-results"
node --check "$project_dir/Resources/animation.js"
/bin/zsh -n "$project_dir/build.sh"
"$binary" --self-test > "$project_dir/test-results/policy.json"
if "$binary" --diagnostics > "$project_dir/test-results/diagnostics.json"; then
  :
else
  diagnostic_status=$?
  if [[ $diagnostic_status -ne 1 ]]; then exit "$diagnostic_status"; fi
fi
"$binary" --render-tests "$project_dir/previews" > "$project_dir/test-results/render.json"
/usr/bin/codesign --verify --deep --strict "$project_dir/dist/Codex 启动器.app"
/usr/bin/python3 - "$project_dir" <<'PY'
import json, pathlib, plistlib, subprocess, sys
project = pathlib.Path(sys.argv[1])
policy = json.loads((project / 'test-results/policy.json').read_text())
render = json.loads((project / 'test-results/render.json').read_text())
diagnostics = json.loads((project / 'test-results/diagnostics.json').read_text())
app=project/'dist/Codex 启动器.app'
info=plistlib.loads((app/'Contents/Info.plist').read_bytes())
assert info['CFBundleDisplayName']=='Codex 启动器'
assert info['CFBundleShortVersionString']=='1.0.0'
assert info['LSUIElement'] is False
assert (app/'Contents/Resources'/info['CFBundleIconFile']).read_bytes().startswith(b'icns')
assert len(policy['passed']) == 20
assert len(policy['windowPassed']) == 15
assert len(policy['pointerPassed']) == 8
assert len(render['rendered']) == 12
assert len(render['checks']['passed']) >= 7
assert diagnostics['resourcesPresent'] and diagnostics['residentWatcher'] is False
assert diagnostics['visualMargin'] == 0 and diagnostics['dragTracking'] == 'mouseEvents'
frames = {frame['name']: frame for frame in render['rendered']}
for name in ['02-code', '03-cards', '04-ready', '08-narrow-cards', '09-narrow-ready', '10-small-ready']:
    frame = frames[name]
    assert frame['opaquePixels'] == frame['width'] * frame['height'], (name, frame)
assert frames['01-intro']['transparentPixels'] > 0
assert frames['07-finished']['transparentPixels'] == 1440 * 900
assert frames['06-crt-reveal']['transparentPixels'] > 1440 * 900 / 3
for frame in frames.values():
    assert (project / 'previews' / (frame['name'] + '.png')).stat().st_size > 100
button = render['interfaceSnapshot']['skipButton']
assert 0 <= button['left'] < button['right'] <= 900
assert 0 <= button['top'] < button['bottom'] <= 650
assert (project / 'previews/13-interface.png').stat().st_size > 100
fixture = project / 'test-results/not-a-directory'
fixture.write_text('intentional filesystem failure fixture')
try:
    binary = project / 'dist/Codex 启动器.app/Contents/MacOS/CodexStartupMac'
    failure = subprocess.run([str(binary), '--render-tests', str(fixture)], capture_output=True, text=True, timeout=15)
    assert failure.returncode == 1, failure
    evidence = {'renderOutputNotDirectory': {'exitCode': failure.returncode, 'stderr': failure.stderr.strip()}}
    (project / 'test-results/failure-paths.json').write_text(json.dumps(evidence, ensure_ascii=False, indent=2))
finally:
    fixture.unlink()
print(f"PASS: {len(policy['passed'])} lifecycle/options checks; {len(policy['windowPassed'])} window/input checks; {len(policy['pointerPassed'])} pointer tracking checks; {len(render['checks']['passed'])} renderer checks; 12 native WebKit frames; UI snapshot; alpha checks; code signature")
PY
