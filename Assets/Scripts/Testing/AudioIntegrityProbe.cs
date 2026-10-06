#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Editor-only proof for the audio-integrity pass: nine measurements, nine NUMBERS. Added
/// at runtime through the CLI bridge, polled for <see cref="Done"/> and <see cref="Report"/>,
/// and removed again - never left in the scene.
///
/// The shape is Testing/EnemyRoleProbe's, and for the same reason: every one of these is
/// about TIME (a crossfade is two seconds, a reload is one and a half, a fuse is three) and a
/// round trip through the bridge is about a second, so the coroutine samples on the frame and
/// the bridge reads the finished report. KeepPlayerAlive is here for the same reason too - a
/// grenade and a boss can both kill the player, the death screen sets timeScale to 0, and
/// every WaitForSeconds in here would stop advancing.
///
/// Three rules this file keeps to:
///
///  1. EVERY MEASUREMENT IS INDEPENDENT. Each runs as its own coroutine, stepped by hand so
///     an exception inside one is caught and written into that one's line; the next still
///     runs. Each has a timeout, so a hang costs one line rather than the report.
///  2. EVERYTHING IS PUT BACK. Every profile field, threshold, ammo count and timeScale this
///     probe writes is restored at the end of the measurement AND registered for OnDestroy,
///     so a run that is killed mid-way still leaves the scene as it found it. The generated
///     clips are runtime AudioClip.Create objects, not assets: a scene saved while one is
///     still assigned would hold a broken reference, which is why the restore is doubled.
///  3. A MISSING MANAGER IS "SKIPPED", NEVER AN EXCEPTION. A probe that throws where the
///     scene is merely incomplete tells you nothing about the code it was pointed at.
///
/// No public AudioClip or AudioClip[] field is declared here on purpose: AudioProgressScanner
/// walks every project MonoBehaviour in the scene, and a clip slot on the probe would show up
/// in the very total measurement 7 is comparing.
/// </summary>
public class AudioIntegrityProbe : MonoBehaviour
{
    // ---- the CLI surface ------------------------------------------------------

    public bool Done { get; private set; }
    public string Report { get; private set; } = "AudioIntegrityProbe: not started";

    [Tooltip("The edit-mode Scan().Total the integrator measured before play. 0 = not supplied, " +
             "and measurement 7 then compares the two play-mode totals only.")]
    public int editModeReportTotal = 0;

    [Tooltip("The total all three readings should equal. 0 = do not check the value, only that " +
             "the readings agree.")]
    public int expectedReportTotal = 244;

    [Tooltip("Boss to spawn for the pitch measurement. Left empty, the first prefab under " +
             "Assets/Inbox/Prefabs/Enemies carrying a BossZombieController is used.")]
    public GameObject bossPrefab;

    [Tooltip("Target for the bolts. Left empty, the first roled-or-plain zombie prefab that is " +
             "not the boss is used.")]
    public GameObject zombiePrefab;

    [Tooltip("Metres in front of the player to stand the bolt target.")]
    public float targetDistance = 6f;

    [Tooltip("Metres in front of the player to spawn the boss.")]
    public float bossDistance = 14f;

    public int boltCount = 30;
    public int grenadeCount = 10;
    public int noRepeatCalls = 50;

    [Tooltip("How long to wait for every bolt to come back to the pool. An arrow that sticks " +
             "in the ground is on Arrow.stickDuration's clock - 10 s by default.")]
    public float boltReturnTimeoutSeconds = 13f;

    // ---- internals ------------------------------------------------------------

    private const string ProbeVoiceName = "AUDIO_PROBE_VOICE";
    private const string ProbeBossName = "AUDIO_PROBE_BOSS";
    private const string ProbeTargetName = "AUDIO_PROBE_TARGET";
    private const string ProbeGrenadeName = "AUDIO_PROBE_GRENADE";

    private readonly string[] results = new string[10];
    private readonly List<Action> restores = new List<Action>();
    private readonly StringBuilder notes = new StringBuilder();

    private Transform player;
    private float t0;
    private bool cleaned;

    // KeepPlayerAlive's two switches. holdTimeScale lets measurement 1 freeze the clock
    // without the top-up putting it straight back; pinHealth (>= 0) holds the player at a
    // chosen health instead of full, which is the whole of measurement 8.
    private bool holdTimeScale;
    private float pinHealth = -1f;

    private int errorCount;
    private string firstError = "";

    private GameObject bossInstance;
    private GameObject targetInstance;
    private AudioSource probeVoice;

    private int totalBeforeBoss = -1;
    private int totalWithBoss = -1;

    // ---- lifecycle ------------------------------------------------------------

    private void OnEnable()
    {
        // The console cursor: everything from the moment the component lands.
        Application.logMessageReceived += OnLog;
    }

    private void OnDisable()
    {
        Application.logMessageReceived -= OnLog;
    }

    private void OnLog(string condition, string stackTrace, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        errorCount++;
        if (string.IsNullOrEmpty(firstError)) firstError = Short(condition, 140);
    }

    private IEnumerator Start()
    {
        t0 = Time.unscaledTime;
        RefreshReport("running");

        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
        player = playerObject != null ? playerObject.transform : null;
        if (player == null)
        {
            Note("no GameObject tagged 'Player' - measurements needing one read SKIPPED");
        }

        StartCoroutine(KeepPlayerAlive());

        yield return Guarded(1, M1Crossfade(), 25f);
        yield return Guarded(3, M3FootstepGain(), 10f);
        yield return Guarded(4, M4NoRepeat(), 20f);
        yield return Guarded(5, M5Reload(), 30f);
        yield return Guarded(6, M6Projectiles(), boltReturnTimeoutSeconds + 45f);
        yield return Guarded(7, M7ScanBeforeBoss(), 40f);
        yield return Guarded(2, M2BossPitch(), 45f);
        yield return Guarded(7, M7ScanWithBoss(), 40f);

        DestroySubjects();
        yield return null;

        yield return Guarded(8, M8HeartbeatAtZero(), 30f);
        yield return Guarded(9, M9Errors(), 5f);

        Cleanup();
        RefreshReport("done");
        Done = true;
    }

    private void OnDestroy()
    {
        Cleanup();
        DestroySubjects();
    }

