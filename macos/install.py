#!/usr/bin/env python3
"""Install only this launcher. Never start apps, change Dock items, or add login services."""
import hashlib
import json
import os
from pathlib import Path
import plistlib
import shutil
import subprocess
import tempfile

PROJECT = Path(__file__).resolve().parent
SOURCE = PROJECT / 'dist/Codex 启动器.app'
DESTINATION = Path('/Applications/Codex 启动器.app')
BUNDLE_ID = 'io.github.codex-startup-animation.macos'


def identity(path):
    with (path / 'Contents/Info.plist').open('rb') as stream:
        return plistlib.load(stream)


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def tree_hashes(path):
    return {str(file.relative_to(path)): digest(file) for file in path.rglob('*') if file.is_file()}


def run(*arguments):
    return subprocess.run(arguments, check=True, capture_output=True, text=True)


def install(destination=DESTINATION):
    destination=Path(destination).expanduser().absolute()
    info = identity(SOURCE)
    if info.get('CFBundleIdentifier') != BUNDLE_ID:
        raise RuntimeError('Source bundle identity does not match this launcher')
    if destination.is_symlink():
        raise RuntimeError('Destination is a symbolic link; no changes made')
    if destination.exists() and identity(destination).get('CFBundleIdentifier') != BUNDLE_ID:
        raise RuntimeError('Destination contains another application; no changes made')
    if not os.access(destination.parent, os.W_OK):
        raise PermissionError('Applications directory is not writable; no permission prompt was opened')
    executable = str(destination / 'Contents/MacOS/CodexStartupMac')
    processes = run('/bin/ps', '-axo', 'comm=').stdout.splitlines()
    if executable in [line.strip() for line in processes]:
        raise RuntimeError('Launcher is still playing; wait until it exits before installing')
    run('/usr/bin/codesign', '--verify', '--deep', '--strict', str(SOURCE))
    probe=subprocess.run([str(SOURCE/'Contents/MacOS/CodexStartupMac'),'--diagnostics'],capture_output=True,text=True)
    if probe.returncode not in (0,1):
        raise RuntimeError('Launcher diagnostics failed')
    diagnostics=json.loads(probe.stdout)
    original_before={}
    original=None
    if diagnostics.get('clientPath'):
        original=Path(diagnostics['clientPath'])/'Contents'
        with (original/'Info.plist').open('rb') as stream:
            client_info=plistlib.load(stream)
        original_before = {name: digest(original / name) for name in ('Info.plist', 'MacOS/'+client_info['CFBundleExecutable'])}
    source_hashes = tree_hashes(SOURCE)
    with tempfile.TemporaryDirectory(prefix='.codex-launcher-install-', dir=destination.parent) as temporary:
        staging = Path(temporary)
        staged = staging / destination.name
        previous = staging / 'previous.app'
        run('/usr/bin/ditto', str(SOURCE), str(staged))
        run('/usr/bin/codesign', '--verify', '--deep', '--strict', str(staged))
        if tree_hashes(staged) != source_hashes:
            raise RuntimeError('Staged application differs from source; destination retained')
        if destination.exists():
            destination.rename(previous)
        try:
            staged.rename(destination)
        except Exception:
            if previous.exists():
                previous.rename(destination)
            raise
        try:
            run('/usr/bin/codesign', '--verify', '--deep', '--strict', str(destination))
            if tree_hashes(destination) != source_hashes:
                raise RuntimeError('Installed application hash mismatch')
        except Exception:
            shutil.rmtree(destination)
            if previous.exists():
                previous.rename(destination)
            raise
    record = {
        'name': info['CFBundleDisplayName'],
        'version': info['CFBundleShortVersionString'],
        'path': str(destination),
        'bundleID': BUNDLE_ID,
        'codeSignatureVerified': True,
        'allInstalledFilesMatchBuild': True,
        'fileCount': len(source_hashes),
        'iconSHA256': digest(destination / 'Contents/Resources/Launcher.icns'),
        'clientInfoAndExecutableUnchanged': all(digest(original / name) == before for name, before in original_before.items()) if original_before else None,
        'startedApplication': False,
        'addedLoginService': False,
        'changedDock': False,
    }
    results = PROJECT / 'test-results'
    results.mkdir(exist_ok=True)
    (results / 'installation.json').write_text(json.dumps(record, ensure_ascii=False, indent=2) + '\n')
    print(json.dumps(record, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    import argparse
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--destination',type=Path,default=DESTINATION)
    install(parser.parse_args().destination)
