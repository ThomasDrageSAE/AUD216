# profiling_driver.py - the phase-0 capture driver from PERFORMANCE_PROFILING_TASKS.md,
# committed as the starting point for re-measurement (see PERFORMANCE_FIX_TASKS.md).
# Usage: python Documentation/tools/profiling_driver.py <stamp> core
# Hard-coded: PROJECT path, the scenario list under SCENARIOS == "core". Edit both.
# Never run two of these at once - one driver, one editor.

"""Phase 0 capture driver for PERFORMANCE_PROFILING_TASKS.md.

Runs the protocol through the Unity CLI: never more than one editor command at a time,
never a wait inside an eval, retries on the connection resets the bridge throws under load.
"""
import base64
import json
import os
import subprocess
import sys
import time

PROJECT = "D:/GitHub/AUD216_Project1_Nature_Level"
SCRATCH = os.path.dirname(os.path.abspath(__file__))
STAMP = sys.argv[1] if len(sys.argv) > 1 else "20260904_1349"
OUT = os.path.join(PROJECT, "Exports", "Profiling", STAMP)


def unity(*args, timeout=180, retries=3):
    """One CLI call, retried on transport failures."""
    for attempt in range(retries):
        proc = subprocess.run(
            ["unity", "command", "--no-banner", "--project-path", PROJECT,
             "--timeout", str(timeout), "--json"] + list(args),
            capture_output=True, text=True, timeout=timeout + 60)
        try:
            data = json.loads(proc.stdout)
        except Exception:
            time.sleep(2)
            continue
        if data.get("success"):
            return data
        msg = json.dumps(data.get("errors"))[:200]
        if "reset" in msg.lower() or "network" in msg.lower() or "timed out" in msg.lower():
            time.sleep(3)
            continue
        return data
    return {"success": False, "errors": [{"message": "gave up after %d attempts" % retries}]}


def result_of(data):
    r = (data.get("data") or {}).get("result")
    if isinstance(r, str):
        try:
            r = json.loads(r)
        except Exception:
            return r
    if isinstance(r, dict) and "result" in r:
        return r["result"]
    return r


def ev(code, timeout=60):
    """Run a C# snippet from a temp file (avoids the shell mangling $" and '{')."""
    path = os.path.join(SCRATCH, "_step.cs")
    with open(path, "w", encoding="utf-8") as fh:
        fh.write(code)
    data = unity("eval_file", "--file", path, timeout=timeout)
    if not data.get("success"):
        return "ERR " + json.dumps(data.get("errors"))[:300]
    return result_of(data)


def menu(path):
    data = unity("menu", "--path", path, timeout=240)
    return "ok" if data.get("success") else "ERR " + json.dumps(data.get("errors"))[:200]


def playing():
    return "True" in str(ev('return UnityEditor.EditorApplication.isPlaying;'))


def enter_play():
    if playing():
        return
    unity("editor_play", timeout=120)
    for _ in range(90):
        if playing():
            break
        time.sleep(1)
    time.sleep(2)
    # The title screen freezes time and disables the controller.
    print("   ", ev('var t = UnityEngine.Object.FindFirstObjectByType<TitleScreen>();\n'
                    'if (t != null) t.GetType().GetMethod("Play", System.Reflection.BindingFlags.NonPublic '
                    '| System.Reflection.BindingFlags.Instance).Invoke(t, null);\n'
                    'return "title dismissed=" + (t != null);'))
    time.sleep(1)   # FirstPersonController.Start runs next frame and resets its own state


def stop_play():
    unity("editor_stop", timeout=120)
    for _ in range(60):
        if not playing():
            return
        time.sleep(1)


def capture(name, seconds, setup_code, uncapped=True):
    print("\n=== %s ===" % name)
    if setup_code:
        print("    setup:", ev(setup_code))
        time.sleep(1.5)

    ev('PerformanceProbe.Stamp = "%s";\n'
       'ProfilerCaptureTools.Stamp = "%s";\n'
       'ProfilerCaptureTools.Scenario = "%s";\n'
       'return "named";' % (STAMP, STAMP, name + ("_uncapped" if uncapped else "_capped")))

    print("    profiler:", menu("Tools/Profiling/Start Profiler Recording"))
    print("    begin:", ev('return PerformanceProbe.Begin("%s", %.1ff, %s);'
                           % (name, seconds, "true" if uncapped else "false")))

    deadline = time.time() + seconds + 45
    while time.time() < deadline:
        time.sleep(3)
        state = str(ev('return "done=" + PerformanceProbe.IsDone;'))
        if "done=True" in state:
            break

    print("    summary:", ev('return PerformanceProbe.LastSummary;'))
    print("    export:", menu("Tools/Profiling/Stop Recording And Export"))

    # Screenshot for the rendering agent.
    data = unity("capture_game_view", "--source", "screen", timeout=120)
    if data.get("success"):
        r = result_of(data)
        b64 = r.get("base64") if isinstance(r, dict) else None
        if b64:
            os.makedirs(OUT, exist_ok=True)
            with open(os.path.join(OUT, "screen_%s.png" % name), "wb") as fh:
                fh.write(base64.b64decode(b64))
            print("    screenshot: ok")

    # Errors logged during the window.
    data = unity("console", "--tail", "400", "--level", "error", timeout=120)
    if data.get("success"):
        r = result_of(data)
        entries = r.get("entries", []) if isinstance(r, dict) else []
        counts = {}
        for e in entries:
            msg = str(e.get("message", ""))[:120]
            counts[msg] = counts.get(msg, 0) + 1
        os.makedirs(OUT, exist_ok=True)
        with open(os.path.join(OUT, "console_%s.txt" % name), "w", encoding="utf-8") as fh:
            fh.write("# distinct error messages in the last 400 entries, with counts\n")
            for msg, n in sorted(counts.items(), key=lambda kv: -kv[1]):
                fh.write("%6d  %s\n" % (n, msg))
        print("    console: %d entries, %d distinct" % (len(entries), len(counts)))


TELEPORT = '''
var pl = UnityEngine.GameObject.FindWithTag("Player");
var cc = pl.GetComponent<UnityEngine.CharacterController>();
var target = UnityEngine.GameObject.Find("%s");
if (target == null) return "target not found";
var p = target.transform.position + new UnityEngine.Vector3(%s);
if (cc != null) cc.enabled = false;
pl.transform.position = p;
pl.transform.LookAt(new UnityEngine.Vector3(target.transform.position.x, pl.transform.position.y, target.transform.position.z));
if (cc != null) cc.enabled = true;
var cam = UnityEngine.Camera.main;
return "moved to " + p + " looking at %s";
'''


def teleport(target, offset="0f, 2f, -6f"):
    return TELEPORT % (target, offset, target)


SCENARIOS = sys.argv[2] if len(sys.argv) > 2 else "core"

if SCENARIOS == "core":
    enter_play()
    capture("S1_spawn_idle", 12, None, uncapped=False)
    capture("S1_spawn_idle", 12, None, uncapped=True)
    capture("S2_studio", 12, teleport("Studio Room", "0f, 2f, 0f"))
    capture("S3_waterfall", 12, teleport("Waterfall", "0f, 2f, -8f"))
    capture("S4_swamp", 12, teleport("Swamp_Water", "0f, 3f, -8f"))
    stop_play()
    print("\ndone: core")