    /// <summary>
    /// Every write this probe made, undone, newest first, each on its own so one failure
    /// cannot strand the rest. Safe to call twice - a restore writes the value it read.
    /// </summary>
    private void Cleanup()
    {
        if (cleaned) return;
        cleaned = true;

        for (int i = restores.Count - 1; i >= 0; i--)
        {
            try { restores[i]?.Invoke(); }
            catch (Exception e) { Note("restore " + i + " threw: " + Short(e.Message, 80)); }
        }
        restores.Clear();

        holdTimeScale = false;
        pinHealth = -1f;
        if (Time.timeScale < 1f) Time.timeScale = 1f;

        if (probeVoice != null) Destroy(probeVoice.gameObject);
    }

    private void DestroySubjects()
    {
        foreach (string name in new[] { ProbeBossName, ProbeTargetName })
        {
            GameObject leftover = GameObject.Find(name);
            if (leftover != null) Destroy(leftover);
        }
        bossInstance = null;
        targetInstance = null;
    }

    // ---- the guarded runner ---------------------------------------------------

    /// <summary>
    /// Runs one measurement to completion, catching whatever it throws and timing out if it
    /// hangs. The body is stepped BY HAND so the try/catch can wrap MoveNext - a coroutine
    /// cannot catch around a yield, and a measurement that took the report down with it
    /// would defeat the point of nine independent ones. Bodies therefore never nest
    /// (`yield return SomeOtherRoutine()`): a nested enumerator is stepped by Unity, outside
    /// this catch.
    /// </summary>
    private IEnumerator Guarded(int tag, IEnumerator body, float timeoutSeconds)
    {
        bool finished = false;
        float until = Time.unscaledTime + timeoutSeconds;

        Coroutine running = StartCoroutine(Step(tag, body, () => finished = true));

        while (!finished && Time.unscaledTime < until) yield return null;

        if (!finished)
        {
            // Stopped, not left running: a measurement still stepping in the background would
            // go on writing over its own FAIL line and changing the scene under the next one.
            StopCoroutine(running);
            Result(tag, $"FAIL no result after {timeoutSeconds:F0} s (hung or threw outside the step loop)");

            // Whatever it froze, the rest of the run needs the clock back.
            holdTimeScale = false;
            if (Time.timeScale < 1f) Time.timeScale = 1f;
        }

        RefreshReport("running");
    }

    private IEnumerator Step(int tag, IEnumerator body, Action onDone)
    {
        while (true)
        {
            object current;
            try
            {
                if (!body.MoveNext()) break;
                current = body.Current;
            }
            catch (Exception e)
            {
                Result(tag, "FAIL exception: " + Short(e.GetType().Name + " " + e.Message, 160));
                break;
            }

            yield return current;
        }

        onDone();
    }

    // ---- 1. the crossfade finishes under a frozen clock -----------------------

    private IEnumerator M1Crossfade()
    {
        AmbienceManager ambience = FindFirstObjectByType<AmbienceManager>(FindObjectsInactive.Include);
        if (ambience == null || ambience.ambienceTracks == null || ambience.ambienceTracks.Length == 0)
        {
            Result(1, "SKIPPED: no AmbienceManager with tracks");
            yield break;
        }

        AmbienceManager.AmbienceState from = ambience.GetCurrentState();
        AmbienceManager.AmbienceState to = from;
        bool found = false;

        foreach (AmbienceManager.AmbienceTrack track in ambience.ambienceTracks)
        {
            if (track == null || track.source == null || track.state == from) continue;
            to = track.state;
            found = true;
            break;
        }

        if (!found)
        {
            Result(1, $"SKIPPED: only one ambience state authored ({from})");
            yield break;
        }

        restores.Add(() =>
        {
            holdTimeScale = false;
            if (Time.timeScale < 1f) Time.timeScale = 1f;
            ambience.ChangeState(from, true);
        });

        ambience.ChangeState(to);

        // 0.3 s in - far enough for the fade to be mid-flight, and the moment the audit
        // says a death screen or a pause menu used to strand it.
        holdTimeScale = true;
        yield return new WaitForSecondsRealtime(0.3f);
        Time.timeScale = 0f;

        float wait = Mathf.Max(3f, ambience.crossFadeDuration + 1.5f);
        yield return new WaitForSecondsRealtime(wait);

        float oldMax = 0f;
        float newWorstGap = 0f;
        int newTracks = 0;

        foreach (AmbienceManager.AmbienceTrack track in ambience.ambienceTracks)
        {
            if (track == null || track.source == null) continue;

            if (track.state == to)
            {
                newTracks++;
                newWorstGap = Mathf.Max(newWorstGap, Mathf.Abs(track.source.volume - track.volume));
            }
            else
            {
                oldMax = Mathf.Max(oldMax, track.source.volume);
            }
        }

        Time.timeScale = 1f;
        holdTimeScale = false;
        ambience.ChangeState(from, true);

        bool pass = oldMax <= 0.001f && newWorstGap <= 0.01f;
        Result(1, $"{Verdict(pass)} crossfade under timeScale=0 {from}->{to} after {wait:F1} s real: " +
                  $"old beds max {oldMax:F3} (want 0.000), new beds worst gap to target {newWorstGap:F3} " +
                  $"(want 0.000, {newTracks} track(s))");
    }

    // ---- 2. the boss keeps its pitch ------------------------------------------

