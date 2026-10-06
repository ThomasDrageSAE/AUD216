using UnityEngine;
using UnityEngine.Events;
using DG.Tweening;

public class ActionHandler : MonoBehaviour
{
    [System.Serializable]
    public class PowerToggleEvent : UnityEvent<bool> { }

    [Header("Events")]
    public PowerToggleEvent onPowerToggled;

    [Header("Mesh Renderers")]
    [SerializeField] private ToggleMeshRenderers meshRendererManager;

    [Header("Switch GameObjects")]
    [SerializeField] private GameObject lightSwitchObject;
    [SerializeField] private GameObject powerBoxLeverObject;

    [Header("Control Groups")]
    [SerializeField] private GameObject studioSpotlights;

    [Header("Electrical Sparks")]
    [SerializeField] private ElectricalSparksController electricalSparksController;

    [Header("Emission Control")]
    [SerializeField] private GameObject ceilingLightsParent;
    [SerializeField] private string emissionPropertyName = "_EmissionColor";

    private Renderer[] emissionRenderers;

    private bool isAnimating = false;
    private bool isLightSwitchOn = false;
    private bool isPowerOn = false;

    private bool hasElectricalBoxBeenUsed = false;
    private bool hasLightsBeenTurnedOnFirstTime = false;
    private bool hasMixingDeskBeenUsedFirstTime = false;

    public bool IsAnimating 
    { 
        get { return isAnimating; }
        private set { isAnimating = value; }
    }

    public bool IsPowerOn => isPowerOn;

    public bool HasElectricalBoxBeenUsed => hasElectricalBoxBeenUsed;
    public bool HasLightsBeenTurnedOnFirstTime => hasLightsBeenTurnedOnFirstTime;
    public bool HasMixingDeskBeenUsedFirstTime => hasMixingDeskBeenUsedFirstTime;

    /// <summary>
    /// Records the first use of the mixing desk. Called by InteractionSystemTMP, which is
    /// where the desk is actually toggled - the only previous assignment lived inside
    /// ToggleMusic, whose body was commented out (and is now gone with the legacy
    /// MusicController), so this could never become true and FirstTimeInteractionTracker
    /// polled for it forever.
    /// </summary>
    public void MarkMixingDeskUsed()
    {
        hasMixingDeskBeenUsedFirstTime = true;
    }

    void Awake()
    {
        InitializeLightGroup();
        InitializeElectricalSparks();
    }

    void Start()
    {
        InitializeEmissionStates();
        InitializeEmissionRenderers();
    }

    public void HandleAction(string actionText, GameObject objectToInteractWith, GameObject switchObject)
    {
        if (isAnimating) return;

        switch (actionText.ToLower())
        {
            case "turn on the lights":
            case "turn off the lights":
                ToggleLights(studioSpotlights, switchObject);
                break;
            case "turn on the power":
            case "turn off the power":
                TogglePower();
                break;
            case "play music":
            case "stop music":
                ToggleMusic();
                break;
            default:
                Debug.LogWarning($"Unknown action: {actionText}");
                break;
        }

        // Remove this line
        // EventManager.Instance.ShowInteractionPrompt(actionText);
    }

    private void ToggleLights(GameObject studioSpotlightsParam, GameObject switchObject)
    {
        if (IsAnimating || lightSwitchObject == null) return;

        IsAnimating = true;
        Transform switchTransform = lightSwitchObject.transform;
        float targetRotation = isLightSwitchOn ? -16f : 1f;

        switchTransform.DOLocalRotate(new Vector3(targetRotation, switchTransform.localEulerAngles.y, switchTransform.localEulerAngles.z), 0.1f)
            .SetEase(Ease.InOutSine)
            .OnComplete(() =>
            {
                isLightSwitchOn = !isLightSwitchOn;
                
                if (isPowerOn && isLightSwitchOn && !hasLightsBeenTurnedOnFirstTime)
                {
                    hasLightsBeenTurnedOnFirstTime = true;
                }
                
                UpdateLightState();
                IsAnimating = false;
            });
    }

    private void UpdateLightState()
    {
        bool shouldLightsBeOn = isPowerOn && isLightSwitchOn;
        
        if (shouldLightsBeOn && !hasLightsBeenTurnedOnFirstTime)
        {
            hasLightsBeenTurnedOnFirstTime = true;
        }

        if (studioSpotlights != null)
        {
            studioSpotlights.SetActive(shouldLightsBeOn);
        }
        ToggleEmission(shouldLightsBeOn);

        string messageKey;
        if (!isPowerOn)
        {
            messageKey = "LightsPowerOff";
        }
        else
        {
            messageKey = isLightSwitchOn ? "LightsOnPowerOn" : "LightsOffPowerOn";
        }
        EventManager.Instance.ShowInteractionPrompt(InteractionMessages.GetMessage(messageKey), 3f);
    }

    private Material[] emissionMaterials;

