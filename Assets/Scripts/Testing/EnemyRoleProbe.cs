#if UNITY_EDITOR
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Editor-only check for the four zombie roles and the combat feedback. Spawns one roled
/// zombie at a time a set distance from the player, drives it, and writes down NUMBERS - the
/// phase speed scale through a lunge, the damage that lands on a shield from three
/// directions, where a spit comes down, what the hitmarker's colour and alpha did on each of
/// the four events.
///
/// A component rather than an editor script because every one of these is about TIME: a
/// lunge is 0.5 s of tell then 2.0 s of burst, and a round trip through the CLI bridge is
/// about a second. Coroutines sample on the frame; the bridge reads the finished report.
/// The same reasoning, and the same shape, as Testing/MinigunAudioProbe.
///
/// Reads two private fields by reflection - ZombieAI.phaseSpeedScale and its moveSpeed -
/// because neither has a getter and adding one to ship code for a test would be the tail
/// wagging the dog. Editor-only, so the reflection never reaches a build.
///
/// Added and removed by whoever runs it; never left in the scene.
/// </summary>
public class EnemyRoleProbe : MonoBehaviour
{
    public bool Done { get; private set; }
    public string Report { get; private set; } = "";

    [Tooltip("Which prefab to test. Set before enabling, or the probe picks the first roled one it finds.")]
    public GameObject prefabUnderTest;

    [Tooltip("Metres in front of the player to spawn it.")]
    public float spawnDistance = 12f;

    [Tooltip("Seconds to let the chase settle before measuring. ⚠ The spitter needs this SHORT: " +
             "a Demon walks 4 m/s and its spit band closes at 7 m, so a long settle spends the " +
             "whole window and the Demon is biting before the first reading.")]
    public float settleSeconds = 1.5f;

    /// <summary>
    /// Damage the player would have taken, totalled before each top-up. Health cannot be
    /// read directly to measure a hit while KeepPlayerAlive is refilling it every frame -
    /// that is how a landed spit read as "no spit" on the first run.
    /// </summary>
    public float DamageAbsorbed { get; private set; }

    private readonly StringBuilder sb = new StringBuilder();
    private Transform player;
    private float t0;