    private IEnumerator M2BossPitch()
    {
        EnemyAudioManager enemyAudio = FindFirstObjectByType<EnemyAudioManager>(FindObjectsInactive.Include);
        if (enemyAudio == null) { Result(2, "SKIPPED: no EnemyAudioManager"); yield break; }
        if (player == null) { Result(2, "SKIPPED: no Player to spawn the boss in front of"); yield break; }

        // The registry lists the boss the level actually uses; the first prefab under
        // Inbox/Prefabs/Enemies alphabetically is a Fort Golem no wave spawns and no profile names.
        GameObject prefab = bossPrefab != null ? bossPrefab : FindRegisteredPrefabWith<BossZombieController>();
        if (prefab == null) prefab = FindPrefabWith<BossZombieController>();
        if (prefab == null) { Result(2, "SKIPPED: no prefab with a BossZombieController"); yield break; }

        bossInstance = SpawnInFront(prefab, bossDistance, ProbeBossName);
        if (bossInstance == null) { Result(2, "SKIPPED: boss failed to instantiate"); yield break; }

        // Start has to have run: it is what resolves the voice and sets the phase watch going.
        yield return new WaitForSeconds(1f);

        BossZombieController boss = bossInstance.GetComponent<BossZombieController>();
        ZombieAI ai = bossInstance.GetComponent<ZombieAI>();
        AudioSource voice = bossInstance.GetComponent<AudioSource>();   // BossZombieController.voice

        if (boss == null || voice == null)
        {
            Result(2, "SKIPPED: the boss instance has no controller or no root AudioSource");
            yield break;
        }

        string key = ai != null ? ai.ProfileKey : null;
        if (string.IsNullOrEmpty(key))
        {
            Result(2, "SKIPPED: the boss has no ZombieAI profile key to look its sounds up by");
            yield break;
        }

        // Force the phase rather than fight the boss for it: phasesTriggered is what
        // PhaseWatch counts with, and the pitch it writes is pow(perPhase, phasesTriggered).
        // Writing the count as well as the pitch stops PhaseWatch re-triggering phase 1 and
        // quietly re-writing the number under the measurement.
        float perPhase = GetFloat(boss, "phasePitchScalePerPhase", 1.08f);
        bool forced = SetField(boss, "phasesTriggered", 1);
        float wanted = Mathf.Pow(perPhase, 1);
        voice.pitch = wanted;

        // PlayRandomOneShot returns before it touches the pitch when the slot is empty, so an
        // unauthored attack array would read as a pass without ever exercising anything.
        EnemyAudioManager.EnemySoundProfile profile = FindEnemyProfile(enemyAudio, key);
        if (profile == null)
        {
            Result(2, $"SKIPPED: no EnemySoundProfile named '{key}' for the boss");
            yield break;
        }

        AudioClip[] attackWas = profile.attackSounds;
        bool substituted = !HasClip(profile.attackSounds);
        if (substituted)
        {
            // Same LENGTH wherever possible: an array's element count is rows in the progress
            // report, and measurement 7 scans again a moment after this.
            int length = attackWas != null && attackWas.Length > 0 ? attackWas.Length : 1;
            AudioClip[] generated = new AudioClip[length];
            for (int i = 0; i < length; i++) generated[i] = Tone("PROBE_BOSS_ATTACK", 0.1f, 300f, 0.02f);
            profile.attackSounds = generated;
            restores.Add(() => profile.attackSounds = attackWas);
        }

        yield return null;

        // Set, read, play and read again in ONE frame. The vocalisation loop plays through
        // this same source every few seconds and resets the pitch on the way past - which is
        // the same defect from the other end - so anything spread over frames would be a
        // lottery rather than a measurement of PlayAttackSound.
        voice.pitch = wanted;
        float before = voice.pitch;
        enemyAudio.PlayAttackSound(key, voice);
        float after = voice.pitch;

        bool played = false;
        foreach (AudioEventEntry entry in AudioEventLog.Recent(1))
        {
            played = entry.Source == bossInstance.name
                     && (!substituted || (entry.Detail != null &&
                                          entry.Detail.IndexOf("PROBE_BOSS_ATTACK", StringComparison.Ordinal) >= 0));
        }

        // Back before measurement 7 scans again; the registered restore is the backstop for a
        // run that is killed between here and there.
        profile.attackSounds = attackWas;

        if (!forced) Note("[2] BossZombieController.phasesTriggered not found - the phase was not forced");

        bool pass = played && Mathf.Abs(after - before) <= 0.001f && before > 1.001f;
        Result(2, $"{Verdict(pass)} boss voice pitch {before:F3} -> {after:F3} across PlayAttackSound " +
                  $"(phase 1 forced to {wanted:F3}; {(substituted ? "generated clip" : "authored clips")}; " +
                  $"played={played})");
    }

    // ---- 3. footstep gain lives on the voice, not the source ------------------

    private IEnumerator M3FootstepGain()
    {
        Audio.FootstepAudioManager footsteps =
            FindFirstObjectByType<Audio.FootstepAudioManager>(FindObjectsInactive.Include);
        if (footsteps == null) { Result(3, "SKIPPED: no FootstepAudioManager"); yield break; }

        AudioSource source = GetField(footsteps, "audioSource") as AudioSource;
        if (source == null) { Result(3, "SKIPPED: FootstepAudioManager has no audioSource field/value"); yield break; }

        // A MARKER rather than the resting value: the claim is that the manager no longer
        // writes the source's volume at all, and a jumpVolume that happens to equal what the
        // source already holds would let the old behaviour pass. 0.375 is not a value anybody
        // drags a slider to.
        const float marker = 0.375f;
        float authored = source.volume;
        restores.Add(() => { if (source != null) source.volume = authored; });

        source.volume = marker;
        footsteps.PlayJumpSound();
        yield return null;
        float afterJump = source.volume;

        source.volume = marker;
        footsteps.PlayLandSound();
        yield return null;
        float afterLand = source.volume;

        source.volume = authored;

        bool pass = Mathf.Abs(afterJump - marker) <= 0.001f && Mathf.Abs(afterLand - marker) <= 0.001f;
        Result(3, $"{Verdict(pass)} footstep source volume: marker {marker:F3} -> jump {afterJump:F3}, " +
                  $"marker -> land {afterLand:F3} (want the marker held - the gain belongs on the voice); " +
                  $"source rests at {authored:F3}");
    }

    // ---- 4. the no-repeat actually remembers ----------------------------------

    private IEnumerator M4NoRepeat()
    {
        EnemyAudioManager enemyAudio = FindFirstObjectByType<EnemyAudioManager>(FindObjectsInactive.Include);
        if (enemyAudio == null) { Result(4, "SKIPPED: no EnemyAudioManager"); yield break; }

        EnemyAudioManager.EnemySoundProfile profile = FirstNamedEnemyProfile(enemyAudio);
        if (profile == null) { Result(4, "SKIPPED: no named EnemySoundProfile to borrow"); yield break; }

        AudioClip[] original = profile.attackSounds;
        AudioClip[] generated = { Tone("PROBE_TONE_A", 0.05f, 440f, 0.02f), Tone("PROBE_TONE_B", 0.05f, 660f, 0.02f) };
        profile.attackSounds = generated;
        restores.Add(() => profile.attackSounds = original);

        AudioSource voice = ProbeSource();
        yield return null;

        // One frame, so nothing else can interleave in the 128-slot ring, and silent
        // (source volume 0) so 50 attack sounds are a measurement rather than a noise.
        for (int i = 0; i < noRepeatCalls; i++)
        {
            enemyAudio.PlayAttackSound(profile.enemyName, voice);
        }

        int seen = 0;
        int repeats = 0;
        string last = null;
        foreach (AudioEventEntry entry in OldestFirst(AudioEventLog.Capacity))
        {
            if (entry.Source != ProbeVoiceName) continue;
            if (last != null && entry.Detail == last) repeats++;
            last = entry.Detail;
            seen++;
        }

        profile.attackSounds = original;

        bool pass = seen >= noRepeatCalls / 2 && repeats == 0;
        Result(4, $"{Verdict(pass)} no-repeat on '{profile.enemyName}' attackSounds: {seen} plays logged of " +
                  $"{noRepeatCalls}, {repeats} consecutive repeats (want 0; a dead memory averages ~{noRepeatCalls / 2})");
    }

