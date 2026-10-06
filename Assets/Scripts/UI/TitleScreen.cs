using UnityEngine;
using UnityStandardAssets.Characters.FirstPerson;

/*
 * TitleScreen.cs
 *
 * Purpose: The screen the game opens on - title, controls, Play and Quit.
 * Used by: nothing; it installs itself when the application launches.
 *
 * Code-driven IMGUI rather than a second scene, because the build contains exactly one
 * scene (index 0) and adding another would mean editing EditorBuildSettings and
 * re-testing every singleton's scene-lifetime assumption.
 *
 * It appears once per application launch, NOT after an in-game Restart: restarting is
 * "play again", and being sent back to a title screen every death would be hostile.
 * The static that tracks this is reset on SubsystemRegistration so the behaviour still
 * holds when Unity's domain reload is disabled.
 *
 * Ambience and music keep playing behind it - AudioSettings.dspTime is unaffected by
 * timeScale, so the adaptive music scheduler carries on regardless.
 *
 * Presentation notes (reworked 2026-09-01, the first pass was Unity's default skin):
 * - No GUI.skin.box panel. That drew a hard-edged grey slab over a night scene; the
 *   text now sits on a soft vertical scrim that fades out top and bottom, so the level
 *   silhouettes still read behind it.
 * - Buttons and every label use the scene's own controls via CanvasButtonStyle - the
 *   rounded 9-sliced sprite and SairaCondensed - so the title screen and the in-game
 *   ESC menu are visibly the same product.
 * - One amber accent (the firestick colour) carries the rule, the keys and the audio
 *   line. Everything else is neutral, so the accent means something.
 * - The column FLOWS (GUILayout) rather than being a list of hard-coded Y values, so
 *   changing the title's length or adding a line of controls re-centres the rest on its
 *   own; the first pass needed eight numbers retuned by hand. Rects are reserved with
 *   GUILayoutUtility.GetRect only where something is drawn twice (the title over its
 *   shadow) or filled directly (the rule).
 */

[DisallowMultipleComponent]
public class TitleScreen : MonoBehaviour
{
    /// <summary>
    /// The game's name. A const rather than a SerializeField: this component is created
    /// at runtime by Install(), so it has no scene instance for an inspector value to
    /// live on.
    /// </summary>
    private const string GameTitle = "EMBERWATCH";
    private const string Tagline = "Keep the lights burning. Last until morning.";

    // Layout runs in 1080p-reference coordinates; OnGUI scales the whole GUI to the
    // actual view height, so the screen keeps the same proportion on any monitor or
    // Game-view size (fixed pixels rendered tiny on high-res displays).
    private const float ReferenceHeight = 1080f;
    private const float ColumnWidth = 1000f;

    // Centred band, sized to frame the column rather than pinned to any one Y.
    private const float ScrimHeight = 720f;

    private const float TitleShadowOffsetX = 4f;
    private const float TitleShadowOffsetY = 5f;
    private const float RuleWidth = 460f;
    private const float RuleHeight = 2f;
    private const float ButtonWidth = 210f;   // 30% narrower (owner request)
    private const float ButtonHeight = 76f;
    private const float ButtonGap = 28f;

    // Rich-text hex for the accent, so the keys can be picked out inside a single label.
    private const string AccentHex = "#D9B86B";

    private static readonly Color Accent = new Color(0.851f, 0.722f, 0.420f);
    private static readonly Color WarmWhite = new Color(0.960f, 0.950f, 0.920f);
    private static readonly Color BodyGrey = new Color(0.720f, 0.710f, 0.680f);
    private static readonly Color Dim = new Color(0f, 0f, 0f, 0.55f);
    private static readonly Color TitleShadow = new Color(0f, 0f, 0f, 0.7f);

    private static bool hasShownThisSession;

    public static bool IsShowing { get; private set; }

    private GUIStyle titleStyle;
    private GUIStyle titleShadowStyle;
    private GUIStyle taglineStyle;
    private GUIStyle helpStyle;
    private GUIStyle buttonStyle;

