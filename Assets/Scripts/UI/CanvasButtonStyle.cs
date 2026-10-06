using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/*
 * CanvasButtonStyle.cs
 *
 * Purpose: Builds an IMGUI GUIStyle that matches the scene's authored canvas buttons -
 *          the rounded 9-sliced sprite, its resting tint, and the TTF behind its TMP
 *          font - so code-drawn buttons read as the same control the player already sees.
 * Used by: PauseMenu (the audio overlay toggles) and TitleScreen (Play / Quit).
 *
 * Extracted from PauseMenu when TitleScreen became the second consumer, the same
 * consolidation AudioUtil got. Only the styling moved; each caller still finds and
 * positions its own buttons, because those rules differ.
 *
 * The scene draws its buttons with Unity's built-in UISprite, 9-sliced. IMGUI can render
 * exactly that by borrowing the sprite's texture and border widths, so this is a faithful
 * copy rather than an approximation of the rounded corners.
 *
 * WARNING: callers bind to their button by literal name. Renaming it in the scene compiles
 * clean and silently drops them back to the flat fallback - not an error, because a scene
 * without that button is still playable.
 */
public static class CanvasButtonStyle
{
    /// <summary>The button both callers match: the authored day-skip control.</summary>
    public const string DefaultAnchorName = "Button - SKIP TO DAY 2";

    /// <summary>
    /// The named button anywhere in the loaded scene, inactive included, or null.
    /// </summary>
    /// <param name="preferredRoot">
    /// Searched first when given - a hidden pause canvas whose children are the likely
    /// match. Falls back to a whole-scene sweep.
    /// </param>
    public static RectTransform FindButton(string name, GameObject preferredRoot = null)
    {
        if (string.IsNullOrEmpty(name)) return null;

        var wanted = new Dictionary<string, RectTransform> { { name, null } };
        FindButtons(preferredRoot, wanted);
        return wanted[name];
    }

    /// <summary>
    /// Fills <paramref name="wanted"/> - keyed by button name, values seeded null - in a
    /// single pass.
    /// </summary>
    /// <remarks>
    /// The sweep is the expensive part: Resources.FindObjectsOfTypeAll walks every loaded
    /// RectTransform, assets included. A caller after two buttons should therefore ask once
    /// rather than call FindButton twice, and it is skipped entirely when the preferred root
    /// already answered for everything.
    /// </remarks>
    public static void FindButtons(GameObject preferredRoot, IDictionary<string, RectTransform> wanted)
    {
        if (wanted == null || wanted.Count == 0) return;

        // Inactive children included: the pause canvas is hidden until the game is
        // paused, and a caller may ask on the frame it is shown.
        if (preferredRoot != null)
        {
            foreach (RectTransform candidate in preferredRoot.GetComponentsInChildren<RectTransform>(true))
            {
                Claim(wanted, candidate);
            }
        }

        if (!AnyMissing(wanted)) return;

        foreach (RectTransform candidate in Resources.FindObjectsOfTypeAll<RectTransform>())
        {
            if (!candidate.gameObject.scene.IsValid()) continue;   // skip prefab assets
            Claim(wanted, candidate);
        }
    }

    /// <summary>First match wins, so a preferred root is never overwritten by the sweep.</summary>
    private static void Claim(IDictionary<string, RectTransform> wanted, RectTransform candidate)
    {
        if (!wanted.TryGetValue(candidate.name, out RectTransform found) || found != null) return;

        wanted[candidate.name] = candidate;
    }

    private static bool AnyMissing(IDictionary<string, RectTransform> wanted)
    {
        foreach (RectTransform found in wanted.Values)
        {
            if (found == null) return true;
        }

        return false;
    }