    // ---- 5. reload start, action and end, in that order -----------------------

    private IEnumerator M5Reload()
    {
        WeaponAudioManager weaponAudio = FindFirstObjectByType<WeaponAudioManager>(FindObjectsInactive.Include);
        if (weaponAudio == null) { Result(5, "SKIPPED: no WeaponAudioManager"); yield break; }

        WeaponController weapon = FindMagazineWeapon();
        if (weapon == null) { Result(5, "SKIPPED: no WeaponController with a magazine in the scene"); yield break; }

        WeaponAudioManager.WeaponSoundProfile profile = FindWeaponProfile(weaponAudio, weapon.ProfileName);
        if (profile == null)
        {
            Result(5, $"SKIPPED: no WeaponSoundProfile named '{weapon.ProfileName}' for {weapon.name}");
            yield break;
        }

        AudioClip startClip = Tone("PROBE_RELOAD_START", 0.2f, 300f, 0.05f);
        AudioClip endClip = Tone("PROBE_RELOAD_END", 0.2f, 500f, 0.05f);
        AudioClip startWas = profile.reloadStartSound;
        AudioClip endWas = profile.reloadEndSound;
        profile.reloadStartSound = startClip;
        profile.reloadEndSound = endClip;
        restores.Add(() =>
        {
            profile.reloadStartSound = startWas;
            profile.reloadEndSound = endWas;
        });

        // The weapon has to be awake, enabled and raised for TryReload to start a coroutine
        // at all. All three are put back below.
        bool wasActive = weapon.gameObject.activeSelf;
        bool wasEnabled = weapon.enabled;
        object raisedWas = GetField(weapon, "isWeaponRaised");
        object ammoWas = GetField(weapon, "currentAmmo");
        restores.Add(() =>
        {
            if (ammoWas != null) SetField(weapon, "currentAmmo", ammoWas);
            if (raisedWas != null) SetField(weapon, "isWeaponRaised", raisedWas);
            if (weapon != null)
            {
                weapon.enabled = wasEnabled;
                weapon.gameObject.SetActive(wasActive);
            }
        });

        if (!wasActive) weapon.gameObject.SetActive(true);
        weapon.enabled = true;
        yield return null;   // let Start run on a weapon that has never been enabled

        SetField(weapon, "isWeaponRaised", true);
        SetField(weapon, "currentAmmo", 0);

        float reloadTime = GetFloat(weapon, "reloadTime", 1.5f);
        float began = Time.unscaledTime;
        weapon.TryReload();

        yield return new WaitForSeconds(reloadTime + 1.25f);

        float startAt = LogTimeOf("PROBE_RELOAD_START", AudioEventLog.Capacity);
        float endAt = LogTimeOf("PROBE_RELOAD_END", AudioEventLog.Capacity);
        float actionAt = LogTimeOfAny(ClipNames(profile.reloadActionSounds), AudioEventLog.Capacity);

        // Restore before anything else reads the profile.
        profile.reloadStartSound = startWas;
        profile.reloadEndSound = endWas;
        if (ammoWas != null) SetField(weapon, "currentAmmo", ammoWas);
        if (raisedWas != null) SetField(weapon, "isWeaponRaised", raisedWas);
        weapon.enabled = wasEnabled;
        weapon.gameObject.SetActive(wasActive);

        bool haveStart = startAt >= 0f;
        bool haveEnd = endAt >= 0f;
        float gap = haveStart && haveEnd ? endAt - startAt : -1f;
        bool ordered = haveStart && haveEnd && endAt >= startAt;
        bool longEnough = gap >= reloadTime - 0.06f;

        string action = actionAt >= 0f
            ? $"action +{actionAt - began:F2}"
            : "action not logged (PlayReloadActionSound records nothing)";

        Result(5, $"{Verdict(ordered && longEnough)} reload on '{weapon.ProfileName}': " +
                  $"start {(haveStart ? "+" + (startAt - began).ToString("F2", CultureInfo.InvariantCulture) : "MISSING")}, " +
                  $"{action}, " +
                  $"end {(haveEnd ? "+" + (endAt - began).ToString("F2", CultureInfo.InvariantCulture) : "MISSING")}; " +
                  $"end-start {gap:F2} s vs reloadTime {reloadTime:F2} s");
    }

    // ---- 6. thirty bolts and ten grenades, quietly ----------------------------

