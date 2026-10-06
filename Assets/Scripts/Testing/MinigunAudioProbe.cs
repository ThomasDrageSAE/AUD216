#if UNITY_EDITOR
using System.Collections;
using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>
/// Editor-only check for the minigun's five-clip audio model. Holds the trigger through the
/// controller's editor seam, releases, re-presses during the shutdown, releases during the
/// wind-up, and every 50 ms writes down the state, the barrel fraction, the rounds left and
/// every AudioSource on the WeaponAudioManager that is playing - clip, playhead, loop flag.
///
/// A component rather than an editor script because the sequence is about TIME: the wind-up
/// is 0.74 s, and a round-trip through the CLI bridge is about a second. Coroutines sample on
/// the frame; the bridge reads the finished report. Added and removed by the check that runs
/// it, never left in the scene.
/// </summary>
public class MinigunAudioProbe : MonoBehaviour
{
    public bool Done { get; private set; }
    public string Report { get; private set; } = "";

    private MinigunController minigun;
    private WeaponAudioManager manager;
    private readonly StringBuilder sb = new StringBuilder();
    private float t0;

    private IEnumerator Start()
    {
        minigun = FindFirstObjectByType<MinigunController>();
        manager = FindFirstObjectByType<WeaponAudioManager>();

        if (minigun == null || manager == null)
        {
            Report = "no MinigunController or no WeaponAudioManager";
            Done = true;
            yield break;
        }

        t0 = Time.time;
        Log("before press");

        // ---- press and hold through the wind-up into sustained fire ----
        minigun.EditorHoldTrigger = true;
        yield return null;
        Log("press +1 frame");

        float until = Time.time + 1.4f;
        while (Time.time < until)
        {
            yield return new WaitForSeconds(0.05f);
            Log("hold");
        }

        // ---- release: both loops must stop dead, shutdown must start ----
        minigun.EditorHoldTrigger = false;
        yield return null;
        yield return null;
        Log("release +2 frames");
        yield return new WaitForSeconds(0.3f);
        Log("release +0.3 s");

        // ---- re-press while the shutdown is still playing and the barrels still turning ----
        minigun.EditorHoldTrigger = true;
        yield return null;
        Log("re-press +1 frame (want: shutdown cut, wind-up resumed from the barrel's fraction)");
        yield return new WaitForSeconds(0.15f);
        Log("re-press +0.15 s");

        // ---- two taps: 0.2 s, and two frames. Every frame of the transition source logged ----
        minigun.EditorHoldTrigger = false;
        yield return WaitForIdle();
        yield return Tap("tap 0.2 s", 0.2f, -1);

        yield return WaitForIdle();
        yield return Tap("tap 2 frames", -1f, 2);

        // ---- the wind-down: the barrels must stop turning when the shutdown clip does ----
        minigun.EditorHoldTrigger = true;
        yield return new WaitForSeconds(1.3f);
        Log("hold 1.3 s, then release");
        minigun.EditorHoldTrigger = false;

        float released = Time.time;
        float shutdownEnded = -1f;
        float barrelsStopped = -1f;
        int frames = 0;

        while (Time.time - released < 3.2f)
        {
            yield return null;
            float since = Time.time - released;

            if (shutdownEnded < 0f && since > 0.1f && !IsPlaying("MinigunShootingShutdown")) shutdownEnded = since;
            if (barrelsStopped < 0f && minigun.EditorSpinFraction <= 0f) barrelsStopped = since;
            if (++frames % 10 == 0) Log("wind-down");
        }

        sb.Append("WIND-DOWN: shutdown clip ended ").Append(shutdownEnded.ToString("0.000", CultureInfo.InvariantCulture))
          .Append(" s after release; barrels stopped ").Append(barrelsStopped.ToString("0.000", CultureInfo.InvariantCulture))
          .Append(" s after release; wind-down in force ").Append(minigun.EditorWinddownSeconds.ToString("0.000", CultureInfo.InvariantCulture))
          .Append(" s; difference ").Append(Mathf.Abs(shutdownEnded - barrelsStopped).ToString("0.000", CultureInfo.InvariantCulture))
          .AppendLine(" s (a frame or two is the sampling; a second is the bug)");

        Report = sb.ToString();
        Done = true;
    }

    private IEnumerator WaitForIdle()
    {
        float until = Time.time + 4f;
        while (minigun.EditorSpinState != "Idle" && Time.time < until) yield return null;
        Log("idle");
    }

