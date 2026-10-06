using UnityEngine;
using UnityEngine.SceneManagement;
using UnityStandardAssets.Characters.FirstPerson;

/*
 * EndGameScreen.cs
 *
 * Purpose: The win and lose screens, with Restart and Quit.
 * Used by: PlayerStats death sequence (defeat)
 *
 * There is no victory ending. ShowVictory lived here until 2026-09-03, called only by
 * ChestPickup - collecting the chest was the win - and it went when the owner deleted the
 * skull/chest collectathon. Restoring one is three lines against Show() below, the way
 * ShowDeath is written; nothing else about this screen assumes a defeat.
 *
 * Code-driven IMGUI rather than authored scene UI: it needs no wiring, cannot be
 * broken by a missing reference, and works in a build. Self-hosting - the static
 * Show* methods create the object on demand.
 *
 * Restart reloads the active scene. That is only safe because the gameplay singletons
 * no longer use DontDestroyOnLoad (single-scene game): every manager is rebuilt with
 * the scene, so a restarted run has fresh objectives, day count, tracker flags and
 * references. If you ever reintroduce DontDestroyOnLoad on those, this button breaks.
 */

public class EndGameScreen : MonoBehaviour
{
    // Layout runs in 1080p-reference coordinates; OnGUI scales the whole GUI to the
    // actual view height, matching TitleScreen so the two bookends stay consistent.
    private const float ReferenceHeight = 1080f;
    private const float PanelWidth = 540f;
    private const float PanelHeight = 280f;

    private string title;
    private string subtitle;
    private Color accent;

    private GUIStyle titleStyle;
    private GUIStyle subtitleStyle;

    // Drawn with Texture2D.whiteTexture tinted through GUI.color - nothing to allocate,
    // nothing to destroy (same approach as TitleScreen).
    private static readonly Color Dim = new Color(0f, 0f, 0f, 0.75f);

    public static bool IsShowing { get; private set; }

    public static void ShowDeath()
    {
        Show("YOU DIED", "The night got the better of you.", new Color(0.9f, 0.35f, 0.35f));
    }

    private static void Show(string title, string subtitle, Color accent)
    {
        if (IsShowing) return;

        var host = new GameObject("End Game Screen");
        EndGameScreen screen = host.AddComponent<EndGameScreen>();
        screen.title = title;
        screen.subtitle = subtitle;
        screen.accent = accent;

        IsShowing = true;

        // The run is over: stop the world and give the mouse back.
        Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // The controller's Update ignores timeScale and re-locks the cursor every
        // frame (and keeps rotating the camera), which made the victory screen
        // unclickable. The death path already disables it via DisablePlayerControls;
        // doing it here covers victory too, and doing it twice is harmless.
        FirstPersonController controller = Object.FindFirstObjectByType<FirstPersonController>();
        if (controller != null)
        {
            controller.enabled = false;
        }
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

        Color previousColour = GUI.color;
        GUI.color = Dim;
        GUI.DrawTexture(new Rect(0f, 0f, viewWidth, ReferenceHeight), Texture2D.whiteTexture);
        GUI.color = previousColour;

        Rect panel = new Rect((viewWidth - PanelWidth) * 0.5f,
                              (ReferenceHeight - PanelHeight) * 0.5f,
                              PanelWidth, PanelHeight);

        GUILayout.BeginArea(panel, GUI.skin.box);
        GUILayout.Space(18f);

        Color previous = GUI.color;
        GUI.color = accent;
        GUILayout.Label(title, titleStyle);
        GUI.color = previous;

        GUILayout.Label(subtitle, subtitleStyle);
        GUILayout.FlexibleSpace();

        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();

        if (GUILayout.Button("Restart", GUILayout.Width(180f), GUILayout.Height(48f)))
        {
            Restart();
        }

        GUILayout.Space(15f);

        if (GUILayout.Button("Quit", GUILayout.Width(180f), GUILayout.Height(48f)))
        {
            Quit();
        }

        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUILayout.Space(20f);
        GUILayout.EndArea();
    }

    private void Restart()
    {
        // Restore both before loading: timeScale is global, and a new scene inherits it.
        Time.timeScale = 1f;
        IsShowing = false;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        Destroy(gameObject);
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private void Quit()
    {
        Time.timeScale = 1f;
        IsShowing = false;

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void EnsureStyles()
    {
        if (titleStyle == null)
        {
            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 44,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };

            subtitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true
            };
        }

    }

    private void OnDestroy()
    {
        // If anything tears the host down outside Restart/Quit, a latched IsShowing
        // would suppress every future end screen (Show early-outs on it).
        IsShowing = false;

    }
}