    private IEnumerator M6Projectiles()
    {
        if (player == null) { Result(6, "SKIPPED: no Player to fire from"); yield break; }

        int errorsBefore = errorCount;
        string detail = "";

        // The target, so the bolts have something to hit that is not the sky.
        // Not just "the first non-boss prefab": Inbox/Prefabs/Enemies holds six zombies no
        // wave spawns and no profile names (the Fort Golem is first alphabetically), and a
        // target with no profile logs a Miss on every vocalisation under the measurement.
        GameObject targetPrefab = zombiePrefab != null ? zombiePrefab : FindProfiledPrefabWithout<BossZombieController>();
        if (targetPrefab == null) targetPrefab = FindPrefabWithout<BossZombieController>();
        if (targetPrefab != null) targetInstance = SpawnInFront(targetPrefab, targetDistance, ProbeTargetName);

        // ---- bolts, through the crossbow so the shooter-ignore is the thing being tested
        CrossbowController crossbow = FindFirstObjectByType<CrossbowController>(FindObjectsInactive.Include);
        int fired = 0;
        string boltPath;

        if (ArrowPool.Instance == null)
        {
            boltPath = "no ArrowPool - bolts skipped";
        }
        else if (crossbow != null && GetField(crossbow, "firePoint") as Transform != null)
        {
            bool wasActive = crossbow.gameObject.activeSelf;
            bool wasEnabled = crossbow.enabled;
            object raisedWas = GetField(crossbow, "isWeaponRaised");
            object drawWas = GetField(crossbow, "drawTime");
            object rateWas = GetField(crossbow, "fireRate");
            restores.Add(() =>
            {
                if (crossbow == null) return;
                if (drawWas != null) SetField(crossbow, "drawTime", drawWas);
                if (rateWas != null) SetField(crossbow, "fireRate", rateWas);
                if (raisedWas != null) SetField(crossbow, "isWeaponRaised", raisedWas);
                SetField(crossbow, "isDrawing", false);
                crossbow.enabled = wasEnabled;
                crossbow.gameObject.SetActive(wasActive);
            });

            if (!wasActive) crossbow.gameObject.SetActive(true);
            crossbow.enabled = true;
            yield return null;

            SetField(crossbow, "isWeaponRaised", true);
            SetField(crossbow, "drawTime", 0f);      // a 1 s draw x 30 is half a minute of nothing
            SetField(crossbow, "fireRate", 0f);

            for (int i = 0; i < boltCount; i++)
            {
                SetField(crossbow, "nextTimeToFire", 0f);
                // Both calls in ONE frame: Update clears isDrawing the moment the mouse
                // button is seen to be up, which it always is here.
                crossbow.Fire();
                crossbow.Fire();
                fired++;
                yield return new WaitForSeconds(0.05f);
            }

            SetField(crossbow, "isDrawing", false);
            if (drawWas != null) SetField(crossbow, "drawTime", drawWas);
            if (rateWas != null) SetField(crossbow, "fireRate", rateWas);
            if (raisedWas != null) SetField(crossbow, "isWeaponRaised", raisedWas);
            crossbow.enabled = wasEnabled;
            crossbow.gameObject.SetActive(wasActive);

            boltPath = "via CrossbowController";
        }
        else
        {
            // No crossbow in the scene: the pool still gets exercised, but the shooter-ignore
            // lives in CrossbowController and is NOT tested on this path. Said so in the line.
            Vector3 from = Head();
            Vector3 dir = Forward();
            for (int i = 0; i < boltCount; i++)
            {
                Arrow arrow = ArrowPool.Instance.GetArrow();
                if (arrow == null) break;
                arrow.transform.position = from + dir * 0.5f;
                arrow.transform.rotation = Quaternion.LookRotation(dir);
                Rigidbody rb = arrow.GetComponent<Rigidbody>();
                if (rb != null) rb.AddForce(dir * 20f, ForceMode.Impulse);
                fired++;
                yield return new WaitForSeconds(0.05f);
            }
            boltPath = "via ArrowPool (no crossbow: shooter-ignore NOT exercised)";
        }

        yield return new WaitForSeconds(0.5f);

        int parented = 0;
        int nearPlayer = 0;
        foreach (Arrow arrow in FindObjectsByType<Arrow>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (arrow == null || !arrow.gameObject.activeInHierarchy) continue;
            if (IsUnder(arrow.transform, player)) parented++;
            if (Vector3.Distance(arrow.transform.position, player.position) < 1.2f) nearPlayer++;
        }

        // Bolts come home two ways: straight away off a damageable, or after
        // Arrow.stickDuration off a wall. Both count as returned.
        float until = Time.unscaledTime + boltReturnTimeoutSeconds;
        int stillOut = CountActiveArrows();
        while (stillOut > 0 && Time.unscaledTime < until)
        {
            yield return new WaitForSeconds(0.25f);
            stillOut = CountActiveArrows();
        }
        float tookSeconds = boltReturnTimeoutSeconds - (until - Time.unscaledTime);

        // ---- grenades, bounced off whatever is in front of the player
        int thrown = 0;
        GameObject grenadePrefab = FindGrenadePrefab();
        WeaponAudioManager weaponAudio = FindFirstObjectByType<WeaponAudioManager>(FindObjectsInactive.Include);

        if (grenadePrefab == null)
        {
            detail += " no grenade prefab;";
        }
        else
        {
            Collider[] ignore = player.GetComponentsInChildren<Collider>(true);
            string key = FindGrenadeAudioKey();
            Vector3 from = Head();
            Vector3 dir = Forward();

            for (int i = 0; i < grenadeCount; i++)
            {
                GameObject spawned = Instantiate(grenadePrefab, from + dir * 1.2f, Quaternion.identity);
                spawned.name = ProbeGrenadeName;
                Grenade grenade = spawned.GetComponent<Grenade>();
                if (grenade == null) { Destroy(spawned); break; }

                grenade.Launch(dir * 14f + Vector3.up * 2f, ignore, weaponAudio, key);
                thrown++;
                yield return new WaitForSeconds(0.2f);
            }

            // The fuse is 3 s by default; give the last one its blast and its ring.
            yield return new WaitForSeconds(4.5f);
        }

        int newErrors = errorCount - errorsBefore;
        // Half of this can be legitimately absent (no pool, no grenade prefab) and the line
        // says which; what must never happen is an exception, a bolt on the player, or a bolt
        // that never came home.
        bool pass = newErrors == 0 && parented == 0 && stillOut == 0 && (fired > 0 || thrown > 0);

        Result(6, $"{Verdict(pass)} projectiles: {fired} bolts {boltPath}, {thrown} grenades; " +
                  $"errors +{newErrors} (want 0), bolts under the player {parented} (want 0), " +
                  $"resting within 1.2 m of the player {nearPlayer}, still out of the pool {stillOut} " +
                  $"after {tookSeconds:F1} s (want 0).{detail}");
    }

    // ---- 7. the report reads the same three ways -----------------------------

    private IEnumerator M7ScanBeforeBoss()
    {
        totalBeforeBoss = AudioProgressScanner.Scan().Total;
        Result(7, $"RUNNING report parity: play-mode before the boss = {totalBeforeBoss}");
        yield break;
    }

