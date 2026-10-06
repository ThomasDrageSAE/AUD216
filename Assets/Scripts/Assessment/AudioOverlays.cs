using UnityEngine;
using UnityEngine.SceneManagement;

/*
 * AudioOverlays.cs
 *
 * Purpose: Single authority for which audio overlay is on screen.
 * Used by: AudioProgressReportUI (Ctrl+G), AudioEventMonitor (Ctrl+J),
 *          MusicDebugOverlay (Ctrl+M), and the pause menu's toggle buttons.
 *
 * The three overlays used to own separate visible flags, so any pair could be up at
 * once - and at their current sizes they overlap illegibly. One shared value means
 * showing any overlay implicitly hides the others, whichever route asked for it: the
 * keyboard shortcut or the pause-menu button. Both routes call Toggle here rather than
 * flipping a local bool, so the two can never disagree about what is showing.
 *
 * State lives here rather than on a MonoBehaviour because the three owners have
 * different lifetimes: two are DontDestroyOnLoad singletons installed at startup, and
 * the music overlay is added per scene by MusicManager and only when layered content
 * exists. Statics are reset on load, matching the project's other reset-on-load statics
 * (AudioEventLog, TitleScreen), so a restarted run opens with no overlay up.
 */

public enum AudioOverlay
{
    None,
    ProgressReport,
    EventMonitor,
    MusicDebug
}

public static class AudioOverlays
{
    /// <summary>The overlay currently on screen, or None.</summary>
    public static AudioOverlay Current { get; private set; }

    /// <summary>
    /// True while a music debug overlay exists to be toggled. It is dev-gated AND only
    /// created when a state has layered content, so the pause menu must not offer a
    /// button that could do nothing.
    /// </summary>
    public static bool MusicDebugAvailable { get; set; }

    public static bool IsShowing(AudioOverlay overlay) =>
        overlay != AudioOverlay.None && Current == overlay;

    /// <summary>
    /// Shows this overlay, or hides it if it is already the one showing. Any other
    /// overlay is closed by the same call - that is the point of a single value.
    ///
    /// Refused while the title or end screen is up. Those draw at GUI.depth -1000 and the
    /// overlays set no depth at all, so one opened underneath is both unreadable and
    /// unclickable - it looks like the shortcut is broken. CLOSING is always allowed:
    /// refusing that could strand an overlay that was already showing.
    /// </summary>
    public static void Toggle(AudioOverlay overlay)
    {
        bool opening = Current != overlay;
        if (opening && (TitleScreen.IsShowing || EndGameScreen.IsShowing)) return;

        Current = Current == overlay ? AudioOverlay.None : overlay;
    }

    public static void HideAll() => Current = AudioOverlay.None;

    /// <summary>
    /// Draws the shared close box in the top-right corner of an overlay and closes the
    /// overlay when it is clicked. Call it inside the panel's BeginArea and pass the
    /// panel's width: coordinates are the panel's own, so this lands in its corner
    /// whatever the panel's position or GUI.matrix scale.
    ///
    /// It lives here so the three overlays cannot drift apart visually, and so a player
    /// who opened one from the pause menu can dismiss it without going back there.
    /// </summary>
    /// <summary>
    /// The Game view height the overlays were tuned on (the owner's Free Aspect tab,
    /// 2656 x 1370, on 2026-09-06). An overlay drawn at <see cref="ResolutionScale"/> is the
    /// same fraction of the screen at every resolution as it was there - which is what the
    /// owner asked for when the 4K view made the progress report a third the size.
    /// </summary>
    public const float ReferenceHeight = 1370f;

    /// <summary>
    /// The GUI.matrix scale for an overlay authored at <paramref name="baseScale"/> on the
    /// reference view. IMGUI draws in screen pixels, so a fixed scale is a fixed number of
    /// pixels - 1120 of them is most of a 1370 px view and a quarter of a 3840 one; this
    /// makes it a fixed fraction instead. Floored so a tiny Game view cannot invert it.
    /// </summary>
    public static float ResolutionScale(float baseScale) =>
        baseScale * Mathf.Max(0.25f, Screen.height / ReferenceHeight);

    public static void DrawCloseButton(float panelWidth)
    {
        const float size = 20f;
        const float margin = 5f;

        if (GUI.Button(new Rect(panelWidth - size - margin, margin, size, size), "X"))
        {
            HideAll();
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnLoad()
    {
        Current = AudioOverlay.None;
        MusicDebugAvailable = false;

        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // The surviving DontDestroyOnLoad overlays would otherwise reappear over a
        // freshly restarted run.
        Current = AudioOverlay.None;
    }
}
