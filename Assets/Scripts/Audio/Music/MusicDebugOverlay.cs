using System.Collections.Generic;
using UnityEngine;

/*
 * MusicDebugOverlay.cs
 *
 * Purpose: On-screen readout for the adaptive music system.
 * Used by: MusicManager (adds this itself when layered content is present)
 *
 * Available in BUILDS, like the Ctrl+G report and the Ctrl+J monitor - the music
 * overlay is as student-facing as they are (it was editor/dev-gated until 2026-09-02,
 * which hid its pause-menu button in the standalone build while the other two showed).
 *
 *   Ctrl+M  toggle the overlay          (alias F8)
 *
 * The Ctrl combo is primary because macOS claims F8-F12 for media and volume.
 *
 * The overlay is read-only: the intensity-floor, state-cycle and clear keys were
 * removed at the owner's request (2026-09-01). Per-stem solo/mute buttons remain -
 * they are mouse-driven and claim no keys. Intensity can still be driven from script
 * through MusicManager.SetIntensityOverride / ClearIntensityOverride, and the overlay
 * displays an override whenever one is set.
 *
 * The bar:beat readout is the thing to watch during a transition: with the generated
 * test stems, beat 1 of each bar is accented and pitched up, so a bar-aligned switch
 * lands exactly when the counter rolls over to beat 1.
 */

[DisallowMultipleComponent]
public class MusicDebugOverlay : MonoBehaviour
{
    private const float Width = 322f;   // 25% narrower than the original 430

    // Drawn at 200% via GUI.matrix (owner request - the raw panel read too small on a
    // high-resolution display), matching the Ctrl+G and Ctrl+J overlays. Layout
    // coordinates below are therefore in the halved "virtual" view.
    // 200% on the reference view, and in proportion to the screen height elsewhere - see
    // AudioOverlays.ResolutionScale. A fixed 2x was a quarter-width panel at 4K.
    private const float BaseScale = 2f;
    private static float Scale => AudioOverlays.ResolutionScale(BaseScale);

    private MusicManager manager;
    private GUIStyle labelStyle;

    // Bars use Texture2D.whiteTexture tinted through GUI.color - nothing to allocate,
    // nothing to destroy (same approach as TitleScreen).
    private static readonly Color BarBackgroundColour = new Color(0f, 0f, 0f, 0.5f);

    public void Initialize(MusicManager owner)
    {
        manager = owner;
    }

    private void Awake()
    {
        if (manager == null) manager = GetComponent<MusicManager>();

        // Tells the pause menu it has something to toggle. This component exists only
        // when a state has layered content, so the button must not be offered
        // unconditionally.
        AudioOverlays.MusicDebugAvailable = true;
    }

    private void OnDestroy()
    {
        AudioOverlays.MusicDebugAvailable = false;
    }

    /// <summary>
    /// The Ctrl combo is the primary binding: on macOS - this project's other platform -
    /// F8-F12 are claimed by the OS for media transport and volume, so a stock machine
    /// never delivers them to the game. F8 is kept as an alias for anyone who has enabled
    /// "Use F1-F12 as standard function keys".
    /// </summary>
    private static bool Chord(KeyCode key, KeyCode aliasKey)
    {
        bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        return (ctrl && Input.GetKeyDown(key)) || Input.GetKeyDown(aliasKey);
    }

    private void Update()
    {
        if (Chord(KeyCode.M, KeyCode.F8)) AudioOverlays.Toggle(AudioOverlay.MusicDebug);
    }

    private void OnGUI()
    {
        if (!AudioOverlays.IsShowing(AudioOverlay.MusicDebug) || manager == null) return;

        EnsureStyles();

        double dspNow = AudioSettings.dspTime;

        // Sized from what is actually drawn - audible players and their real stem counts.
        // A fixed allowance per registered player left a huge empty box, and clipped the
        // bottom bars of any state authored with more than four stems.
        int rows = 0;
        foreach (KeyValuePair<MusicManager.MusicState, LayeredStatePlayer> entry in manager.LayeredPlayers)
        {
            if (entry.Value.IsAudible) rows += 1 + entry.Value.StemSlotCount;
        }
        float height = 175f + rows * 36f;   // stem rows carry solo/mute buttons now

        // Saved and restored: the pause menu draws its own scaled GUI in the same frame,
        // and GUI.matrix is a global that would otherwise leak between the two.
        Matrix4x4 previousMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(Scale, Scale, 1f));
        float viewWidth = Screen.width / Scale;