    private IEnumerator M7ScanWithBoss()
    {
        totalWithBoss = AudioProgressScanner.Scan().Total;
        bool bossAlive = bossInstance != null;

        bool agree = totalBeforeBoss > 0 && totalBeforeBoss == totalWithBoss;
        bool editAgrees = editModeReportTotal <= 0 || editModeReportTotal == totalBeforeBoss;
        bool expected = expectedReportTotal <= 0 || totalWithBoss == expectedReportTotal;

        Result(7, $"{Verdict(agree && editAgrees && expected)} report Total: " +
                  $"edit={(editModeReportTotal > 0 ? editModeReportTotal.ToString() : "n/a")}, " +
                  $"play-before-boss={totalBeforeBoss}, play-with-boss={totalWithBoss} " +
                  $"(boss alive={bossAlive}; expected {(expectedReportTotal > 0 ? expectedReportTotal.ToString() : "n/a")})");
        yield break;
    }

    // ---- 8. the heartbeat at a threshold of zero ------------------------------

    private IEnumerator M8HeartbeatAtZero()
    {
        PlayerAudioManager playerAudio = FindFirstObjectByType<PlayerAudioManager>(FindObjectsInactive.Include);
        if (playerAudio == null) { Result(8, "SKIPPED: no PlayerAudioManager"); yield break; }
        if (PlayerStats.Instance == null) { Result(8, "SKIPPED: no PlayerStats.Instance"); yield break; }

        PlayerAudioManager.PlayerSoundProfile profile = FindHealthProfile(playerAudio);
        if (profile == null) { Result(8, "SKIPPED: no Health PlayerSoundProfile"); yield break; }

        AudioClip loopWas = profile.lowHealthLoop;
        if (profile.lowHealthLoop == null)
        {
            // UpdateLowHealthState returns on a null loop, so without this the measurement
            // never reaches the division it exists to test.
            profile.lowHealthLoop = Tone("PROBE_HEARTBEAT", 0.4f, 90f, 0.02f);
            restores.Add(() => profile.lowHealthLoop = loopWas);
        }

        float thresholdWas = profile.playBelowThisHealth;
        restores.Add(() =>
        {
            profile.playBelowThisHealth = thresholdWas;
            pinHealth = -1f;
        });

        // Health pinned at 1 by KeepPlayerAlive - low enough for the loop to start against
        // the authored threshold, and never 0, which would take the death path with it.
        pinHealth = 1f;
        yield return null;
        yield return null;

        playerAudio.UpdateLowHealthState(true);
        yield return new WaitForSeconds(0.4f);

        // The loop has to be RUNNING for the threshold to be divided by at all - a
        // measurement taken against a loop that never started would pass without touching the
        // line it exists to test.
        bool looping = GetField(playerAudio, "isLowHealthPlaying") is bool b && b;
        if (!looping)
        {
            profile.lowHealthLoop = loopWas;
            pinHealth = -1f;
            Result(8, $"SKIPPED: the heartbeat loop would not start at 1 hp " +
                      $"(playBelowThisHealth {thresholdWas:F1}, loop clip " +
                      $"{(loopWas != null ? "authored" : "generated")})");
            yield break;
        }

        // Now the latent: the slider at 0, with the loop already running and re-deriving its
        // volume from currentHealth / playBelowThisHealth ten times a second.
        int errorsBefore = errorCount;
        profile.playBelowThisHealth = 0f;
        yield return new WaitForSeconds(1.2f);

        bool stillLooping = GetField(playerAudio, "isLowHealthPlaying") is bool c && c;

        int bad = 0;
        int scanned = 0;
        foreach (AudioSource source in FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (source == null) continue;
            scanned++;
            if (float.IsNaN(source.volume) || float.IsInfinity(source.volume)) bad++;
        }

        AudioSource beat = profile.heartbeatAudioSource != null
            ? profile.heartbeatAudioSource
            : GetField(playerAudio, "defaultAudioSource") as AudioSource;
        float beatVolume = beat != null ? beat.volume : -1f;

        int newErrors = errorCount - errorsBefore;

        profile.playBelowThisHealth = thresholdWas;
        profile.lowHealthLoop = loopWas;
        pinHealth = -1f;
        yield return null;
        playerAudio.UpdateLowHealthState(false);
        playerAudio.StopLowHealthLoop();

        bool pass = bad == 0 && newErrors == 0;
        Result(8, $"{Verdict(pass)} heartbeat with playBelowThisHealth=0 at 1 hp: " +
                  $"{bad} of {scanned} AudioSources NaN/Inf (want 0), errors +{newErrors} (want 0), " +
                  $"heartbeat source volume {beatVolume:F3}, loop still running={stillLooping}");
    }

    // ---- 9. the console since the cursor -------------------------------------

    private IEnumerator M9Errors()
    {
        bool pass = errorCount == 0;
        Result(9, $"{Verdict(pass)} console errors since the probe started: {errorCount}" +
                  (pass ? "" : $"; first: {firstError}"));
        yield break;
    }

    // ---- the player, kept alive ----------------------------------------------

    /// <summary>
    /// Holds the player un-dead for as long as the probe runs, and puts timeScale back if a
    /// death screen got in first - a grenade at the player's feet and a boss slam both kill,
    /// and a frozen clock stops every WaitForSeconds in this file. Two switches:
    /// holdTimeScale, so measurement 1 can freeze the clock deliberately, and pinHealth, so
    /// measurement 8 can hold the player at 1 hp instead of full.
    /// </summary>
    private IEnumerator KeepPlayerAlive()
    {
        FieldInfo health = typeof(PlayerStats).GetField("currentHealth", BindingFlags.NonPublic | BindingFlags.Instance);
        FieldInfo dead = typeof(PlayerStats).GetField("isDead", BindingFlags.NonPublic | BindingFlags.Instance);

        while (!Done)
        {
            PlayerStats stats = PlayerStats.Instance;
            if (stats != null && health != null && dead != null)
            {
                dead.SetValue(stats, false);
                health.SetValue(stats, pinHealth >= 0f ? pinHealth : stats.GetMaxHealth());
            }

            if (!holdTimeScale && Time.timeScale < 1f) Time.timeScale = 1f;

            yield return null;
        }
    }

    // ---- scene helpers --------------------------------------------------------

    private Vector3 Head()
    {
        return player != null ? player.position + Vector3.up * 1.5f : Vector3.zero;
    }

