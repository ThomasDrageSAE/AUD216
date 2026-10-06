using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/*
 * CombatFeedback.cs
 *
 * Purpose: Tells the player their round landed - the reticle's hitmarker flashes and a 2D
 *          confirmation plays, white and small for a body hit, red and larger for a head
 *          hit. Nothing here decides damage, death or XP; it only reports what ZombieAI has
 *          already done.
 * Used by: Nothing calls it. It listens. One of these lives in the scene under Game
 *          Management (Tools/HUD/Add Combat Feedback puts it there).
 *
 * ZombieAI.OnZombieHit is STATIC, which is the whole reason this class can exist without
 * hunting down every zombie as it spawns - and it is also the reason OnDisable must
 * unsubscribe. A static event holds its subscribers across a scene reload, and this game
 * restarts by reloading its one scene: a leaked subscription would leave a destroyed
 * CombatFeedback handling hits for the new game, painting a marker that is no longer there.
 * Deliberately NOT DontDestroyOnLoad, for the same reason as every other manager here.
 *
 * The marker itself belongs to ReticleController - it is a child of the reticle root, so
 * Show(false) takes it down with the crosshair and this class never touches its active
 * state, only its colour and its scale. A scene with no marker wired, or no UIAudioManager
 * yet, is silent and harmless: every dereference below is checked where it happens rather
 * than cached across frames as a promise that the object is still alive.
 *
 * The zombie's own headshotSounds stay where they are - 3D, at the zombie, saying where the
 * hit happened. The slots this class plays are 2D, at the player, saying that it happened
 * at all. They are two different sounds and a student authors them apart.
 */
[DisallowMultipleComponent]
public class CombatFeedback : MonoBehaviour
{
    [Header("Hit Marker")]
    [Tooltip("Seconds the marker stays up. Its punch scale falls to 1 across this, and it " +
             "goes invisible at the end")]
    [SerializeField] private float hitFlashSeconds = 0.12f;

    [Tooltip("Scale an ordinary hit starts at, shrinking to 1 across the flash")]
    [SerializeField] private float hitPunchScale = 1.25f;

    [Tooltip("Scale a headshot starts at. Bigger than a body hit on purpose - the colour " +
             "alone is not a difference a colour-blind player can read")]
    [SerializeField] private float headshotPunchScale = 1.5f;

    [Tooltip("Marker colour for a hit anywhere but the head band")]
    [SerializeField] private Color hitColour = Color.white;

    [Tooltip("Marker colour for a hit in the head band")]
    [SerializeField] private Color headshotColour = new Color(1f, 0.25f, 0.2f, 1f);

    [Header("Sound")]
    [Tooltip("Floor on how often the confirmation may play, in seconds. 0 = every landed " +
             "hit, which is what the minigun's twenty rounds a second will ask for. Raise " +
             "it if that reads as a machine gun of UI beeps rather than as a gun")]
    [SerializeField, Range(0f, 0.5f)] private float minimumSoundInterval = 0f;

    // The marker this class last resolved a Graphic for. Held only to save a GetComponent
    // per bullet, never as a promise that the marker still exists - it is re-checked against
    // ReticleController's current one on every hit, so a reloaded scene resolves afresh.
    private RectTransform markerRect;
    private Graphic markerGraphic;

    private Coroutine flashRoutine;
    private float lastSoundTime = float.NegativeInfinity;

    private void OnEnable()
    {
        ZombieAI.OnZombieHit += HandleZombieHit;
    }

    private void OnDisable()
    {
        ZombieAI.OnZombieHit -= HandleZombieHit;

        // A flash cut off half way would leave the marker lit over a HUD nothing is driving
        // any more. Put it away; StopAllCoroutines is implied by the disable itself.
        flashRoutine = null;
        Paint(markerRect, markerGraphic, hitColour, 0f, 1f);
    }

    private void HandleZombieHit(ZombieHit hit)
    {
        Flash(hit.Headshot);
        PlayConfirmation(hit.Headshot);
    }

    private void Flash(bool headshot)
    {
        if (!TryResolveMarker(out RectTransform marker, out Graphic graphic)) return;

        // Restart rather than stack: the minigun lands twenty hits a second, and two
        // routines writing the same colour would fight over which one puts it away.
        if (flashRoutine != null) StopCoroutine(flashRoutine);
        flashRoutine = StartCoroutine(FlashRoutine(marker, graphic, headshot));
    }

    private IEnumerator FlashRoutine(RectTransform marker, Graphic graphic, bool headshot)
    {
        Color colour = headshot ? headshotColour : hitColour;
        float punch = headshot ? headshotPunchScale : hitPunchScale;

        float elapsed = 0f;
        while (elapsed < hitFlashSeconds)
        {
            if (marker == null || graphic == null) yield break;

            float t = hitFlashSeconds > 0f ? elapsed / hitFlashSeconds : 1f;
            Paint(marker, graphic, colour, 1f, Mathf.LerpUnclamped(punch, 1f, t));

            // Unscaled: a hitmarker that freezes lit because something stopped the clock is
            // worse than one that never showed.
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        Paint(marker, graphic, colour, 0f, 1f);
        flashRoutine = null;
    }

    private void PlayConfirmation(bool headshot)
    {
        if (minimumSoundInterval > 0f && Time.unscaledTime - lastSoundTime < minimumSoundInterval) return;

        // At the dereference, not in Awake: UIAudioManager is a scene-lifetime singleton
        // with no DontDestroyOnLoad, so Instance is null for the first frames and again
        // after a restart. Both play methods are no-ops on an empty slot, so an unauthored
        // clip costs nothing but silence.
        UIAudioManager uiAudio = UIAudioManager.Instance;
        if (uiAudio == null) return;

        lastSoundTime = Time.unscaledTime;

        if (headshot) uiAudio.PlayHeadshotPing();
        else uiAudio.PlayHitMarker();
    }

    /// <summary>
    /// The reticle's marker and the Graphic that paints it, asked for fresh on every hit.
    /// ReticleController.HitMarker is null until the setup tool wires it, and points at a
    /// different object after a scene reload - so the GetComponent result is cached only for
    /// as long as the answer is the same RectTransform.
    /// </summary>
    private bool TryResolveMarker(out RectTransform marker, out Graphic graphic)
    {
        marker = ReticleController.HitMarker;
        if (marker == null)
        {
            markerRect = null;
            markerGraphic = null;
            graphic = null;
            return false;
        }

        if (marker != markerRect || markerGraphic == null)
        {
            markerRect = marker;

            // The tool builds the marker as an Image on the marker object itself. The
            // children search is the fallback for one built as a group of ticks with the
            // graphic a level down; include-inactive, because Show(false) may have it down.
            Graphic own = marker.GetComponent<Graphic>();
            markerGraphic = own != null ? own : marker.GetComponentInChildren<Graphic>(true);
        }

        graphic = markerGraphic;
        return graphic != null;
    }

    /// <summary>
    /// Colour, alpha and uniform scale in one place, so no path can set one and forget
    /// another - the bug that leaves a marker at 1.5x for the rest of the night.
    /// </summary>
    private static void Paint(RectTransform marker, Graphic graphic, Color colour, float alpha, float scale)
    {
        if (marker == null || graphic == null) return;

        colour.a = alpha;
        graphic.color = colour;
        marker.localScale = new Vector3(scale, scale, 1f);
    }
}