    private void ToggleEmission(bool isOn)
    {
        if (emissionRenderers != null && emissionRenderers.Length > 0)
        {
            // Cache the per-renderer material instances once instead of hitting
            // renderer.material (which instantiates/looks up a copy) every toggle.
            if (emissionMaterials == null || emissionMaterials.Length != emissionRenderers.Length)
            {
                emissionMaterials = new Material[emissionRenderers.Length];
                for (int i = 0; i < emissionRenderers.Length; i++)
                {
                    emissionMaterials[i] = emissionRenderers[i].material;
                }
            }

            for (int i = 0; i < emissionMaterials.Length; i++)
            {
                Material material = emissionMaterials[i];
                if (material == null) continue;
                if (material.HasProperty(emissionPropertyName))
                {
                    if (isOn)
                    {
                        material.EnableKeyword("_EMISSION");
                        material.SetColor(emissionPropertyName, Color.white); // You can adjust the color as needed
                    }
                    else
                    {
                        material.DisableKeyword("_EMISSION");
                        material.SetColor(emissionPropertyName, Color.black);
                    }
                }
                else
                {
                    Debug.LogWarning($"Material on {emissionRenderers[i].gameObject.name} does not have the emission property: {emissionPropertyName}");
                }
            }
        }
        else
        {
            Debug.LogWarning("No emission renderers found. Make sure the Ceiling Lights Parent is assigned and has child objects with renderers.");
        }
    }

    private void ToggleMusic()
    {
        if (!isPowerOn)
        {
            EventManager.Instance.ShowInteractionPrompt(InteractionMessages.GetMessage("MusicPowerOff"), 3f);
            return;
        }

        // Nothing further: the desk's audio is InteractionSystemTMP's job, through
        // InteractionAudioManager.PlayMixingDeskAudio. The commented-out body that used
        // to sit here drove the legacy MusicController, deleted 2026-09-03 with its
        // class. This method survives only to refuse the action while the power is off.
    }

    private void TogglePower()
    {
        if (IsAnimating || powerBoxLeverObject == null) return;

        IsAnimating = true;
        Transform powerSwitchTransform = powerBoxLeverObject.transform;
        float targetRotation = isPowerOn ? 0f : 60f;

        powerSwitchTransform.DOLocalRotate(new Vector3(targetRotation, powerSwitchTransform.localEulerAngles.y, powerSwitchTransform.localEulerAngles.z), 0.5f)
            .SetEase(Ease.InOutSine)
            .OnComplete(() =>
            {
                if (meshRendererManager != null)
                {
                    meshRendererManager.ToggleEmissionStates();
                }
                isPowerOn = !isPowerOn;

                if (isPowerOn && isLightSwitchOn && !hasLightsBeenTurnedOnFirstTime)
                {
                    hasLightsBeenTurnedOnFirstTime = true;
                }

                if (isPowerOn && !hasElectricalBoxBeenUsed)
                {
                    hasElectricalBoxBeenUsed = true;
                }

                // ElectricalSparksController subscribes to onPowerToggled and handles the
                // change itself, so calling it directly here made every toggle run twice.
                UpdatePowerState();
                UpdateLightStateWithoutMessage();
                //UpdateMusicState();
                
                IsAnimating = false;
                onPowerToggled?.Invoke(isPowerOn);
                NotifyInteractionSystems();
            });
    }

    private void UpdatePowerState()
    {
        string messageKey = isPowerOn ? "PowerOn" : "PowerOff";
        EventManager.Instance.ShowInteractionPrompt(InteractionMessages.GetMessage(messageKey), 3f);
    }

    private void UpdateLightStateWithoutMessage()
    {
        bool shouldLightsBeOn = isPowerOn && isLightSwitchOn;
        
        if (studioSpotlights != null)
        {
            studioSpotlights.SetActive(shouldLightsBeOn);
        }
        ToggleEmission(shouldLightsBeOn);
    }

    private void NotifyInteractionSystems()
    {
        InteractionSystemTMP[] interactionSystems = FindObjectsByType<InteractionSystemTMP>(FindObjectsSortMode.None);
        foreach (var system in interactionSystems)
        {
            system.UpdateInteractability();
        }
    }

    private void ToggleElectricalSparks()
    {
        if (electricalSparksController != null)
        {
            if (isPowerOn)
            {
                electricalSparksController.DeactivateSparks();
            }
            else
            {
                electricalSparksController.ActivateSparks();
            }
        }
        else
        {
            Debug.LogError("ElectricalSparksController is null in ActionHandler");
        }
    }

    private void InitializeLightGroup()
    {
        if (studioSpotlights != null)
        {
            studioSpotlights.SetActive(Application.isPlaying ? false : true);
            isLightSwitchOn = !Application.isPlaying;
        }
    }

    private void InitializeElectricalSparks()
    {
        if (electricalSparksController != null)
        {
            electricalSparksController.gameObject.SetActive(!isPowerOn);
        }
    }

    private void InitializeEmissionStates()
    {
        if (meshRendererManager != null)
        {
            meshRendererManager.InitializeEmissionState();
        }
        else
        {
            Debug.LogWarning("MeshRendererManager is not assigned in ActionHandler.");
        }
    }

    private void InitializeEmissionRenderers()
    {
        if (ceilingLightsParent != null)
        {
            emissionRenderers = ceilingLightsParent.GetComponentsInChildren<Renderer>();
            if (emissionRenderers.Length == 0)
            {
                Debug.LogWarning("No renderers found in the children of the Ceiling Lights Parent object.");
            }
        }
        else
        {
            Debug.LogWarning("Ceiling Lights Parent is not assigned in ActionHandler.");
        }
    }

}