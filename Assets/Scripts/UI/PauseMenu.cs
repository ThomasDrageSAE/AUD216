using System.Collections.Generic;
using UnityEngine;

/*
 * PauseMenu.cs
 * 
 * Purpose: Controls game pause functionality and pause menu display
 * Used by: Game state management, UI system
 * 
 * Key Features:
 * - Toggle pause state with Escape key
 * - Cursor management
 * - Time scale control
 * - Menu visibility handling
 * 
 * Performance Considerations:
 * - Minimal update overhead
 * - Efficient state management
 * - Smart audio handling
 * 
 * Dependencies:
 * - UIAudioManager for sound effects
 * - Requires pause menu UI setup
 * - Time system integration
 */
/// <summary>One acknowledged contribution, listed in the settings menu's popup.</summary>
[System.Serializable]
public class ContributionEntry
{
    public string contributor;
    [TextArea] public string contribution;
}

public class PauseMenu : MonoBehaviour
{
    public GameObject pauseMenuUI;
    private static bool isPaused = false;

    [Header("Contributions")]
    [Tooltip("Student acknowledgements shown by the CONTRIBUTIONS button in the " +
             "settings menu. Add a row per contribution as they land.")]
    [SerializeField] private ContributionEntry[] contributions =
    {
        new ContributionEntry
        {
            contributor = "Liam Chapman",
            contribution = "Magic mushroom system, the well & weapon reloads"
        }
    };

    // The authored canvas buttons the overlay toggles align themselves to. Names, not
    // serialized references, so this needs no scene edit; a rename is reported at runtime.
    // The button these are stacked above and styled after. The name lives on
    // CanvasButtonStyle, which is the other half of the same contract - two copies of it
    // would mean renaming the canvas button broke the styling and the positioning
    // separately.
    private const string SecondAnchorButtonName = "Button - SKIP TO DAY 3";

    private const string ContributionsLabel = "CONTRIBUTIONS";
    private const float PanelPadding = 24f;
    private const float TitleGap = 16f;
    private const float EntryGap = 10f;

    /// <summary>Floor for the shrink-to-fit loop; below this a label is unreadable anyway.</summary>
    private const int MinFontSize = 10;

    /// <summary>Top-to-bottom order. Music debug is last so it can be dropped when absent.</summary>
    private static readonly (string Label, AudioOverlay Overlay)[] OverlayButtons =
    {
        ("PROGRESS REPORT", AudioOverlay.ProgressReport),
        ("EVENT MONITOR", AudioOverlay.EventMonitor),
        ("MUSIC DEBUG", AudioOverlay.MusicDebug)
    };

    /// <summary>The canvas heading's yellow, reused to show which overlay is open.</summary>
    private static readonly Color ActiveButtonColour = new Color(0.914f, 0.871f, 0.227f);

    private RectTransform anchorButton;
    private RectTransform secondAnchorButton;
    private Canvas anchorCanvas;
    private bool anchorSearched;

    private readonly Vector3[] worldCorners = new Vector3[4];
    private GUIStyle menuButtonStyle;

    // The canvas buttons' resting tint, so an inactive toggle shades like the authored
    // control. The rest of the styling now comes from CanvasButtonStyle, shared with
    // TitleScreen.
    private Color menuButtonTint = Color.white;

    // Reused for text measurement; a fresh GUIContent per frame would be pure garbage.
    private readonly GUIContent labelContent = new GUIContent();

    // The acknowledgements popup. While it is up the settings canvas hides
    // underneath: uGUI would otherwise still catch clicks through the IMGUI panel,
    // and a sub-page reads cleaner than a pile-up anyway.
    private bool showingContributions;
    private GUIStyle contributionTitleStyle;
    private GUIStyle contributionEntryStyle;

    // Check the pause state of the game
    public static bool IsGamePaused()
    {
        return isPaused;
    }

    private void Awake()
    {
        // isPaused is static, so it survives a scene reload. Restarting from the pause
        // menu would otherwise leave the fresh scene believing it is already paused.
        isPaused = false;
        Time.timeScale = 1f;
    }

