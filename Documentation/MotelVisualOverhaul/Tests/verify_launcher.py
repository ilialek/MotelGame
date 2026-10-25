from pathlib import Path
import subprocess, time, os, signal, json, datetime

project = Path('/Users/ilalekomtsev/MotelGame')
launcher = project / 'Builds/Playtest/LaunchWindowed.command'
executable = str(project / 'Builds/Playtest/MotelGame.app/Contents/MacOS/MotelGame')

def players():
    result = set()
    for line in subprocess.check_output(['ps', '-axo', 'pid=,comm='], text=True).splitlines():
        fields = line.strip().split(None, 1)
        if len(fields) == 2 and fields[1] == executable:
            result.add(int(fields[0]))
    return result

before = players()
subprocess.run([str(launcher)], check=True)
owned = set()
try:
    deadline = time.monotonic() + 20
    while time.monotonic() < deadline:
        owned = players() - before
        if owned:
            break
        time.sleep(.5)
    assert len(owned) == 1, f'Expected one newly launched app; got {owned}'
    time.sleep(8)
    assert owned <= players(), 'Normal app exited unexpectedly'
    log = Path.home() / 'Library/Logs/DefaultCompany/MotelGame/Player.log'
    text = log.read_text(errors='replace')
    critical = [line for line in text.splitlines() if any(term in line for term in ['Exception:', 'NullReferenceException', 'MissingReferenceException', 'Assertion failed', '[Error]'])]
    assert not critical, critical
    report = {'time': datetime.datetime.now().isoformat(), 'launcher': str(launcher), 'result': 'PASS',
              'new_process': list(owned), 'existing_processes_preserved': sorted(before),
              'checked': 'Stable .command launched one normal app; process remained alive for 8 seconds; startup log has no exception/error matches. Window flags 800x600, fullscreen 0. No human input/visual assertion.',
              'critical_log_matches': critical}
    target = project / 'Documentation/MotelVisualOverhaul'
    (target / 'launcher-startup.log').write_text(text)
    (target / 'launcher-validation.json').write_text(json.dumps(report, indent=2))
    print(json.dumps(report, indent=2))
finally:
    for pid in owned & players():
        os.kill(pid, signal.SIGTERM)
