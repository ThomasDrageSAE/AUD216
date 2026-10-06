using UnityEngine;
using System.Collections;

public class CameraShakeController : MonoBehaviour
{
    private static CameraShakeController s_Instance;
    public static CameraShakeController Instance => s_Instance;

    [Header("References")]
    [Tooltip("Reference to the camera that will be shaken")]
    [SerializeField] private Camera m_Camera;
    
    [Tooltip("Reference to the FirstPersonController")]
    [SerializeField] private UnityStandardAssets.Characters.FirstPerson.FirstPersonController m_FirstPersonController;

    private Transform m_CameraTransform;
    private bool m_IsShaking;

    /// <summary>
    /// The offset this controller put on the camera last frame, so the next frame can take
    /// it off again.
    /// </summary>
    /// <remarks>
    /// The shake is ADDITIVE - subtract what was added, add what this frame wants - rather
    /// than a write of "rest pose + shake". FirstPersonController owns the camera's
    /// localPosition: it writes it for the head bob (:349, :360) and for the slide, which
    /// holds the camera at slideCameraYOffset below standing (:626-630, :672-676). A shake
    /// that snapped to a pose remembered in Awake therefore stood the player up for the
    /// length of every shot fired mid-slide, and fought the bob on every shot fired while
    /// walking. Riding on whatever the controller wrote this frame leaves both alone.
    /// </remarks>
    private Vector3 m_AppliedOffset;

    private void Awake()
    {
        if (s_Instance == null)
        {
            s_Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        // Validate required references
        if (m_Camera == null)
        {
            Debug.LogError("CameraShakeController: Camera reference not set in inspector!");
            enabled = false;
            return;
        }

        m_CameraTransform = m_Camera.transform;
    }

    public void ShakeCamera(float _duration, float _magnitude, float _roughness, Vector3 _direction)
    {
        if (m_IsShaking || m_CameraTransform == null)
        {
            return;
        }
        
        StartCoroutine(ShakeCoroutine(_duration, _magnitude, _roughness, _direction));
    }

    private IEnumerator ShakeCoroutine(float _duration, float _magnitude, float _roughness, Vector3 _direction)
    {
        m_IsShaking = true;
        float elapsed = 0f;
        
        while (elapsed < _duration)
        {
            float dampingFactor = 1f - (elapsed / _duration);
            float noiseOffset = Random.Range(0f, 1000f);
            
            Vector3 noise = new Vector3(
                Mathf.PerlinNoise(Time.time * _roughness, noiseOffset) * 2f - 1f,
                Mathf.PerlinNoise(Time.time * _roughness, noiseOffset + 1f) * 2f - 1f,
                Mathf.PerlinNoise(Time.time * _roughness, noiseOffset + 2f) * 2f - 1f
            );

            Vector3 shake = Vector3.Scale(_direction, noise) * _magnitude * dampingFactor;

            // Off with last frame's, on with this frame's, over whatever pose the
            // controller has written in between - see m_AppliedOffset.
            m_CameraTransform.localPosition += shake - m_AppliedOffset;
            m_AppliedOffset = shake;

            // Unscaled, so a shake started as the player dies finishes under the death
            // screen instead of freezing the camera off-centre for as long as it is up.
            // The damping is a ratio of two accumulated seconds, so it cannot overshoot
            // after a hitch; the loop simply ends.
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        // Only this controller's own offset comes off. Whatever the camera was holding
        // underneath it - slide height, bob, the jump bob - stays.
        m_CameraTransform.localPosition -= m_AppliedOffset;
        m_AppliedOffset = Vector3.zero;
        m_IsShaking = false;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (m_Camera == null)
        {
            Debug.LogWarning("CameraShakeController: Please assign the Camera reference in the inspector!");
        }
    }
#endif
} 