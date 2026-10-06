using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using TMPro;
using UnityStandardAssets.Characters.FirstPerson;

public class PlayerStats : MonoBehaviour, IDamageable
{
    [Header("Hunger Settings")]
    [SerializeField] private Slider hungerBar;
    [SerializeField] private float maxHunger = 100f;
    [SerializeField] private float hungerDecreaseRate = 1.667f; // Will take 60 seconds to go from 100 to 0
    [Tooltip("Hunger warning sound fires below this fraction of max hunger")]
    [Range(0f, 1f)] [SerializeField] private float hungerWarningFraction = 0.25f;
    [Tooltip("Seconds between repeat hunger warnings while hunger stays low")]
    [SerializeField] private float hungerWarningRepeatSeconds = 30f;
    
    [Header("Health Settings")]
    [SerializeField] private Slider healthBar;
    [SerializeField] private float maxHealth = 100f;

    [Header("Stamina Settings")]
    [SerializeField] private Slider staminaBar;
    [SerializeField] private float maxStamina = 100f;
    [SerializeField] private float staminaIncreaseRate = 10f;
    [SerializeField] private float staminaRequiredForSpecialMoves = 50f;
    [SerializeField] private float staminaCostDoubleJump = 30f;
    [SerializeField] private float staminaCostSlide = 40f;
    [SerializeField] private float sprintStaminaCostPerSecond = 15f;

    [Header("Damage Feedback")]
    [SerializeField] private ParticleSystem damageParticles;
    [SerializeField] private Animator damageAnimator;
    [SerializeField] private string damageAnimationTrigger = "damageAnimationTrigger";
    [SerializeField] private GameObject damageEffectRoot;
    [Tooltip("Swings the edge-of-screen smear toward the attacker. Empty = found on (or " +
             "added to) the Damage Effect Root at runtime, so the scene needs no edit")]
    [SerializeField] private DamageDirectionIndicator damageDirection;
    [Tooltip("Vignette effect for damage and low health indication")]
    [SerializeField] private Animator vignetteAnimator;
    [Tooltip("Health threshold for triggering hit effects (default 50)")]
    [SerializeField] private float midHealthThreshold = 50f;
    [Tooltip("Health threshold to activate constant vignette effect (default 25)")]
    [SerializeField] private float lowHealthThreshold = 25f;

    [Header("XP Settings")]
    [SerializeField] private Slider xpBar;
    [SerializeField] private float baseXpToLevelUp = 100f;
    [SerializeField] private float xpScalingFactor = 1.5f;

    // A NEW field rather than halving baseXpToLevelUp: that one is serialized in the
    // scene at 100, so a code-default change would silently do nothing - and only the
    // FIRST level should be cheaper (owner request 2026-09-02: level 2 took too long),
    // with the established curve untouched from level 2 onward.
    [Tooltip("Level 1 -> 2 costs baseXpToLevelUp x this. Later levels keep the normal curve")]
    [Range(0.1f, 1f)] [SerializeField] private float firstLevelXpFraction = 0.5f;

    // A FRACTION of the current level's requirement, not a flat number: the curve grows
    // 1.5x per level, so a flat objective reward would be a tenth of a level early on and
    // a rounding error by level 8. Expressed this way the promise "an objective is worth at
    // least a tenth of a level" holds at every level.
    [Tooltip("Share of the CURRENT level's XP requirement granted for completing one " +
             "objective. 0.1 = a tenth of a level, whatever the level costs.")]
    [Range(0.01f, 1f)] [SerializeField] private float objectiveXpFraction = 0.1f;
    [SerializeField] private TextMeshProUGUI levelText;

    [Header("Level Up Animation")]
    [SerializeField] private GameObject levelUpAnimatorPrefab;
    [SerializeField] private Canvas targetCanvas;
    [SerializeField] private string levelNumberTextPath = "LevelBox/Label_PlayerLevel";

    [Header("XP Gain Animation")]
    [SerializeField] private GameObject xpGainAnimatorPrefab;
    [SerializeField] private Transform xpGainParent;
    [SerializeField] private string xpGainTextPath = "Content/HUD_XPLog_Item/Content/Text";
    [SerializeField] private float xpGainAnimationDuration = 2f;

    // Where the next toast should pop, in world space, or null for the bottom of the screen.
    // Only AddXP(float, Vector3) ever sets it and only ShowXPGainAnimation reads it, both
    // within one synchronous call - LevelUp does not re-enter AddXP, so there is no second
    // toast in flight that could pick this up by mistake.
    private Vector3? xpToastWorldPoint;