        // Top-right (owner request); the panel used to sit top-left.
        GUILayout.BeginArea(new Rect(viewWidth - Width - 10f, 10f, Width, height), GUI.skin.box);
        AudioOverlays.DrawCloseButton(Width);

        GUILayout.Label("<b>MUSIC</b>  Ctrl+M hide · S/M per stem to solo or mute", labelStyle);

        // --- Horizontal state -------------------------------------------------
        string stateLine = $"State: <b>{manager.CurrentState}</b>";
        if (manager.HasPendingTransition)
        {
            double remaining = manager.PendingEntryDsp - dspNow;
            double beats = manager.Conductor.IsRunning && manager.Conductor.BeatDuration > 0.0
                ? remaining / manager.Conductor.BeatDuration
                : 0.0;
            stateLine += $"  ->  <b>{manager.PendingState}</b>  [{manager.PendingRuleName}]  " +
                         $"in {Mathf.Max(0f, (float)remaining):F2}s ({Mathf.Max(0f, (float)beats):F1} beats)";
        }
        GUILayout.Label(stateLine, labelStyle);

        MusicConductor conductor = manager.Conductor;
        GUILayout.Label(conductor.IsRunning
            ? $"Grid: bar {conductor.CurrentBar(dspNow) + 1}, beat {conductor.CurrentBeatInBar(dspNow)} " +
              $"of {conductor.BeatsPerBar}   ({conductor.Bpm:F0} BPM)"
            : "Grid: not running (legacy track playing)", labelStyle);

        // --- Vertical intensity ----------------------------------------------
        MusicIntensityDriver intensity = manager.IntensityDriver;
        if (intensity != null)
        {
            GUILayout.Label($"Intensity: <b>{intensity.Smoothed:F2}</b>  (raw {intensity.Raw:F2})", labelStyle);
            DrawBar(intensity.Smoothed, Color.cyan);
            GUILayout.Label(
                $"  combat {intensity.CombatContribution:F2} ({intensity.ActiveZombies} alive) · " +
                $"health {intensity.HealthContribution:F2} · night {intensity.NightContribution:F2}" +
                (intensity.HasOverride ? $" · <b>override {intensity.OverrideValue:F2}</b>" : ""),
                labelStyle);
        }

        // --- Stems ------------------------------------------------------------
        foreach (KeyValuePair<MusicManager.MusicState, LayeredStatePlayer> entry in manager.LayeredPlayers)
        {
            LayeredStatePlayer player = entry.Value;
            if (!player.IsAudible) continue;

            GUILayout.Space(4f);
            GUILayout.Label($"<b>{entry.Key}</b>  segment '{player.GetSegmentName()}'  " +
                            $"state fade {player.StateFade:F2}", labelStyle);

            for (int slot = 0; slot < player.StemSlotCount; slot++)
            {
                float gate = player.GetStemGate(slot);

                GUILayout.BeginHorizontal();

                // Solo/mute for auditioning individual layers while authoring the mix.
                bool solo = GUILayout.Toggle(player.GetDebugSolo(slot), "S", GUI.skin.button,
                    GUILayout.Width(26f));
                if (solo != player.GetDebugSolo(slot)) player.SetDebugSolo(slot, solo);

                bool mute = GUILayout.Toggle(player.GetDebugMute(slot), "M", GUI.skin.button,
                    GUILayout.Width(26f));
                if (mute != player.GetDebugMute(slot)) player.SetDebugMute(slot, mute);

                GUILayout.Label($"{player.GetStemName(slot)} (>= {player.GetStemMinIntensity(slot):F2})  {gate:F2}",
                    labelStyle);

                GUILayout.EndHorizontal();

                DrawBar(gate, gate > 0.001f ? Color.green : Color.grey);
            }
        }

        GUILayout.EndArea();

        GUI.matrix = previousMatrix;
    }

    private void DrawBar(float value01, Color fill)
    {
        Rect rect = GUILayoutUtility.GetRect(Width - 30f, 8f);

        Color previous = GUI.color;
        GUI.color = BarBackgroundColour;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);

        Rect filled = new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(value01), rect.height);
        GUI.color = fill;
        GUI.DrawTexture(filled, Texture2D.whiteTexture);
        GUI.color = previous;
    }

    private void EnsureStyles()
    {
        if (labelStyle == null)
        {
            labelStyle = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 12, wordWrap = true };
        }

    }
}