    /// <summary>
    /// Press for a time or a number of frames, release, then log every frame until the
    /// barrels stop and the transition source falls silent. Reports how long the wind-up
    /// was heard, how long the wind-down was heard and from what offset, and when the
    /// barrels stopped - the three numbers a tap is judged on.
    /// </summary>
    private IEnumerator Tap(string name, float seconds, int frames)
    {
        Log(name + ": press");
        float pressed = Time.time;
        minigun.EditorHoldTrigger = true;

        if (frames > 0) { for (int f = 0; f < frames; f++) yield return null; }
        else yield return new WaitForSeconds(seconds);

        float fractionAtRelease = minigun.EditorSpinFraction;
        float held = Time.time - pressed;
        minigun.EditorHoldTrigger = false;
        float released = Time.time;

        float windupHeardUntil = -1f, winddownFrom = -1f, winddownStarted = -1f, winddownEnded = -1f, barrelsStopped = -1f;
        float lastWindupTime = -1f;

        for (int f = 0; f < 240; f++)
        {
            yield return null;
            float since = Time.time - released;

            foreach (AudioSource source in manager.GetComponents<AudioSource>())
            {
                if (!source.isPlaying || source.clip == null) continue;
                if (source.clip.name == "MinigunSpinWindup") { windupHeardUntil = Time.time - pressed; lastWindupTime = source.time; }
                if (source.clip.name == "MinigunSpinWindDown")
                {
                    if (winddownStarted < 0f) { winddownStarted = since; winddownFrom = source.time; }
                    winddownEnded = since;
                }
            }

            if (barrelsStopped < 0f && minigun.EditorSpinFraction <= 0f) barrelsStopped = since;
            if (f < 12 || f % 6 == 0) Log(name + " +" + since.ToString("0.000", CultureInfo.InvariantCulture));
            if (barrelsStopped >= 0f && since > 0.3f && winddownEnded >= 0f && since - winddownEnded > 0.1f) break;
            if (barrelsStopped >= 0f && since > 1.0f && winddownStarted < 0f) break;
        }

        sb.Append(name.ToUpper()).Append(": held ").Append(held.ToString("0.000", CultureInfo.InvariantCulture))
          .Append(" s, barrels at ").Append(fractionAtRelease.ToString("0.00", CultureInfo.InvariantCulture))
          .Append("; wind-up heard until ").Append(windupHeardUntil.ToString("0.000", CultureInfo.InvariantCulture))
          .Append(" s after press (playhead ").Append(lastWindupTime.ToString("0.000", CultureInfo.InvariantCulture))
          .Append("); wind-down ").Append(winddownStarted < 0f ? "NEVER PLAYED" :
              "from " + winddownFrom.ToString("0.000", CultureInfo.InvariantCulture) + " s, heard " +
              winddownStarted.ToString("0.000", CultureInfo.InvariantCulture) + " .. " + winddownEnded.ToString("0.000", CultureInfo.InvariantCulture) + " s after release")
          .Append("; barrels stopped ").Append(barrelsStopped.ToString("0.000", CultureInfo.InvariantCulture))
          .AppendLine(" s after release");
    }

    private float WinddownLength()
    {
        foreach (AudioSource source in manager.GetComponents<AudioSource>())
        {
            if (source.clip != null && source.clip.name == "MinigunSpinWindDown") return source.clip.length;
        }
        return 0f;
    }

    private bool IsPlaying(string clipName)
    {
        foreach (AudioSource source in manager.GetComponents<AudioSource>())
        {
            if (source.isPlaying && source.clip != null && source.clip.name == clipName) return true;
        }
        return false;
    }

    private void Log(string tag)
    {
        sb.Append((Time.time - t0).ToString("0.000", CultureInfo.InvariantCulture).PadLeft(6))
          .Append("  ").Append(tag.PadRight(24))
          .Append(" state=").Append(minigun.EditorSpinState.PadRight(12))
          .Append(" spin=").Append(minigun.EditorSpinFraction.ToString("0.00", CultureInfo.InvariantCulture))
          .Append(" windup=").Append(minigun.EditorWindupSeconds.ToString("0.000", CultureInfo.InvariantCulture))
          .Append(" ammo=").Append(minigun.CurrentAmmo);

        foreach (AudioSource source in manager.GetComponents<AudioSource>())
        {
            if (!source.isPlaying || source.clip == null) continue;
            sb.Append("  [").Append(source.clip.name)
              .Append(" t=").Append(source.time.ToString("0.000", CultureInfo.InvariantCulture))
              .Append(source.loop ? " loop" : "")
              .Append(']');
        }

        sb.AppendLine();
    }

    private void OnDestroy()
    {
        if (minigun != null) minigun.EditorHoldTrigger = false;
    }
}
#endif