    [Header("Death Settings")]
    [SerializeField] private float deathDelay = 2f;

    [Header("Audio")]
    [SerializeField] private PlayerAudioManager playerAudioManager;

    /// <summary>
    /// The player's stat-audio manager, for consumables that need a fallback sound.
    /// </summary>
    public PlayerAudioManager AudioManager => playerAudioManager;

    private float currentHunger;
    private float currentHealth;
    private float currentStamina;
    private bool isSprinting = false;

    // Fraction of max stamina that must be recovered before sprinting can restart.
    private const float SprintResumeFraction = 0.15f;

    // Gates the "recovered" chime so it only follows an actual depletion, rather than
    // firing every time stamina dips under the resume threshold and climbs back.
    private bool staminaDepletedNotified = false;

    private float lastHungerWarningTime = 0f;
    private bool hungerWarningArmed = true;
    private float currentXp;
    private int currentLevel = 1;
    private float xpToNextLevel;
    private bool isDead = false;
    private bool isLowHealthEffectActive = false;
    private static readonly int ActiveParameter = Animator.StringToHash("Active");
    private static readonly int HitParameter = Animator.StringToHash("Hit");

    private float lastHungerUpdate = 0f;
    private float hungerUpdateInterval = 0.1f; // Update hunger every 100ms instead of every frame
    private float lastStaminaUpdate = 0f;
    private float staminaUpdateInterval = 0.05f; // Update stamina every 50ms
    
    // Tracks the damage-flash hide so a later hit (or death) can cancel a pending one.
    private Coroutine hideDamageEffectRoutine;