    // Only the gradient needs a texture of its own. The flat fills use
    // Texture2D.whiteTexture tinted through GUI.color, which allocates nothing and so
    // never needs releasing.
    private Texture2D scrimTexture;

    // Reused for measuring, so reserving the title's rect does not allocate every frame.
    private readonly GUIContent titleContent = new GUIContent();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        hasShownThisSession = false;
        IsShowing = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (hasShownThisSession) return;
        hasShownThisSession = true;

        var host = new GameObject("Title Screen");
        host.AddComponent<TitleScreen>();
    }

    private void Awake()
    {
        IsShowing = true;

        Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // FirstPersonController's Update ignores timeScale and re-locks the cursor every
        // frame, which would leave these buttons unclickable and the camera spinning
        // behind the panel. Same shutdown EndGameScreen performs.
        SetPlayerControlEnabled(false);
    }

    private void OnGUI()
    {
        EnsureStyles();

        GUI.depth = -1000;   // above every gameplay overlay

        // IMGUI routes mouse input through this matrix too, so the buttons stay
        // clickable at every scale.
        float scale = Screen.height / ReferenceHeight;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        float viewWidth = Screen.width / scale;

        // A lighter dim than the old panel, because the scrim carries the text contrast -
        // the level stays faintly visible instead of going flat black.
        DrawFill(new Rect(0f, 0f, viewWidth, ReferenceHeight), Dim);
        GUI.DrawTexture(new Rect(0f, (ReferenceHeight - ScrimHeight) * 0.5f, viewWidth, ScrimHeight),
                        scrimTexture);

        GUILayout.BeginArea(new Rect((viewWidth - ColumnWidth) * 0.5f, 0f,
                                     ColumnWidth, ReferenceHeight));

        // The flexible spaces either side centre the whole column, so its height is
        // whatever the content adds up to rather than something to keep in sync.
        GUILayout.FlexibleSpace();

        DrawTitle();

        GUILayout.Space(20f);
        DrawRule();
        GUILayout.Space(18f);

        GUILayout.Label(Tagline, taglineStyle);

        GUILayout.Space(46f);
        GUILayout.Label(
            $"<color={AccentHex}>WASD</color> move     " +
            $"<color={AccentHex}>Shift</color> sprint     " +
            $"<color={AccentHex}>Space</color> jump     " +
            $"<color={AccentHex}>C</color> slide\n" +
            $"<color={AccentHex}>E</color> interact     " +
            $"<color={AccentHex}>1-5</color> inventory     " +
            $"<color={AccentHex}>1-5 twice</color> drop     " +
            $"<color={AccentHex}>Esc</color> pause",
            helpStyle);

        GUILayout.Space(56f);
        DrawButtons();

        GUILayout.FlexibleSpace();
        GUILayout.EndArea();
    }

    /// <summary>
    /// The title over an offset copy of itself, so the letters stay legible wherever the
    /// scene behind happens to be pale. The rect is reserved first because the shadow has
    /// to be drawn before the title, which a plain GUILayout.Label cannot express.
    /// </summary>
    private void DrawTitle()
    {
        titleContent.text = GameTitle;

        Rect rect = GUILayoutUtility.GetRect(titleContent, titleStyle);
        Rect shadow = new Rect(rect.x + TitleShadowOffsetX, rect.y + TitleShadowOffsetY,
                               rect.width, rect.height);

        GUI.Label(shadow, GameTitle, titleShadowStyle);
        GUI.Label(rect, GameTitle, titleStyle);
    }

    private void DrawRule()
    {
        Rect row = GUILayoutUtility.GetRect(ColumnWidth, RuleHeight);
        DrawFill(new Rect(row.center.x - RuleWidth * 0.5f, row.y, RuleWidth, RuleHeight), Accent);
    }

    private void DrawButtons()
    {
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();

        if (GUILayout.Button("PLAY", buttonStyle,
                             GUILayout.Width(ButtonWidth), GUILayout.Height(ButtonHeight)))
        {
            Play();
        }

        GUILayout.Space(ButtonGap);

        if (GUILayout.Button("QUIT", buttonStyle,
                             GUILayout.Width(ButtonWidth), GUILayout.Height(ButtonHeight)))
        {
            Quit();
        }

        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
    }

    /// <summary>
    /// Solid rect in a colour. Uses Unity's built-in white texture rather than one made
    /// here, so there is nothing to allocate and nothing to destroy.
    /// </summary>
    private static void DrawFill(Rect rect, Color colour)
    {
        Color previous = GUI.color;
        GUI.color = colour;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previous;
    }

    private void Play()
    {
        IsShowing = false;

        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        SetPlayerControlEnabled(true);

        if (UIAudioManager.Instance != null)
        {
            UIAudioManager.Instance.PlayMenuToggle(false);
        }

        Destroy(gameObject);
    }

    private void Quit()
    {
        IsShowing = false;
        Time.timeScale = 1f;

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private static void SetPlayerControlEnabled(bool enabled)
    {
        var controller = Object.FindFirstObjectByType<FirstPersonController>();
        if (controller != null)
        {
            controller.enabled = enabled;
        }
    }

    /// <summary>
    /// A centred label style in the canvas font. SairaCondensed carries its own weight,
    /// so only the fallback default font ever needs synthesized bold.
    /// </summary>
    private static GUIStyle MakeLabelStyle(Font font, int size, Color colour,
                                           bool boldWhenNoFont = false, bool richText = false)
    {
        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            font = font,
            fontSize = size,
            fontStyle = boldWhenNoFont && font == null ? FontStyle.Bold : FontStyle.Normal,
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true,
            richText = richText
        };

        style.normal.textColor = colour;
        return style;
    }

    private void EnsureStyles()
    {
        if (titleStyle == null)
        {
            // The scene's own button and font, so this screen and the ESC menu match.
            // Null-tolerant throughout: a scene without that button still draws, just in
            // the default skin.
            RectTransform anchor = CanvasButtonStyle.FindButton(CanvasButtonStyle.DefaultAnchorName);
            Font canvasFont = CanvasButtonStyle.FindFont(anchor);

            buttonStyle = CanvasButtonStyle.Build(anchor);
            buttonStyle.fontSize = 39;   // 30% larger (owner request)

            // Each style is built from the skin rather than copied from a sibling: a
            // GUIStyle copy can share its GUIStyleState, and one shared state would give
            // every label here the last colour assigned.
            titleStyle = MakeLabelStyle(canvasFont, 112, WarmWhite, boldWhenNoFont: true);
            titleShadowStyle = MakeLabelStyle(canvasFont, 112, TitleShadow, boldWhenNoFont: true);

            // The title is not a control and must not react to the mouse. The default
            // label skin carries a hover text colour, so with a custom normal colour a
            // mouse-over read as a highlight (owner request 2026-09-02: title only -
            // the other labels keep the skin's behaviour).
            titleStyle.hover.textColor = WarmWhite;
            titleShadowStyle.hover.textColor = TitleShadow;
            taglineStyle = MakeLabelStyle(canvasFont, 28, BodyGrey);
            helpStyle = MakeLabelStyle(canvasFont, 24, BodyGrey, richText: true);
        }

        if (scrimTexture == null)
        {
            // A vertical band that fades to nothing at both ends, so the text sits on
            // enough darkness to read without a visible panel edge anywhere.
            const int height = 64;
            scrimTexture = new Texture2D(1, height) { wrapMode = TextureWrapMode.Clamp };

            for (int y = 0; y < height; y++)
            {
                float t = y / (height - 1f);
                float alpha = Mathf.Sin(t * Mathf.PI) * 0.82f;   // 0 -> peak -> 0
                scrimTexture.SetPixel(0, y, new Color(0f, 0f, 0f, alpha));
            }

            scrimTexture.Apply();
        }
    }

    private void OnDestroy()
    {
        // Never leave the flag latched: PauseMenu and the end screens defer to it.
        IsShowing = false;

        if (scrimTexture != null) Destroy(scrimTexture);
    }
}