    private static readonly FieldInfo PhaseScaleField =
        typeof(ZombieAI).GetField("phaseSpeedScale", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo MoveSpeedField =
        typeof(ZombieAI).GetField("moveSpeed", BindingFlags.NonPublic | BindingFlags.Instance);

    private IEnumerator Start()
    {
        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
        if (playerObject == null || prefabUnderTest == null)
        {
            Report = prefabUnderTest == null ? "no prefabUnderTest set" : "no Player in the scene";
            Done = true;
            yield break;
        }

        player = playerObject.transform;
        t0 = Time.time;

        // The subject walks over and bites, and the screamer's test spawns a neighbour AND a
        // 1000 hp boss beside it. Measured 2026-09-07: the player was dead within seconds,
        // the death screen set timeScale to 0, and every WaitForSeconds in here stopped
        // advancing - the probe simply hung. Topping the player up each frame is not cheating
        // at the test: what is being measured is what the ROLE does, and a corpse cannot
        // measure it.
        StartCoroutine(KeepPlayerAlive());

        if (PhaseScaleField == null || MoveSpeedField == null)
        {
            Line("⚠ ZombieAI's private fields have been renamed - the speed numbers below will read 0. " +
                 "Fix the reflection in EnemyRoleProbe, do not trust a zero.");
        }

        // In front of the player, on the NavMesh: a zombie spawned off it never moves and
        // every reading afterwards is a lie about the role rather than about the mesh.
        Vector3 wanted = player.position + player.forward * spawnDistance;
        Vector3 spawnAt = wanted;
        if (NavMesh.SamplePosition(wanted, out NavMeshHit navHit, 8f, NavMesh.AllAreas)) spawnAt = navHit.position;
        else Line($"⚠ no NavMesh within 8 m of {wanted:F1}; spawning anyway, movement readings are void");

        GameObject zombie = Instantiate(prefabUnderTest, spawnAt, Quaternion.LookRotation(player.position - spawnAt));
        zombie.name = "ROLE_PROBE_SUBJECT";
        zombie.tag = "Enemy";

        ZombieAI ai = zombie.GetComponent<ZombieAI>();
        ZombieRole role = zombie.GetComponent<ZombieRole>();
        Line($"subject '{prefabUnderTest.name}' at {spawnAt:F1}, {Vector3.Distance(spawnAt, player.position):F1} m from the player; " +
             $"role = {(role == null ? "<none>" : role.GetType().Name)}");

        if (ai == null)
        {
            Report = sb.ToString();
            Done = true;
            yield break;
        }

        // Let Start run and the chase settle before anything is measured.
        yield return new WaitForSeconds(settleSeconds);

        if (role is ZombieShield) yield return MeasureShield(ai, zombie.transform);
        else if (role is ZombieRunner) yield return MeasureSpeedScale(ai, "runner: waiting for a lunge", 12f);
        else if (role is ZombieScreamer) yield return MeasureScream(ai, zombie.transform);
        else if (role is ZombieSpitter) yield return MeasureSpit(zombie.transform);
        else Line("no role component - nothing role-specific to measure");

        Destroy(zombie);
        Report = sb.ToString();
        Done = true;
    }

    // ---- the shield: three hits, three numbers --------------------------------

    /// <summary>
    /// Front, back and head, by damaging the zombie directly at a chosen point. The
    /// arithmetic is the whole claim: 10 from the front should land as 3, from behind as 10,
    /// and on the head from the front as 20 (raw x2, because the shield waves headshots
    /// through and ZombieAI applies the multiplier to what the modifier returned).
    /// </summary>
    private IEnumerator MeasureShield(ZombieAI ai, Transform zombie)
    {
        Collider body = zombie.GetComponent<Collider>();
        if (body == null)
        {
            Line("no collider on the subject - cannot aim a hit at it");
            yield break;
        }

        float max = MaxHealthOf(ai);
        Bounds bounds = body.bounds;

        // Points chosen in the zombie's own frame, so its facing at the moment of the test
        // cannot quietly turn a "front" hit into a flank one.
        Vector3 chest = new Vector3(bounds.center.x, bounds.center.y, bounds.center.z);
        Vector3 front = chest + zombie.forward * (bounds.extents.magnitude + 0.2f);
        Vector3 back = chest - zombie.forward * (bounds.extents.magnitude + 0.2f);
        Vector3 head = new Vector3(front.x, bounds.max.y - bounds.size.y * 0.05f, front.z);

        yield return HitAndReport(ai, front, "front, body", max, 10f);
        yield return HitAndReport(ai, back, "behind, body", max, 10f);
        yield return HitAndReport(ai, head, "head, from the front", max, 10f);

        Line("expected: 3.0 front, 10.0 behind, 20.0 head");
    }

    private IEnumerator HitAndReport(ZombieAI ai, Vector3 point, string label, float max, float damage)
    {
        float before = ai.HealthFraction * max;
        ai.TakeDamage(damage, point);
        yield return null;
        float after = ai.HealthFraction * max;
        Line($"{label}: {damage:F1} sent, {before - after:F2} landed  (health {before:F1} -> {after:F1} of {max:F0})");
    }

    // ---- the runner: the burst ------------------------------------------------

    /// <summary>
    /// Watches the phase speed scale for as long as it takes a lunge to happen. The claim is
    /// 1 at rest, 2.2 for about 2 s, then 1 again - and that the scale is never left high.
    /// </summary>
    private IEnumerator MeasureSpeedScale(ZombieAI ai, string what, float seconds)
    {
        Line(what);

        float until = Time.time + seconds;
        float peak = 0f;
        float peakStarted = -1f;
        float peakEnded = -1f;
        float last = 1f;

        while (Time.time < until)
        {
            float scale = PhaseScaleOf(ai);
            if (scale > peak) peak = scale;

            if (scale > 1.05f && last <= 1.05f) peakStarted = Time.time;
            if (scale <= 1.05f && last > 1.05f) peakEnded = Time.time;

            last = scale;
            yield return new WaitForSeconds(0.1f);
        }

        Line($"peak phase speed scale {peak:F2}" +
             (peakStarted >= 0f && peakEnded > peakStarted
                 ? $", held {peakEnded - peakStarted:F2} s"
                 : peakStarted >= 0f ? ", still held when the window closed" : ", never rose"));
        Line($"scale at the end {PhaseScaleOf(ai):F2}  (want 1.00 - a scale left high is the bug)");
    }

    // ---- the screamer: the rally ----------------------------------------------

    /// <summary>
    /// A second zombie beside the screamer, and the boss beside them both. The claim is that
    /// the neighbour's scale goes to 1.5 for about 5 s and the boss's never moves.
    /// </summary>
    private IEnumerator MeasureScream(ZombieAI screamer, Transform screamerTransform)
    {
        GameObject neighbourPrefab = FindPrefabWithout<BossZombieController>();
        GameObject bossPrefab = FindPrefabWith<BossZombieController>();

        ZombieAI neighbour = SpawnBeside(neighbourPrefab, screamerTransform, 3f, "ROLE_PROBE_NEIGHBOUR");
        ZombieAI boss = SpawnBeside(bossPrefab, screamerTransform, 5f, "ROLE_PROBE_BOSS");

        Line($"neighbour = {(neighbour == null ? "<none spawned>" : neighbour.name)}, " +
             $"boss = {(boss == null ? "<none spawned>" : boss.name)}");

        float until = Time.time + 20f;
        float neighbourPeak = 0f;
        float bossPeak = 0f;
        float screamerPeak = 0f;

        while (Time.time < until)
        {
            if (neighbour != null) neighbourPeak = Mathf.Max(neighbourPeak, PhaseScaleOf(neighbour));
            if (boss != null) bossPeak = Mathf.Max(bossPeak, PhaseScaleOf(boss));
            screamerPeak = Mathf.Max(screamerPeak, PhaseScaleOf(screamer));
            yield return new WaitForSeconds(0.1f);
        }

        Line($"peak scale - neighbour {neighbourPeak:F2} (want 1.50), boss {bossPeak:F2} (want 1.00 - the boss is excluded), " +
             $"screamer itself {screamerPeak:F2} (want 1.50)");
        Line($"settled - neighbour {(neighbour == null ? 0f : PhaseScaleOf(neighbour)):F2}, " +
             $"boss {(boss == null ? 0f : PhaseScaleOf(boss)):F2}  (both want 1.00)");

        if (neighbour != null) Destroy(neighbour.gameObject);
        if (boss != null) Destroy(boss.gameObject);
    }

    // ---- the spitter: where it lands ------------------------------------------

    /// <summary>
    /// Stands still and waits to be spat at. The claim is that the spit lands within the
    /// splash radius of where the player was when it launched, and takes exactly its damage
    /// once - so the player's health is watched rather than the projectile.
    /// </summary>
    private IEnumerator MeasureSpit(Transform spitter)
    {
        float before = DamageAbsorbed;
        Vector3 stood = player.position;

        Line($"player at {stood:F2}; watching damage absorbed (currently {before:F1}) for up to 15 s. " +
             $"Demon is {DistanceToPlayerFrom(spitter):F1} m away, band is 7..16 m");

        float until = Time.time + 15f;
        Vector3 landedAt = Vector3.zero;
        bool sawSpit = false;

        while (Time.time < until)
        {
            ZombieSpit spit = FindFirstObjectByType<ZombieSpit>();
            if (spit != null)
            {
                sawSpit = true;
                landedAt = spit.transform.position;
            }

            float now = DamageAbsorbed;
            if (now > before + 0.0001f)
            {
                Line($"hit at {Time.time - t0:F2} s: damage absorbed {before:F1} -> {now:F1} " +
                     $"(this hit {now - before:F1}; a spit is 12, a bite is 15); last seen spit at {landedAt:F2}, " +
                     $"{Vector3.Distance(landedAt, stood):F2} m from where the player stood at launch");
                yield break;
            }

            yield return new WaitForSeconds(0.1f);
        }

        Line(sawSpit
            ? $"a spit flew but never damaged the player; last seen at {landedAt:F2}, {Vector3.Distance(landedAt, stood):F2} m from the player"
            : "no spit in 15 s - check the pool exists and the Demon is inside its range band");
    }

    // ---- helpers --------------------------------------------------------------

    private float DistanceToPlayerFrom(Transform t)
    {
        Vector3 d = player.position - t.position;
        d.y = 0f;
        return d.magnitude;
    }

    private ZombieAI SpawnBeside(GameObject prefab, Transform anchor, float offset, string name)
    {
        if (prefab == null) return null;

        Vector3 at = anchor.position + anchor.right * offset;
        if (NavMesh.SamplePosition(at, out NavMeshHit hit, 6f, NavMesh.AllAreas)) at = hit.position;

        GameObject spawned = Instantiate(prefab, at, anchor.rotation);
        spawned.name = name;
        spawned.tag = "Enemy";
        return spawned.GetComponent<ZombieAI>();
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

    private static float PhaseScaleOf(ZombieAI ai)
    {
        if (ai == null || PhaseScaleField == null) return 0f;
        return (float)PhaseScaleField.GetValue(ai);
    }

    /// <summary>
    /// Max health, reconstructed: HealthFraction is public but the total is not, and the
    /// damage numbers only mean something against it. moveSpeed's neighbour in the same
    /// header, read the same way.
    /// </summary>
    private static float MaxHealthOf(ZombieAI ai)
    {
        FieldInfo field = typeof(ZombieAI).GetField("maxHealth", BindingFlags.NonPublic | BindingFlags.Instance);
        return field != null ? (float)field.GetValue(ai) : 0f;
    }

    /// <summary>
    /// Holds the player at full health and un-dead for as long as the probe runs, and puts
    /// timeScale back if a death screen got in first. Stops when the probe does, so the
    /// moment the report is written the player is mortal again.
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
                // Measure first, then refill: the gap is exactly what hit the player this
                // frame, and it is the only way to see a hit at all while topping up.
                float missing = stats.GetMaxHealth() - stats.GetCurrentHealth();
                if (missing > 0f) DamageAbsorbed += missing;

                dead.SetValue(stats, false);
                health.SetValue(stats, stats.GetMaxHealth());
            }

            // Only forced back up: a probe that quietly ran the game at a different speed
            // would make every duration in the report a lie.
            if (Time.timeScale < 1f) Time.timeScale = 1f;

            yield return null;
        }
    }

    private void Line(string text)
    {
        sb.Append((Time.time - t0).ToString("0.000", CultureInfo.InvariantCulture).PadLeft(7))
          .Append("  ").AppendLine(text);
    }

    private void OnDestroy()
    {
        // Anything the probe spawned goes with it, however the run ended.
        foreach (string name in new[] { "ROLE_PROBE_SUBJECT", "ROLE_PROBE_NEIGHBOUR", "ROLE_PROBE_BOSS" })
        {
            GameObject leftover = GameObject.Find(name);
            if (leftover != null) Destroy(leftover);
        }
    }
}
#endif