    /// <summary>
    /// The player's stats. Consumables and enemies resolve the player through this rather
    /// than GameObject.FindGameObjectWithTag("Player"): PlayerStats lives on a child of
    /// the Player root, so a tag lookup returns the root (or, historically, whichever of
    /// several tagged objects Unity felt like) and GetComponent finds nothing.
    ///
    /// Deliberately NOT DontDestroyOnLoad - this is a single-scene game and the scene's
    /// own copy is always the right one.
    /// </summary>
    public static PlayerStats Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"[PlayerStats] A second PlayerStats exists on '{gameObject.name}'; " +
                             $"'{Instance.gameObject.name}' keeps ownership.", this);
        }
        else
        {
            Instance = this;
        }

        // Ensure vignette starts inactive
        if (vignetteAnimator != null)
        {
            vignetteAnimator.SetBool(ActiveParameter, false);
            isLowHealthEffectActive = false;
        }

        ResolveDamageDirection();
    }

    /// <summary>
    /// The edge-of-screen smear's direction driver: the wired one, else one already on
    /// the damage-effect root, else one added there now. Resolved here rather than wired
    /// in the scene so hits become directional without a scene edit.
    /// </summary>
    private void ResolveDamageDirection()
    {
        if (damageDirection != null || damageEffectRoot == null) return;

        damageDirection = damageEffectRoot.GetComponent<DamageDirectionIndicator>();
        if (damageDirection == null)
        {
            damageDirection = damageEffectRoot.AddComponent<DamageDirectionIndicator>();
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        InitializeStats();
        InitializeUI();
        InitializeXPSystem();
        UpdateLowHealthEffect();

        // Time.time counts from application start and survives scene reloads, so a
        // fresh PlayerStats must not tick from the fields' default of 0: the first
        // hunger tick after a restart would bill the entire previous session at once
        // (0.5 hunger/s x a few minutes = instant starvation, then a death loop).
        lastHungerUpdate = Time.time;
        lastStaminaUpdate = Time.time;
        lastHungerWarningTime = Time.time;
    }

    private void InitializeStats()
    {
        currentHunger = maxHunger;
        currentHealth = maxHealth;
        // Full, like health and hunger: starting at zero meant ~10 seconds of the
        // sprint cutting in and out while the bar first filled (owner request
        // 2026-09-03).
        currentStamina = maxStamina;
    }

    private void InitializeUI()
    {
        if (hungerBar != null)
        {
            hungerBar.maxValue = maxHunger;
            hungerBar.value = currentHunger;
        }
        
        if (healthBar != null)
        {
            healthBar.maxValue = maxHealth;
            healthBar.value = currentHealth;
        }

        if (staminaBar != null)
        {
            staminaBar.maxValue = maxStamina;
            staminaBar.value = currentStamina;
        }

        if (damageEffectRoot != null)
        {
            damageEffectRoot.SetActive(false);
        }
    }

    private void InitializeXPSystem()
    {
        currentXp = 0f;
        xpToNextLevel = baseXpToLevelUp * firstLevelXpFraction;   // level 2 comes quickly
        
        if (xpBar != null)
        {
            xpBar.maxValue = xpToNextLevel;
            xpBar.value = currentXp;
        }

        UpdateLevelText();
    }

    private void Update()
    {
        if (isDead) return;

        float currentTime = Time.time;

        // Update hunger and stamina at intervals instead of every frame. The handlers are
        // given the time that ACTUALLY elapsed, not the nominal interval: a tick fires on
        // the first frame at or after its due time, so at low frame rates the real gap is
        // longer and crediting a fixed interval silently ran these stats slow (about 75%
        // speed at 30fps for the 0.05s stamina tick), making survival pacing depend on
        // the machine.
        if (currentTime >= lastHungerUpdate + hungerUpdateInterval)
        {
            HandleHunger(currentTime - lastHungerUpdate);
            lastHungerUpdate = currentTime;
        }

        if (currentTime >= lastStaminaUpdate + staminaUpdateInterval)
        {
            HandleStamina(currentTime - lastStaminaUpdate);
            lastStaminaUpdate = currentTime;
        }

        // Check health/hunger death condition less frequently
        if (currentHealth <= 0 || currentHunger <= 0)
        {
            Die();
        }
    }

    private void HandleHunger(float elapsedSeconds)
    {
        if (currentHunger <= 0) return;

        currentHunger -= hungerDecreaseRate * elapsedSeconds;
        currentHunger = Mathf.Max(0, currentHunger);
        
        if (hungerBar != null)
        {
            hungerBar.value = currentHunger;
        }

        UpdateHungerWarning();
    }

    /// <summary>
    /// The hunger warning band. One reading, shared by the repeating one-shot warnings and
    /// the sustained hunger loop, so the two can never disagree about when the player is
    /// starving.
    /// </summary>
    private bool IsHungerLow => currentHunger <= maxHunger * hungerWarningFraction;

    /// <summary>
    /// Warns once when hunger first falls below the threshold, then repeats on an
    /// interval while it stays there, and re-arms once the player eats back above it.
    /// Without the re-arm a single meal would silence the warning for the rest of the run.
    /// </summary>
    private void UpdateHungerWarning()
    {
        if (playerAudioManager == null) return;

        UpdateHungerLoop();

        if (!IsHungerLow)
        {
            hungerWarningArmed = true;
            return;
        }

        if (hungerWarningArmed || Time.time >= lastHungerWarningTime + hungerWarningRepeatSeconds)
        {
            playerAudioManager.PlayHungerSound();
            lastHungerWarningTime = Time.time;
            hungerWarningArmed = false;
        }
    }

    /// <summary>
    /// Drives the sustained hunger loop, the one-shot warnings' companion (owner request
    /// 2026-09-03). Kept separate from UpdateHungerWarning so the eating path can end the
    /// loop the moment a meal lifts hunger out of the band without touching the one-shot's
    /// repeat timer or its re-arm.
    /// </summary>
    private void UpdateHungerLoop()
    {
        if (playerAudioManager == null) return;
        playerAudioManager.UpdateHungerLoopState(IsHungerLow);
    }

    private void HandleStamina(float elapsedSeconds)
    {
        float staminaChange;

        if (isSprinting)
        {
            staminaChange = -sprintStaminaCostPerSecond * elapsedSeconds;
        }
        else if (currentStamina < maxStamina)
        {
            staminaChange = staminaIncreaseRate * elapsedSeconds;
        }
        else
        {
            return; // No change needed
        }

        float previousStamina = currentStamina;
        currentStamina = Mathf.Clamp(currentStamina + staminaChange, 0, maxStamina);
        
        if (staminaBar != null)
        {
            staminaBar.value = currentStamina;
        }

        AfterStaminaChanged(previousStamina);
    }

    /// <summary>
    /// Fires the two stamina audio edges. Every path that moves currentStamina routes
    /// through here, so "hit empty" and "recovered enough to sprint again" are detected
    /// in one place rather than in each of the three spenders.
    /// </summary>
    private void AfterStaminaChanged(float previousStamina)
    {
        if (playerAudioManager == null) return;

        if (previousStamina > 0f && currentStamina <= 0f)
        {
            playerAudioManager.PlayStaminaDepletedSound();
            staminaDepletedNotified = true;
            return;
        }

        if (!staminaDepletedNotified) return;

        // Matches HasStaminaForSprinting's resume gate: this is the moment sprinting
        // becomes possible again, which is what the sound is telling the player.
        float resumeThreshold = maxStamina * SprintResumeFraction;
        if (previousStamina < resumeThreshold && currentStamina >= resumeThreshold)
        {
            playerAudioManager.PlayStaminaRegeneratingSound();
            staminaDepletedNotified = false;
        }
    }

    private void Die()
    {
        if (isDead) return;
        isDead = true;

        // The low-health heartbeat and the hunger loop are driven by the audio manager,
        // which lives on a different GameObject, so they survived death and kept sounding
        // at full volume through the whole death sequence - starving to death ends with
        // hunger at 0, which is exactly where the loop is loudest.
        if (hideDamageEffectRoutine != null)
        {
            StopCoroutine(hideDamageEffectRoutine);
            hideDamageEffectRoutine = null;
        }

        if (playerAudioManager != null)
        {
            playerAudioManager.StopLowHealthLoop();
            playerAudioManager.StopHungerLoop();
        }

        DisablePlayerControls();
        ShowDeathEffects();
        AnimationEventHandler.Instance?.ShowDeathAnnouncement();
        StartCoroutine(QuitGameSequence());
    }

    private void ShowDeathEffects()
    {
        if (damageEffectRoot != null)
        {
            damageEffectRoot.SetActive(true);
        }

        if (damageParticles != null)
        {
            damageParticles.Play();
        }

        if (damageAnimator != null)
        {
            damageAnimator.SetTrigger(damageAnimationTrigger);
        }
    }

    private void DisablePlayerControls()
    {
        // PlayerStats sits on its own GameObject, so the old GetComponents<MonoBehaviour>()
        // loop only ever found this component and the "script != this" filter dropped it.
        // Nothing was disabled: the player kept full movement, mouse look, sliding and
        // weapon fire for the entire death sequence.
        FirstPersonController controller = FindFirstObjectByType<FirstPersonController>();
        if (controller != null)
        {
            controller.enabled = false;
        }

        foreach (WeaponController weapon in FindObjectsByType<WeaponController>(FindObjectsSortMode.None))
        {
            weapon.enabled = false;
        }
    }

    /// <summary>
    /// Runs after the death effects have played. Dying used to quit the application
    /// outright, giving the player no way to try again.
    /// </summary>
    private IEnumerator QuitGameSequence()
    {
        // Realtime: a scaled wait never resumes if the player pauses (timeScale 0)
        // during the delay, and the screen this leads to freezes time anyway.
        yield return new WaitForSecondsRealtime(deathDelay);

        EndGameScreen.ShowDeath();
    }

    /// <summary>
    /// Damage with no known source (IDamageable). The edge-of-screen smear falls back to
    /// its neutral top edge, which is what every hit showed before it became directional.
    /// </summary>
    public void TakeDamage(float amount)
    {
        TakeDamage(amount, null);
    }

    /// <summary>
    /// Damage from a known attacker: the edge-of-screen smear swings round to the edge the
    /// attack came from and follows the attacker while the flash shows. The enemies call
    /// this one with their own transform.
    /// </summary>
    public void TakeDamage(float amount, Transform attacker)
    {
        if (isDead) return;

        // Before the effect shows, so the first visible frame already points the right way.
        if (damageDirection != null)
        {
            if (attacker != null) damageDirection.PointAt(attacker);
            else damageDirection.Clear();
        }

        currentHealth = Mathf.Max(0, currentHealth - amount);
        if (healthBar != null)
        {
            healthBar.value = currentHealth;
        }

        if (playerAudioManager != null)
        {
            playerAudioManager.PlayDamageSound();
        }

        UpdateLowHealthEffect();  // Update low health state first
        ShowDamageEffects();     // Then show hit effects

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    private void ShowDamageEffects()
    {
        if (damageEffectRoot != null)
        {
            damageEffectRoot.SetActive(true);

            // Track the handle: an untracked hide from a previous hit used to fire
            // mid-way through a later flash, and after the killing blow it switched the
            // death effect back off half a second in.
            if (hideDamageEffectRoutine != null)
            {
                StopCoroutine(hideDamageEffectRoutine);
            }
            hideDamageEffectRoutine = StartCoroutine(HideDamageEffect());
        }

        if (damageParticles != null)
        {
            damageParticles.Play();
        }

        if (damageAnimator != null)
        {
            damageAnimator.SetTrigger(damageAnimationTrigger);
        }

        // Only trigger hit effect when health is below midHealthThreshold
        if (vignetteAnimator != null && currentHealth <= midHealthThreshold)
        {
            vignetteAnimator.SetTrigger(HitParameter);
//            Debug.Log($"Triggering hit effect at {currentHealth:F1} health (below mid threshold of {midHealthThreshold})");
        }
    }

    private IEnumerator HideDamageEffect()
    {
        yield return new WaitForSeconds(0.5f);

        if (damageEffectRoot != null)
        {
            damageEffectRoot.SetActive(false);
        }

        hideDamageEffectRoutine = null;
    }

    /// <summary>
    /// XP awarded for something that happened at a place - a kill, so the toast can pop at
    /// the corpse instead of at the bottom of the screen. Everything else about the award is
    /// identical: the same grant, the same level-up path, the same bar.
    /// </summary>
    /// <param name="worldPoint">Where it happened. Behind the camera, or with no camera, the
    /// toast falls back to its usual place.</param>
    public void AddXP(float amount, Vector3 worldPoint)
    {
        if (isDead) return;

        // Set around ONE call and consumed inside it. A field rather than a parameter
        // because the grant, the level-up loop and the bar all live in AddXP(float), and a
        // kill has to go through exactly that path - a second copy of it here is how XP
        // ends up granted twice. Cleared again below for the paths where the toast never
        // gets as far as reading it, so no objective can inherit a corpse's position.
        xpToastWorldPoint = worldPoint;
        AddXP(amount);
        xpToastWorldPoint = null;
    }

    public void AddXP(float amount)
    {
        if (isDead) return;
        
        bool isFirstXP = currentXp <= 0;
        currentXp += amount;
        
        ShowXPGainAnimation(amount);
        
        while (currentXp >= xpToNextLevel)
        {
            float excess = currentXp - xpToNextLevel;
            LevelUp();
            currentXp = excess;
        }

        if (xpBar != null)
        {
            xpBar.value = currentXp;
        }

        if (isFirstXP)
        {
            UpdateLevelText();
        }
    }

    /// <summary>
    /// XP for finishing one objective, as a share of what the CURRENT level costs, so it is
    /// worth the same proportion of a level at level 1 and at level 10. Read the threshold
    /// before granting - AddXP may level the player up, which moves it.
    /// </summary>
    public void AwardObjectiveXP()
    {
        if (isDead) return;

        float award = xpToNextLevel * objectiveXpFraction;
        if (award <= 0f) return;

        AddXP(award);
    }

    private void ShowXPGainAnimation(float amount)
    {
        if (xpGainAnimatorPrefab == null) return;

        // Consumed, not read: whatever happens below, the next toast is a bottom one again.
        Vector3? popAt = xpToastWorldPoint;
        xpToastWorldPoint = null;

        Transform parent = xpGainParent != null ? xpGainParent : targetCanvas?.transform;
        if (parent == null)
        {
            targetCanvas = FindFirstObjectByType<Canvas>();
            if (targetCanvas == null) return;
            parent = targetCanvas.transform;
        }

        GameObject xpGainInstance = Instantiate(xpGainAnimatorPrefab, parent);
        
        // Set XP text
        Transform xpGainTextTransform = xpGainInstance.transform.Find(xpGainTextPath);
        if (xpGainTextTransform != null)
        {
            if (xpGainTextTransform.TryGetComponent<TextMeshProUGUI>(out var xpGainText))
            {
                // Whole numbers. The objective award is a tenth of the level threshold, which
                // is 50.625 at level 3 and 75.9375 at level 4 - printed raw, those wrapped onto
                // a second line that fell off the strip (the owner's report, 2026-09-06).
                // Nobody is owed the fraction: the bar keeps the float, the toast is a shout.
                xpGainText.textWrappingMode = TextWrappingModes.NoWrap;
                xpGainText.text = $"+{Mathf.RoundToInt(amount)} XP";

                // And the strip grows to the text. The prefab is a fixed 220 px, sized for
                // "+80 XP"; anything longer - a big award, a longer string later - would spill.
                // Every fixed-width rect between the text and the instance root is widened;
                // the stretched ones follow their parents.
                FitXpStripToText(xpGainInstance.transform, xpGainText);
            }
        }

        // Play animation
        if (xpGainInstance.TryGetComponent<Animator>(out var animator))
        {
            int layerIndex = animator.GetLayerIndex("Base Layer");
            if (layerIndex != -1 && animator.HasState(layerIndex, Animator.StringToHash("ANIM_HUD_Event_XPGain_In")))
            {
                animator.Play("ANIM_HUD_Event_XPGain_In", layerIndex);
            }
        }

        // A kill's toast pops at the corpse. Everything else - objectives, fish - keeps the
        // bottom of the screen it has always risen from, and so does a kill the camera
        // cannot draw: TryToastPosition says no and this simply falls through, leaving the
        // prefab's own anchored position untouched.
        RectTransform parentRect = parent as RectTransform;
        if (popAt.HasValue && parentRect != null &&
            xpGainInstance.transform is RectTransform toastRect &&
            TryToastPosition(toastRect, parentRect, popAt.Value, out Vector2 anchored))
        {
            toastRect.anchoredPosition = anchored;
            StartCoroutine(DriftXpToast(toastRect, anchored));
        }

        Destroy(xpGainInstance, xpGainAnimationDuration);
    }

    /// <summary>
    /// Where one toast has to sit so that it draws over <paramref name="worldPoint"/>.
    /// False means that point cannot be drawn: no camera, or a corpse BEHIND it, whose
    /// WorldToScreenPoint comes back mirrored through the screen centre. Putting a toast at
    /// a mirrored point lands it on the opposite side of the HUD from the kill, which reads
    /// as a bug rather than as feedback - the caller falls back to the bottom toast instead.
    /// </summary>
    private static bool TryToastPosition(RectTransform toast, RectTransform parent, Vector3 worldPoint, out Vector2 anchoredPosition)
    {
        anchoredPosition = Vector2.zero;

        // Camera.main at the dereference: this runs a handful of times a night, and the
        // camera is gone during teardown and for the first frames after a reload.
        Camera camera = Camera.main;
        if (camera == null || toast == null || parent == null) return false;

        Vector3 screenPoint = camera.WorldToScreenPoint(worldPoint);
        if (screenPoint.z < 0f) return false;

        // Keep it on screen. The margin is authored in canvas REFERENCE px (3840 x 2160,
        // match height), so it is turned into real pixels by the canvas's own scale factor
        // rather than assumed to be one to one - at 1080p that factor is 0.5.
        Canvas canvas = parent.GetComponentInParent<Canvas>();
        float scale = canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
        float margin = XpToastScreenMargin * scale;

        screenPoint.x = Mathf.Clamp(screenPoint.x, margin, Mathf.Max(margin, Screen.width - margin));
        screenPoint.y = Mathf.Clamp(screenPoint.y, margin, Mathf.Max(margin, Screen.height - margin));

        // The canvas is Screen Space - Overlay, so the camera argument is null. Passing the
        // game camera here is the classic way to get a toast that lands nowhere near the kill.
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenPoint, null, out Vector2 local))
        {
            return false;
        }

        // local is measured from the PARENT's pivot; anchoredPosition is measured from the
        // toast's own anchor, which on this prefab is (0.5, 0) of a 600 x 600 region sitting
        // at the bottom of the screen. Setting the raw local point would drop every toast by
        // the height of that anchor - which is the mistake this subtraction exists to avoid.
        Rect rect = parent.rect;
        Vector2 anchor = (toast.anchorMin + toast.anchorMax) * 0.5f;
        Vector2 anchorOrigin = new Vector2(rect.x + rect.width * anchor.x, rect.y + rect.height * anchor.y);

        anchoredPosition = local - anchorOrigin;
        return true;
    }

    /// <summary>
    /// Floats one corpse toast up over its life, so that two kills a second apart do not
    /// stack in the same spot. Runs on THIS rather than on the toast: the toast is
    /// Destroy()ed on a timer, and a coroutine started on it would be cut short by exactly
    /// the thing that ends it. The rect is null-checked every frame instead.
    ///
    /// Safe against the prefab's own animator, which drives m_AnchoredPosition.x and
    /// m_Alpha on the HUD_XPLog_Item CHILD - this moves the root, on y, and the two never
    /// touch the same curve.
    /// </summary>
    private IEnumerator DriftXpToast(RectTransform toast, Vector2 from)
    {
        float elapsed = 0f;
        while (elapsed < xpGainAnimationDuration)
        {
            if (toast == null) yield break;

            float t = xpGainAnimationDuration > 0f ? elapsed / xpGainAnimationDuration : 1f;
            toast.anchoredPosition = new Vector2(from.x, from.y + XpToastRiseReferencePx * t);

            // Scaled, to stay in step with Destroy()'s own delay.
            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    /// <summary>How far a corpse's toast floats, in canvas reference px over its whole life.</summary>
    private const float XpToastRiseReferencePx = 120f;

    /// <summary>
    /// Keep-inside-the-screen margin for a corpse toast, in canvas reference px. Wider than
    /// it looks it needs to be: the toast is 220 px across and pivots on its centre, so
    /// anything under 110 lets half the strip off the edge.
    /// </summary>
    private const float XpToastScreenMargin = 160f;

    /// <summary>
    /// Widens the XP toast to fit its text. The prefab's strip is a fixed 220 px, and the
    /// text sits in a HorizontalLayoutGroup whose padding is read rather than assumed. Only
    /// grows - a short toast keeps the prefab's width so the row of them stays even.
    /// </summary>
    private static void FitXpStripToText(Transform root, TextMeshProUGUI text)
    {
        float needed = text.GetPreferredValues(text.text).x + XpStripSlack;

        HorizontalLayoutGroup layout = text.GetComponentInParent<HorizontalLayoutGroup>();
        if (layout != null) needed += layout.padding.horizontal;

        for (Transform t = text.transform.parent; t != null; t = t.parent)
        {
            if (t is RectTransform rect && Mathf.Approximately(rect.anchorMin.x, rect.anchorMax.x) && rect.sizeDelta.x < needed)
            {
                rect.sizeDelta = new Vector2(needed, rect.sizeDelta.y);
            }

            if (t == root) break;
        }
    }

    /// <summary>
    /// Breathing room either side of the XP text, in reference px, on top of the layout's
    /// own padding. Small on purpose: measured 2026-09-06, "+76 XP" needs 141 px of text and
    /// 60 of padding, so 16 keeps it at the prefab's 220 and only a genuinely longer toast
    /// grows the strip.
    /// </summary>
    private const float XpStripSlack = 16f;

    private void LevelUp()
    {
        currentLevel++;

        // From the level, not by compounding: the discounted first threshold must not
        // propagate. Level 2->3 costs base x factor (150 by default), exactly as before
        // the discount existed; only 1->2 is cheap.
        xpToNextLevel = baseXpToLevelUp * Mathf.Pow(xpScalingFactor, currentLevel - 1);
        
        if (xpBar != null)
        {
            xpBar.maxValue = xpToNextLevel;
        }

        UpdateLevelText();
        ShowLevelUpAnimation();
    }

    private void ShowLevelUpAnimation()
    {
        if (levelUpAnimatorPrefab == null || targetCanvas == null) return;

        GameObject levelUpInstance = Instantiate(levelUpAnimatorPrefab, targetCanvas.transform);
        
        if (levelUpInstance.TryGetComponent<RectTransform>(out var rectTransform))
        {
            rectTransform.localScale = Vector3.one;
        }

        // Set level text
        Transform levelTextTransform = levelUpInstance.transform.Find(levelNumberTextPath);
        if (levelTextTransform != null)
        {
            if (levelTextTransform.TryGetComponent<TextMeshProUGUI>(out var levelNumberText))
            {
                levelNumberText.text = currentLevel.ToString();
            }
        }

        // Play animation
        if (levelUpInstance.TryGetComponent<Animator>(out var animator))
        {
            int layerIndex = animator.GetLayerIndex("Base Layer");
            if (layerIndex != -1 && animator.HasState(layerIndex, Animator.StringToHash("ANIM_HUD_Event_LevelUp_In")))
            {
                animator.Play("ANIM_HUD_Event_LevelUp_In", layerIndex);
            }
        }

        Destroy(levelUpInstance, 5f);
    }

    private void UpdateLevelText()
    {
        if (levelText != null)
        {
            levelText.text = currentXp <= 0 ? "XP" : currentLevel.ToString();
        }
    }

    // Public methods for external access
    public void AddHealth(float amount)
    {
        if (isDead) return;
        currentHealth = Mathf.Min(currentHealth + amount, maxHealth);
        if (healthBar != null)
        {
            healthBar.value = currentHealth;
        }

        // No SFX here: the consuming item owns its sound (medicine vs fish),
        // otherwise the medicine sound double-fires / plays for fish.

        UpdateLowHealthEffect();
    }

    public void AddHunger(float amount)
    {
        if (isDead) return;
        currentHunger = Mathf.Min(currentHunger + amount, maxHunger);
        if (hungerBar != null)
        {
            hungerBar.value = currentHunger;
        }

        // Eating is the only thing that lifts hunger, so it is the only edge that can end
        // the loop early. Waiting for the next hunger tick would hold the warning over the
        // meal for up to a tenth of a second.
        UpdateHungerLoop();
    }

    public bool CanUseSpecialMove(float staminaCost)
    {
        float currentStaminaPercentage = (currentStamina / maxStamina) * 100f;
        return currentStaminaPercentage >= staminaRequiredForSpecialMoves && currentStamina >= staminaCost;
    }

    public bool UseStaminaForDoubleJump()
    {
        if (CanUseSpecialMove(staminaCostDoubleJump))
        {
            float previousStamina = currentStamina;
            currentStamina -= staminaCostDoubleJump;
            if (staminaBar != null)
            {
                staminaBar.value = currentStamina;
            }
            AfterStaminaChanged(previousStamina);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Whether a slide is affordable right now, without spending anything. Callers must
    /// test with this and only call UseStaminaForSlide once the slide is actually going
    /// to happen.
    /// </summary>
    public bool CanAffordSlide() => CanUseSpecialMove(staminaCostSlide);

    public bool UseStaminaForSlide()
    {
        if (CanUseSpecialMove(staminaCostSlide))
        {
            float previousStamina = currentStamina;
            currentStamina -= staminaCostSlide;
            if (staminaBar != null)
            {
                staminaBar.value = currentStamina;
            }
            AfterStaminaChanged(previousStamina);
            return true;
        }
        return false;
    }

    public void SetSprinting(bool sprinting)
    {
        if (isDead) return;
        isSprinting = sprinting;
    }

    // Getter methods
    public float GetCurrentHealth() => currentHealth;

    /// <summary>For systems that reason in fractions (HeartbeatLayer).</summary>
    public float GetMaxHealth() => maxHealth;

    /// <summary>
    /// How full the belly is, 0-1. The hunger loop states both its threshold and its volume
    /// ramp as fractions, and maxHunger lives here, so the division does too. A zero max is
    /// a broken setup: report a full belly, so the warning sits silent rather than jammed on.
    /// </summary>
    public float GetHungerFraction() => maxHunger > 0f ? currentHunger / maxHunger : 1f;

    /// <summary>The full meter, so a consumer can feed by a fraction of it rather than by a guessed number.</summary>
    public float MaxHunger => maxHunger;

    /// <summary>
    /// Hysteresis: keep sprinting until the bar is empty, but require a small reserve
    /// before starting again. A plain "> 0" test oscillated at zero — the regen tick
    /// added enough to re-enable sprinting, the next drain tick emptied it again, and
    /// sprint flickered on and off every ~50ms (audible as fluttering footstep cadence).
    /// </summary>
    public bool HasStaminaForSprinting()
    {
        return isSprinting
            ? currentStamina > 0f
            : currentStamina >= maxStamina * SprintResumeFraction;
    }
    public bool IsDead() => isDead;

    private void UpdateLowHealthEffect()
    {
        if (vignetteAnimator == null) return;

        bool shouldBeLowHealth = currentHealth <= lowHealthThreshold;
        
        // Force update the state even if it hasn't changed
        isLowHealthEffectActive = shouldBeLowHealth;
        vignetteAnimator.SetBool(ActiveParameter, isLowHealthEffectActive);

        if (playerAudioManager != null)
        {
            playerAudioManager.UpdateLowHealthState(isLowHealthEffectActive);
        }
    }

    private void OnValidate()
    {
        if (lowHealthThreshold > midHealthThreshold)
        {
      //      Debug.LogWarning("Low health threshold should not be higher than mid health threshold. Adjusting...");
            lowHealthThreshold = midHealthThreshold;
        }
        
        if (midHealthThreshold > maxHealth)
        {
        //    Debug.LogWarning("Mid health threshold should not be higher than max health. Adjusting...");
            midHealthThreshold = maxHealth;
        }
    }
}