    void Update()
    {
        // The title and end screens own timeScale and the cursor while they are up;
        // Escape here would otherwise resume the world underneath an unclickable
        // overlay.
        if (TitleScreen.IsShowing || EndGameScreen.IsShowing) return;

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            // Escape peels one layer at a time: popup first, then the pause itself.
            if (isPaused && showingContributions)
            {
                CloseContributions();
            }
            else if (isPaused)
            {
                ResumeGame();
            }
            else
            {
                PauseGame();
            }
        }
    }

    private void OnGUI()
    {
        if (!isPaused || TitleScreen.IsShowing || EndGameScreen.IsShowing) return;

        GUI.depth = -100;

        // No Resume button: Escape toggles the pause menu and that is enough (owner
        // request, 2026-09-01). ResumeGame stays public - Escape calls it, and the
        // authored canvas may wire buttons to it by name.
        if (showingContributions)
        {
            DrawContributionsPopup();
            return;
        }

        DrawOverlayToggles();
    }

    /// <summary>
    /// Buttons for the audio overlays, so the Ctrl shortcuts are not the only way to open
    /// them. Both routes go through AudioOverlays, which holds the single authority over
    /// which overlay is up - so the buttons and the keys can never disagree, and picking
    /// one overlay closes whichever was already showing.
    ///
    /// They are stacked directly above the authored SKIP TO DAY 2 button and styled to
    /// match it. Position, size and spacing are measured from that button's RectTransform
    /// at runtime rather than hard-coded, so they stay aligned at any resolution and
    /// follow the canvas if its layout is ever re-authored.
    /// </summary>
    private void DrawOverlayToggles()
    {
        if (!TryGetAnchorLayout(out Rect anchor, out float gap)) return;

        int count = AudioOverlays.MusicDebugAvailable ? OverlayButtons.Length : OverlayButtons.Length - 1;
        EnsureMenuButtonStyle();

        // One size for the whole set, stepped down until the widest label fits: sizing
        // each button independently left PROGRESS REPORT visibly smaller than its
        // neighbours, which reads as a mistake rather than as a fit.
        menuButtonStyle.fontSize = Mathf.RoundToInt(anchor.height * 0.52f);
        float available = anchor.width - 14f;

        for (int i = 0; i <= count; i++)
        {
            labelContent.text = i < count ? OverlayButtons[i].Label : ContributionsLabel;
            while (menuButtonStyle.fontSize > MinFontSize &&
                   menuButtonStyle.CalcSize(labelContent).x > available)
            {
                menuButtonStyle.fontSize--;
            }
        }

        // CONTRIBUTIONS sits at the very top of the stack - acknowledgements, not a
        // gameplay control - and opens the popup instead of toggling an overlay.
        float contributionsY = anchor.y - (count + 1) * (anchor.height + gap);
        labelContent.text = ContributionsLabel;
        Color restingBackground = GUI.backgroundColor;
        GUI.backgroundColor = menuButtonTint;
        if (GUI.Button(new Rect(anchor.x, contributionsY, anchor.width, anchor.height),
                       labelContent, menuButtonStyle))
        {
            OpenContributions();
        }
        GUI.backgroundColor = restingBackground;

        for (int i = 0; i < count; i++)
        {
            // i = 0 is the topmost button; the last one sits one step above the anchor.
            float y = anchor.y - (count - i) * (anchor.height + gap);
            Rect rect = new Rect(anchor.x, y, anchor.width, anchor.height);

            labelContent.text = OverlayButtons[i].Label;
            bool showing = AudioOverlays.IsShowing(OverlayButtons[i].Overlay);

            // The sprite is white, so tinting it gives both states without a second
            // texture: the anchor button's own shade at rest, and the canvas's SETTINGS
            // yellow for whichever overlay is open.
            Color previousBackground = GUI.backgroundColor;
            GUI.backgroundColor = showing ? ActiveButtonColour : menuButtonTint;

            if (GUI.Button(rect, labelContent, menuButtonStyle))
            {
                AudioOverlays.Toggle(OverlayButtons[i].Overlay);
            }

            GUI.backgroundColor = previousBackground;
        }
    }

    private void OpenContributions()
    {
        showingContributions = true;
        if (pauseMenuUI != null) pauseMenuUI.SetActive(false);
    }

    private void CloseContributions()
    {
        showingContributions = false;
        if (isPaused && pauseMenuUI != null) pauseMenuUI.SetActive(true);
    }

    /// <summary>
    /// The acknowledgements panel: dimmed backdrop, yellow heading, one wrapped line
    /// per contribution, CLOSE styled like the other menu buttons. Entries are
    /// authored on this component's Contributions list in the inspector.
    /// </summary>
    private void DrawContributionsPopup()
    {
        FindAnchorButtons();
        EnsureMenuButtonStyle();
        EnsureContributionStyles();

        Color previousColour = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.65f);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = previousColour;

        // Twice the original cap (owner request): room for a line to spread out.
        float width = Mathf.Min(Screen.width * 0.9f, 1440f);
        float rowWidth = width - 2f * PanelPadding;

        contributionTitleStyle.fontSize = Mathf.Max(MinFontSize, Mathf.RoundToInt(Screen.height * 0.045f));
        contributionEntryStyle.fontSize = Mathf.Max(MinFontSize, Mathf.RoundToInt(Screen.height * 0.028f));

        labelContent.text = ContributionsLabel;
        float titleHeight = contributionTitleStyle.CalcHeight(labelContent, rowWidth);
        float closeHeight = Mathf.Max(34f, Screen.height * 0.05f);

        // Measured first so the panel wraps its content at any resolution.
        float entriesHeight = 0f;
        foreach (ContributionEntry entry in contributions)
        {
            if (entry == null || string.IsNullOrEmpty(entry.contributor)) continue;

            labelContent.text = FormatContribution(entry);
            entriesHeight += contributionEntryStyle.CalcHeight(labelContent, rowWidth) + EntryGap;
        }

        float height = PanelPadding + titleHeight + TitleGap + entriesHeight +
                       EntryGap + closeHeight + PanelPadding;
        Rect panel = new Rect((Screen.width - width) / 2f, (Screen.height - height) / 2f,
                              width, height);

        GUI.color = new Color(0.08f, 0.08f, 0.08f, 0.95f);
        GUI.DrawTexture(panel, Texture2D.whiteTexture);
        GUI.color = previousColour;

        float y = panel.y + PanelPadding;
        labelContent.text = ContributionsLabel;
        GUI.Label(new Rect(panel.x + PanelPadding, y, rowWidth, titleHeight),
                  labelContent, contributionTitleStyle);
        y += titleHeight + TitleGap;

        foreach (ContributionEntry entry in contributions)
        {
            if (entry == null || string.IsNullOrEmpty(entry.contributor)) continue;

            labelContent.text = FormatContribution(entry);
            float rowHeight = contributionEntryStyle.CalcHeight(labelContent, rowWidth);
            GUI.Label(new Rect(panel.x + PanelPadding, y, rowWidth, rowHeight),
                      labelContent, contributionEntryStyle);
            y += rowHeight + EntryGap;
        }

        float closeWidth = Mathf.Min(rowWidth, 220f);
        menuButtonStyle.fontSize = Mathf.RoundToInt(closeHeight * 0.52f);
        Color restingBackground = GUI.backgroundColor;
        GUI.backgroundColor = menuButtonTint;
        labelContent.text = "CLOSE";
        if (GUI.Button(new Rect(panel.x + (width - closeWidth) / 2f,
                                panel.yMax - PanelPadding - closeHeight,
                                closeWidth, closeHeight), labelContent, menuButtonStyle))
        {
            CloseContributions();
        }
        GUI.backgroundColor = restingBackground;
    }

    private static string FormatContribution(ContributionEntry entry)
    {
        return string.IsNullOrEmpty(entry.contribution)
            ? $"<b>{entry.contributor}</b>"
            : $"<b>{entry.contributor}</b>\n{entry.contribution}";
    }

    /// <summary>Popup text in the canvas's own font: yellow heading, wrapped white rows.</summary>
    private void EnsureContributionStyles()
    {
        if (contributionTitleStyle != null) return;

        Font font = CanvasButtonStyle.FindFont(anchorButton);

        contributionTitleStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold
        };
        if (font != null) contributionTitleStyle.font = font;
        contributionTitleStyle.normal.textColor = ActiveButtonColour;

        contributionEntryStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.UpperLeft,
            wordWrap = true,
            richText = true
        };
        if (font != null) contributionEntryStyle.font = font;
        contributionEntryStyle.normal.textColor = Color.white;
    }

    /// <summary>
    /// Screen rect of the SKIP TO DAY 2 button in GUI coordinates (y down), plus the
    /// vertical gap the canvas uses between it and SKIP TO DAY 3. False when the authored
    /// buttons cannot be found, in which case no overlay buttons are drawn rather than
    /// having them land somewhere arbitrary - the Ctrl shortcuts still work.
    /// </summary>
    private bool TryGetAnchorLayout(out Rect anchor, out float gap)
    {
        anchor = default;
        gap = 0f;

        FindAnchorButtons();
        if (!TryGetScreenRect(anchorButton, out Rect anchorScreen)) return false;

        // Screen space has y up, IMGUI has y down.
        anchor = new Rect(anchorScreen.x, Screen.height - anchorScreen.yMax,
                          anchorScreen.width, anchorScreen.height);

        gap = TryGetScreenRect(secondAnchorButton, out Rect secondScreen)
            ? Mathf.Max(0f, anchorScreen.yMin - secondScreen.yMax)
            : anchor.height * 0.25f;

        return true;
    }

    private void FindAnchorButtons()
    {
        if (anchorSearched) return;
        anchorSearched = true;

        // Both names in one pass. The skip buttons may live on a sibling canvas rather
        // than under pauseMenuUI, and the whole-scene sweep behind this is expensive
        // enough to be worth asking for them together.
        var wanted = new Dictionary<string, RectTransform>
        {
            { CanvasButtonStyle.DefaultAnchorName, null },
            { SecondAnchorButtonName, null }
        };

        CanvasButtonStyle.FindButtons(pauseMenuUI, wanted);

        anchorButton = wanted[CanvasButtonStyle.DefaultAnchorName];
        secondAnchorButton = wanted[SecondAnchorButtonName];

        if (anchorButton != null)
        {
            anchorCanvas = anchorButton.GetComponentInParent<Canvas>();
            menuButtonTint = CanvasButtonStyle.TintOf(anchorButton);
        }

        if (anchorButton == null)
        {
            Debug.LogWarning($"[PauseMenu] No '{CanvasButtonStyle.DefaultAnchorName}' in the scene, " +
                             "so the audio overlay buttons cannot be positioned and are hidden. " +
                             "The Ctrl+G / Ctrl+J / Ctrl+M shortcuts still work.", this);
        }
    }

    private bool TryGetScreenRect(RectTransform target, out Rect rect)
    {
        rect = default;
        if (target == null) return false;

        target.GetWorldCorners(worldCorners);   // 0 = bottom-left, 2 = top-right

        // An overlay canvas already reports its corners in screen pixels; any other
        // render mode has to go through its camera.
        Camera camera = anchorCanvas != null && anchorCanvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? anchorCanvas.worldCamera
            : null;

        Vector2 bottomLeft = RectTransformUtility.WorldToScreenPoint(camera, worldCorners[0]);
        Vector2 topRight = RectTransformUtility.WorldToScreenPoint(camera, worldCorners[2]);

        rect = Rect.MinMaxRect(Mathf.Min(bottomLeft.x, topRight.x), Mathf.Min(bottomLeft.y, topRight.y),
                               Mathf.Max(bottomLeft.x, topRight.x), Mathf.Max(bottomLeft.y, topRight.y));

        return rect.width > 1f && rect.height > 1f;
    }

    /// <summary>
    /// The canvas buttons' own style - rounded 9-sliced sprite, SairaCondensed, black
    /// label - built once from the anchor. Size is applied per button by the caller,
    /// which shrinks it to fit.
    /// </summary>
    private void EnsureMenuButtonStyle()
    {
        if (menuButtonStyle != null) return;

        menuButtonStyle = CanvasButtonStyle.Build(anchorButton);
    }

    public void ResumeGame()
    {
        showingContributions = false;
        pauseMenuUI.SetActive(false);
        Time.timeScale = 1f; // Resume game time
        isPaused = false;
        Cursor.lockState = CursorLockMode.Locked; // Lock the cursor to the center of the screen
        Cursor.visible = false; // Make the cursor invisible
        
        // Play menu toggle off sound
        if (UIAudioManager.Instance != null)
        {
            UIAudioManager.Instance.PlayMenuToggle(false);
        }
    }

    public void PauseGame()
    {
        pauseMenuUI.SetActive(true);
        Time.timeScale = 0f; // Pause game time
        isPaused = true;
        Cursor.lockState = CursorLockMode.None; // Free the cursor
        Cursor.visible = true; // Make the cursor visible
        
        // Play menu toggle on sound
        if (UIAudioManager.Instance != null)
        {
            UIAudioManager.Instance.PlayMenuToggle(true);
        }
    }
}