    /// <summary>
    /// The TTF behind a button's TMP label, or null. IMGUI cannot draw a TMP font asset,
    /// but the asset keeps a reference to the TTF it was generated from
    /// (SairaCondensed-SemiBold here) and that is a plain Font. TMP can be configured to
    /// strip this on build, so callers must tolerate null.
    /// </summary>
    public static Font FindFont(RectTransform anchorButton)
    {
        if (anchorButton == null) return null;

        TMP_Text label = anchorButton.GetComponentInChildren<TMP_Text>(true);
        return label != null && label.font != null ? label.font.sourceFontFile : null;
    }

    /// <summary>
    /// A button style matching the given anchor: its rounded sprite, its tint and its
    /// font, with black label text. Pass null for the flat white fallback.
    /// </summary>
    /// <remarks>
    /// fontSize is deliberately NOT set - PauseMenu sizes its labels from the anchor's
    /// height and shrinks them to fit, and TitleScreen picks a fixed size. Baking one
    /// here would silently override both.
    /// </remarks>
    public static GUIStyle Build(RectTransform anchorButton)
    {
        Font font = FindFont(anchorButton);
        Sprite sprite = CaptureBackground(anchorButton, out Color tint);

        Texture2D background;
        RectOffset border;

        if (sprite != null)
        {
            // Sprite.border is (left, bottom, right, top); RectOffset is (l, r, t, b).
            Vector4 slice = sprite.border;
            background = sprite.texture;
            border = new RectOffset((int)slice.x, (int)slice.z, (int)slice.w, (int)slice.y);
        }
        else
        {
            // A flat fill has nothing to 9-slice, so it draws square corners. Unity's own
            // white texture, so a static helper with no teardown hook allocates nothing it
            // would then have to keep for the life of the domain.
            background = Texture2D.whiteTexture;
            border = new RectOffset(0, 0, 0, 0);
        }

        GUIStyle style = new GUIStyle(GUI.skin.button)
        {
            alignment = TextAnchor.MiddleCenter,
            font = font,
            // SairaCondensed-SemiBold already carries the weight; synthesizing bold on
            // top of it would be heavier than the buttons it is matching. Only the
            // fallback default font needs the emphasis.
            fontStyle = font != null ? FontStyle.Normal : FontStyle.Bold,
            border = border,
            padding = new RectOffset(6, 6, 0, 0),
            // Backstop for a label still too wide at its minimum size.
            clipping = TextClipping.Clip
        };

        foreach (GUIStyleState state in new[]
                 {
                     style.normal, style.hover, style.active, style.focused,
                     style.onNormal, style.onHover, style.onActive, style.onFocused
                 })
        {
            state.background = background;
            state.textColor = Color.black;
        }

        return style;
    }

    /// <summary>
    /// The anchor's resting tint, for callers that draw their own chrome over it -
    /// PauseMenu shades its toggles with this so an inactive one matches the canvas.
    /// </summary>
    public static Color TintOf(RectTransform anchorButton)
    {
        CaptureBackground(anchorButton, out Color tint);
        return tint;
    }

    /// <summary>
    /// The anchor's background sprite, and its resting tint, so a code-drawn button gets
    /// the same rounded corners and shade instead of an approximation.
    /// </summary>
    private static Sprite CaptureBackground(RectTransform anchorButton, out Color tint)
    {
        tint = Color.white;
        if (anchorButton == null) return null;

        Image image = anchorButton.GetComponent<Image>();
        if (image == null || image.sprite == null) return null;

        tint = image.color;

        // A Button multiplies its normal colour over the graphic's own.
        Button button = anchorButton.GetComponent<Button>();
        if (button != null && button.transition == Selectable.Transition.ColorTint)
        {
            tint *= button.colors.normalColor;
        }

        Sprite sprite = image.sprite;

        // Only when the sprite owns its whole texture. An atlased sprite would draw the
        // entire sheet, so those fall back to the plain fill.
        if (sprite.texture != null &&
            Mathf.Approximately(sprite.rect.width, sprite.texture.width) &&
            Mathf.Approximately(sprite.rect.height, sprite.texture.height))
        {
            return sprite;
        }

        return null;
    }

}