    private Vector3 Forward()
    {
        Camera camera = Camera.main;
        Vector3 forward = camera != null ? camera.transform.forward : (player != null ? player.forward : Vector3.forward);
        forward.y = 0f;
        return forward.sqrMagnitude < 0.001f ? Vector3.forward : forward.normalized;
    }

    /// <summary>On the NavMesh in front of the player, as EnemyRoleProbe places its subject.</summary>
    private GameObject SpawnInFront(GameObject prefab, float distance, string name)
    {
        if (prefab == null || player == null) return null;

        Vector3 wanted = player.position + Forward() * distance;
        Vector3 at = wanted;
        if (NavMesh.SamplePosition(wanted, out NavMeshHit hit, 8f, NavMesh.AllAreas)) at = hit.position;

        GameObject spawned = Instantiate(prefab, at, Quaternion.LookRotation(player.position - at));
        spawned.name = name;
        spawned.tag = "Enemy";
        return spawned;
    }

    /// <summary>The first prefab in the scene's AssessedPrefabRegistry carrying T, else null.</summary>
    private static GameObject FindRegisteredPrefabWith<T>() where T : Component
    {
        var registry = FindFirstObjectByType<AssessedPrefabRegistry>(FindObjectsInactive.Include);
        if (registry == null || registry.prefabs == null) return null;
        foreach (GameObject candidate in registry.prefabs)
            if (candidate != null && candidate.GetComponent<T>() != null) return candidate;
        return null;
    }

    private static GameObject FindPrefabWith<T>() where T : Component
    {
        foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Inbox/Prefabs/Enemies" }))
        {
            GameObject candidate = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
            if (candidate != null && candidate.GetComponent<T>() != null) return candidate;
        }
        return null;
    }

    /// <summary>
    /// The first prefab under Inbox/Prefabs/Enemies with a ZombieAI, without T, whose profile
    /// key the scene's EnemyAudioManager actually knows - i.e. a zombie the level uses.
    /// </summary>
    private GameObject FindProfiledPrefabWithout<T>() where T : Component
    {
        EnemyAudioManager manager = FindFirstObjectByType<EnemyAudioManager>(FindObjectsInactive.Include);
        if (manager == null) return null;
        foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Inbox/Prefabs/Enemies" }))
        {
            GameObject candidate = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
            if (candidate == null || candidate.GetComponent<T>() != null) continue;
            ZombieAI ai = candidate.GetComponent<ZombieAI>();
            if (ai == null || string.IsNullOrEmpty(ai.ProfileKey)) continue;
            if (FindEnemyProfile(manager, ai.ProfileKey) != null) return candidate;
        }
        return null;
    }

    private static GameObject FindPrefabWithout<T>() where T : Component
    {
        foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Inbox/Prefabs/Enemies" }))
        {
            GameObject candidate = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
            if (candidate != null && candidate.GetComponent<ZombieAI>() != null && candidate.GetComponent<T>() == null)
            {
                return candidate;
            }
        }
        return null;
    }

    /// <summary>The scene's grenade, from the controller that throws it, else from the assets.</summary>
    private GameObject FindGrenadePrefab()
    {
        GrenadeController controller = FindFirstObjectByType<GrenadeController>(FindObjectsInactive.Include);
        if (controller != null)
        {
            Grenade prefab = GetField(controller, "grenadePrefab") as Grenade;
            if (prefab != null) return prefab.gameObject;
        }

        foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:Prefab"))
        {
            GameObject candidate = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
            if (candidate != null && candidate.GetComponent<Grenade>() != null) return candidate;
        }
        return null;
    }

    private string FindGrenadeAudioKey()
    {
        GrenadeController controller = FindFirstObjectByType<GrenadeController>(FindObjectsInactive.Include);
        return controller != null ? controller.ProfileName : "Grenade";
    }

    private int CountActiveArrows()
    {
        int active = 0;
        foreach (Arrow arrow in FindObjectsByType<Arrow>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (arrow != null && arrow.gameObject.activeInHierarchy) active++;
        }
        return active;
    }

    private static bool IsUnder(Transform child, Transform ancestor)
    {
        if (child == null || ancestor == null) return false;
        for (Transform t = child.parent; t != null; t = t.parent)
        {
            if (t == ancestor) return true;
        }
        return false;
    }

    /// <summary>A silent voice of the probe's own, so 50 attack sounds are a measurement.</summary>
    private AudioSource ProbeSource()
    {
        if (probeVoice != null) return probeVoice;

        GameObject host = new GameObject(ProbeVoiceName);
        host.transform.SetParent(transform);
        probeVoice = host.AddComponent<AudioSource>();
        probeVoice.playOnAwake = false;
        probeVoice.spatialBlend = 0f;
        probeVoice.volume = 0f;      // PlayOneShot's scale multiplies this: silence, still logged
        return probeVoice;
    }

    private WeaponController FindMagazineWeapon()
    {
        WeaponController fallback = null;
        foreach (WeaponController weapon in FindObjectsByType<WeaponController>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (weapon == null || !weapon.UsesMagazine) continue;
            if (weapon is GrenadeController) continue;

            string key = weapon.ProfileName;
            if (!string.IsNullOrEmpty(key) && key.IndexOf("SMG", StringComparison.OrdinalIgnoreCase) >= 0) return weapon;
            if (fallback == null) fallback = weapon;
        }
        return fallback;
    }

    // ---- profile helpers ------------------------------------------------------

    private EnemyAudioManager.EnemySoundProfile FindEnemyProfile(EnemyAudioManager manager, string key)
    {
        if (!(GetField(manager, "enemyProfiles") is Array profiles)) return null;

        foreach (object entry in profiles)
        {
            if (!(entry is EnemyAudioManager.EnemySoundProfile profile)) continue;
            // The manager resolves a zombie's key through its id alias as well as the
            // authored name - "RockGolem" finds "Rock Golem" (AudioProfileIds) - so compare
            // with the spaces gone, the way that alias is formed.
            if (Same(profile.enemyName, key)) return profile;
            if (Same(profile.enemyName.Replace(" ", ""), key.Replace(" ", ""))) return profile;
        }
        return null;
    }

    private EnemyAudioManager.EnemySoundProfile FirstNamedEnemyProfile(EnemyAudioManager manager)
    {
        if (!(GetField(manager, "enemyProfiles") is Array profiles)) return null;

        foreach (object entry in profiles)
        {
            if (!(entry is EnemyAudioManager.EnemySoundProfile profile)) continue;
            if (!string.IsNullOrEmpty(profile.enemyName)) return profile;
        }
        return null;
    }

