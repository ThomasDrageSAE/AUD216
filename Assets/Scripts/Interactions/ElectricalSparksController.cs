using UnityEngine;
using DG.Tweening; // Ensure this is present

public class ElectricalSparksController : MonoBehaviour
{
    [SerializeField]
    private ParticleSystem sparksParticleSystem; // Assign in Inspector

    [SerializeField]
    private float fadeDuration = 1f; // Duration for fading out sparks

    [SerializeField]
    private ActionHandler actionHandler; // Assign in Inspector

    private bool isActive = true;

    // The pending "stop and hide" call from DeactivateSparks, kept so it can be cancelled.
    private Tween deactivateCall;

    private void KillPendingDeactivation()
    {
        if (deactivateCall != null)
        {
            deactivateCall.Kill();
            deactivateCall = null;
        }
    }

    void Start()
    {
        // Subscribe to the onPowerToggled event
        if (actionHandler != null)
        {
            actionHandler.onPowerToggled.AddListener(HandlePowerToggle);
        }
        else
        {
            Debug.LogError("ActionHandler is not assigned in ElectricalSparksController.");
        }

        // Ensure that the sparks are active at the start
        if (sparksParticleSystem != null)
        {
            sparksParticleSystem.Play();
        }
        else
        {
            Debug.LogWarning("Sparks Particle System is not assigned in ElectricalSparksController.");
        }
    }

    void OnDestroy()
    {
        // Unsubscribe to prevent memory leaks
        if (actionHandler != null)
        {
            actionHandler.onPowerToggled.RemoveListener(HandlePowerToggle);
        }

        KillPendingDeactivation();
    }

    void HandlePowerToggle(bool isPowerOn)
    {
        if (isPowerOn)
        {
            DeactivateSparks();
        }
        else
        {
            ActivateSparks();
        }
    }

    public void ActivateSparks()
    {
        // Cancel a pending deactivation. Toggling power off then on again inside
        // fadeDuration otherwise let the stale call fire and switch the sparks back off
        // while the power was on.
        KillPendingDeactivation();

        isActive = true;
        gameObject.SetActive(true);
        if (sparksParticleSystem != null)
        {
            sparksParticleSystem.Play();
            // Reset the start color alpha to 1
            var main = sparksParticleSystem.main;
            Color currentColor = main.startColor.color;
            main.startColor = new Color(currentColor.r, currentColor.g, currentColor.b, 1f);
        }
    }

    public void DeactivateSparks()
    {
        if (isActive)
        {
            isActive = false;
            if (sparksParticleSystem != null)
            {
                // Fade out the particle system's start color alpha
                var main = sparksParticleSystem.main;
                Color currentColor = main.startColor.color;
                Color targetColor = new Color(currentColor.r, currentColor.g, currentColor.b, 0f);
                main.startColor = targetColor;

                // Schedule deactivation after fade duration. Tracked so it can be
                // cancelled - it would otherwise also run against a destroyed object
                // during scene teardown.
                KillPendingDeactivation();
                deactivateCall = DOVirtual.DelayedCall(fadeDuration, () =>
                {
                    if (sparksParticleSystem != null) sparksParticleSystem.Stop();
                    gameObject.SetActive(false);
                    deactivateCall = null;
                });
            }
            else
            {
                gameObject.SetActive(false);
                Debug.LogWarning("Sparks Particle System is not assigned in ElectricalSparksController.");
            }
        }
    }
}