    private WeaponAudioManager.WeaponSoundProfile FindWeaponProfile(WeaponAudioManager manager, string key)
    {
        if (!(GetField(manager, "m_WeaponProfiles") is Array profiles)) return null;

        WeaponAudioManager.WeaponSoundProfile named = null;
        foreach (object entry in profiles)
        {
            if (!(entry is WeaponAudioManager.WeaponSoundProfile profile)) continue;
            if (Same(profile.weaponName, key)) return profile;
            if (named == null && !string.IsNullOrEmpty(profile.weaponName)
                && profile.weaponName.IndexOf("SMG", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                named = profile;
            }
        }
        return named;
    }

    private PlayerAudioManager.PlayerSoundProfile FindHealthProfile(PlayerAudioManager manager)
    {
        if (!(GetField(manager, "soundProfiles") is Array profiles)) return null;

        foreach (object entry in profiles)
        {
            if (!(entry is PlayerAudioManager.PlayerSoundProfile profile)) continue;
            if (profile.soundType == PlayerAudioManager.SoundType.Health) return profile;
        }
        return null;
    }

    private static bool Same(string a, string b)
    {
        if (a == null || b == null) return false;
        return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasClip(AudioClip[] clips)
    {
        if (clips == null) return false;
        foreach (AudioClip clip in clips)
        {
            if (clip != null) return true;
        }
        return false;
    }

    private static List<string> ClipNames(AudioClip[] clips)
    {
        var names = new List<string>();
        if (clips == null) return names;
        foreach (AudioClip clip in clips)
        {
            if (clip != null) names.Add(clip.name);
        }
        return names;
    }

    // ---- the event log --------------------------------------------------------

    private static List<AudioEventEntry> OldestFirst(int max)
    {
        var list = new List<AudioEventEntry>();
        foreach (AudioEventEntry entry in AudioEventLog.Recent(max)) list.Add(entry);
        list.Reverse();
        return list;
    }

    /// <summary>
    /// When a row naming this clip was logged, or -1. Matched on the CLIP NAME rather than on
    /// the logging call's own wording: the probe generates the clip it is looking for, so the
    /// row is findable whatever the Record call decides to put around it. Times are
    /// AudioEventLog's, which are unscaled.
    /// </summary>
    private static float LogTimeOf(string needle, int window)
    {
        foreach (AudioEventEntry entry in AudioEventLog.Recent(window))
        {
            if (entry.Detail != null && entry.Detail.IndexOf(needle, StringComparison.Ordinal) >= 0) return entry.Time;
        }
        return -1f;
    }

    private static float LogTimeOfAny(List<string> needles, int window)
    {
        if (needles == null || needles.Count == 0) return -1f;

        foreach (AudioEventEntry entry in AudioEventLog.Recent(window))
        {
            if (entry.Detail == null) continue;
            foreach (string needle in needles)
            {
                if (entry.Detail.IndexOf(needle, StringComparison.Ordinal) >= 0) return entry.Time;
            }
        }
        return -1f;
    }

    // ---- generated clips ------------------------------------------------------

    /// <summary>
    /// A quarter-second of tone, made at runtime for a slot the profile leaves empty. Quiet
    /// on purpose (the default amplitude is -26 dBFS): the probe fires fifty of these in a
    /// frame and nobody should have to take their headphones off to run it.
    /// </summary>
    private static AudioClip Tone(string name, float seconds = 0.25f, float frequency = 440f, float amplitude = 0.05f)
    {
        const int rate = 44100;
        int samples = Mathf.Max(16, Mathf.RoundToInt(seconds * rate));

        AudioClip clip = AudioClip.Create(name, samples, 1, rate, false);
        float[] data = new float[samples];
        for (int i = 0; i < samples; i++)
        {
            data[i] = Mathf.Sin(2f * Mathf.PI * frequency * i / rate) * amplitude;
        }
        clip.SetData(data, 0);
        return clip;
    }

    // ---- reflection -----------------------------------------------------------

    /// <summary>
    /// A field by name anywhere up the hierarchy, public or not. Private-field reads are how
    /// this probe reaches a footstep source, a weapon's magazine and the boss's phase count
    /// without adding a getter to ship code for the sake of a test. Editor-only, so none of
    /// it reaches a build.
    /// </summary>
    private static FieldInfo FieldOf(Type type, string name)
    {
        for (Type current = type; current != null; current = current.BaseType)
        {
            FieldInfo field = current.GetField(name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null) return field;
        }
        return null;
    }

    private static object GetField(object target, string name)
    {
        if (target == null) return null;
        FieldInfo field = FieldOf(target.GetType(), name);
        return field != null ? field.GetValue(target) : null;
    }

    private static bool SetField(object target, string name, object value)
    {
        if (target == null) return false;
        FieldInfo field = FieldOf(target.GetType(), name);
        if (field == null) return false;

        try { field.SetValue(target, value); return true; }
        catch { return false; }
    }

    private static float GetFloat(object target, string name, float fallback)
    {
        object value = GetField(target, name);
        return value is float f ? f : fallback;
    }

    // ---- the report -----------------------------------------------------------

    private void Result(int tag, string text)
    {
        if (tag < 1 || tag >= results.Length) return;
        results[tag] = $"[{tag}] {text}";
    }

    private void Note(string text)
    {
        notes.Append("note  ").AppendLine(text);
    }

    private static string Verdict(bool pass) => pass ? "PASS" : "FAIL";

    private static string Short(string text, int max)
    {
        if (string.IsNullOrEmpty(text)) return "";
        text = text.Replace("\r", " ").Replace("\n", " ");
        return text.Length <= max ? text : text.Substring(0, max) + "...";
    }

    private void RefreshReport(string status)
    {
        var sb = new StringBuilder();
        sb.Append("AudioIntegrityProbe ").Append(status)
          .Append("  t=").Append((Time.unscaledTime - t0).ToString("0.0", CultureInfo.InvariantCulture))
          .AppendLine(" s");

        for (int i = 1; i <= 9; i++)
        {
            sb.AppendLine(results[i] ?? $"[{i}] (not run)");
        }

        if (notes.Length > 0) sb.Append(notes);

        Report = sb.ToString();
    }
}
#endif
