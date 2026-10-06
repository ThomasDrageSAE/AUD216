using UnityEngine;
using UnityStandardAssets.CrossPlatformInput;
using UnityStandardAssets.Utility;
using Random = UnityEngine.Random;
using System.Collections;
using System.Collections.Generic;
using Audio;

/*
 * FirstPersonController.cs
 * 
 * Purpose: Core player movement and interaction controller
 * Used by: Player character, main camera system
 * 
 * Key Features:
 * - Full first-person movement (walk, run, jump, slide)
 * - Camera control and head bobbing
 * - Surface-based footstep system
 * - Weapon/item management and switching
 * - Stamina-based movement mechanics
 * - Advanced movement features (double jump, sliding)
 * 
 * Movement Systems:
 * - Walking/Running with speed control
 * - Jump system with double jump capability
 * - Sliding mechanics with momentum
 * - Surface detection for audio/effects
 * 
 * Camera Features:
 * - Smooth mouse look controls
 * - FOV kick effects for movement
 * - Head bobbing during movement
 * - Camera shake system integration
 * 
 * Performance Considerations:
 * - Uses efficient physics checks
 * - Optimized camera updates
 * - Caches component references
 * - Uses coroutines for smooth transitions
 * 
 * Dependencies:
 * - Requires CharacterController component
 * - Uses PlayerStats for stamina management
 * - Integrates with Inventory system
 * - Requires properly configured input system
 */

namespace UnityStandardAssets.Characters.FirstPerson
{
    [RequireComponent(typeof(CharacterController))]
    public class FirstPersonController : MonoBehaviour
    {
        [Header("Movement Settings")]
        [SerializeField] private bool m_IsWalking;                    // Current walking state
        [SerializeField] private float m_WalkSpeed;                   // Base walking speed
        [SerializeField] private float m_RunSpeed;                    // Base running speed
        [SerializeField] [Range(0f, 1f)] private float m_RunstepLenghten;  // Step length modifier when running
        [SerializeField] private float m_JumpSpeed;                   // Initial jump velocity
        [SerializeField] private float m_StickToGroundForce;         // Force keeping player grounded
        [SerializeField] private float m_GravityMultiplier;          // Modifier for gravity strength

        [Header("Inventory Keys")]
        [Tooltip("A second press of the SAME number key within this many seconds drops that " +
                 "slot's item instead of selecting it. The owner's call (2026-09-06), replacing " +
                 "Shift+number - Shift is also sprint, and holding it to drop meant a sprint " +
                 "burst on every drop. Unscaled time, so the pause menu cannot stretch a tap.")]
        [SerializeField] private float slotDoubleTapSeconds = 0.6f;

        // The last number key pressed and when, for the double-tap. -1 is "nothing pending".
        private int lastSlotTapped = -1;
        private float lastSlotTapTime = float.NegativeInfinity;

        [Header("Look Settings")]
        [SerializeField] private MouseLook m_MouseLook;              // Mouse look control system
        
        [Header("Camera Effects")]
        [SerializeField] private bool m_UseFovKick;                  // Whether to use FOV effects
        [SerializeField] private FOVKick m_FovKick = new FOVKick();  // FOV kick effect parameters
        [SerializeField] private bool m_UseHeadBob;                  // Whether to use head bobbing
        [SerializeField] private CurveControlledBob m_HeadBob = new CurveControlledBob();  // Head bob parameters
        [SerializeField] private LerpControlledBob m_JumpBob = new LerpControlledBob();    // Jump bob parameters

        [SerializeField] private float m_StepInterval;
        [SerializeField] private SurfaceDetector surfaceDetector;
        [SerializeField] private FootstepAudioManager playerMovementAudio;

        [Header("Inventory")]
        [SerializeField] private Inventory inventory;
        private WeaponController activeWeapon;

        private Camera m_Camera;
        private bool m_Jump;
        private Vector2 m_Input;
        private Vector3 m_MoveDir = Vector3.zero;
        private CharacterController m_CharacterController;
        private CollisionFlags m_CollisionFlags;
        private bool m_PreviouslyGrounded;
        private Vector3 m_OriginalCameraPosition;
        private float m_StepCycle;
        private float m_NextStep;
        private bool m_Jumping;
        private bool isFalling;

        [Header("Jump Settings")]
        [SerializeField] private int maxJumps = 2;
        private int jumpsRemaining;

        [Header("Slide Settings")]
        [SerializeField] private float slideDuration = 1f;
        [SerializeField] private float slideYScale = 0.25f;
        [SerializeField] private float slideCameraYOffset = -1f;
        [SerializeField] private KeyCode slideKey = KeyCode.C;
        [SerializeField] private float minSpeedToSlide = 5f;
        [SerializeField] private float slideLerpSpeed = 10f;
        [SerializeField] private float slideMovementSpeed = 15f;

        private bool isSliding = false;
        private float slideTimer = 0f;
        private Vector3 slideDirection;
        private Coroutine m_FovKickRoutine;
        private float originalHeight;
        private Vector3 originalCenter;
        private float currentSlideHeight;
        private float currentCameraHeight;
        private Vector3 currentCharacterCenter;

        [SerializeField] private PlayerStats playerStats;

        private void Start()
        {
            m_CharacterController = GetComponent<CharacterController>();
            m_Camera = Camera.main;
            m_OriginalCameraPosition = m_Camera.transform.localPosition;
            m_FovKick.Setup(m_Camera);
            m_HeadBob.Setup(m_Camera, m_StepInterval);
            m_StepCycle = 0f;
            m_NextStep = m_StepCycle / 2f;
            m_Jumping = false;
            isFalling = false;
            m_MouseLook.Init(transform, m_Camera.transform);

            activeWeapon = null;

            if (inventory == null)
            {
                Debug.LogError("Inventory reference not set in FirstPersonController!");
            }

            jumpsRemaining = maxJumps;

            originalHeight = m_CharacterController.height;
            originalCenter = m_CharacterController.center;

            currentSlideHeight = originalHeight;
            currentCameraHeight = m_OriginalCameraPosition.y;
            currentCharacterCenter = originalCenter;

            if (playerStats == null)
            {
                // PlayerStats lives on a child object, not on this one, so GetComponent
                // here can never find it - the singleton is the working fallback.
                // Without it canSprint stays false and sprinting silently never works.
                playerStats = PlayerStats.Instance;
                if (playerStats == null)
                {
                    Debug.LogError("PlayerStats reference not set and no instance found - " +
                                   "sprinting will be unavailable!", this);
                }
            }

            if (playerMovementAudio == null)
            {
                Debug.LogWarning("FootstepAudioManager reference (playerMovementAudio) not set in FirstPersonController!", this);
            }
        }

        private void Update()
        {
            if (PauseMenu.IsGamePaused())
            {
                m_MouseLook.SetCursorLock(false);
                return;
            }
            else
            {
                m_MouseLook.SetCursorLock(true);
            }

            // Inventory slot keys. One press selects the slot; a second press of the SAME
            // key within slotDoubleTapSeconds DROPS it, the item landing in front of the
            // player as a pickup. A second press later than that is an ordinary select again
            // (which, on the slot already in hand, lowers the weapon - unchanged).
            int tapped = -1;
            if (Input.GetKeyDown(KeyCode.Alpha1)) tapped = 0;
            else if (Input.GetKeyDown(KeyCode.Alpha2)) tapped = 1;
            else if (Input.GetKeyDown(KeyCode.Alpha3)) tapped = 2;
            else if (Input.GetKeyDown(KeyCode.Alpha4)) tapped = 3;
            else if (Input.GetKeyDown(KeyCode.Alpha5)) tapped = 4;
            if (tapped >= 0) HandleSlotTap(tapped);

            // Handle weapon firing
            if (activeWeapon != null && Input.GetMouseButton(0))
            {
                activeWeapon.Fire();
            }

            RotateView();

            if (!m_Jump)
            {
                m_Jump = CrossPlatformInputManager.GetButtonDown("Jump");
            }

            if (!m_PreviouslyGrounded && m_CharacterController.isGrounded)
            {
                StartCoroutine(m_JumpBob.DoBobCycle());
                m_MoveDir.y = 0f;
                m_Jumping = false;
                jumpsRemaining = maxJumps;
                if (isFalling && playerMovementAudio != null)
                {
                    playerMovementAudio.PlayLandSound();
                    isFalling = false;
                }
            }

            if (!m_CharacterController.isGrounded && !m_Jumping && m_PreviouslyGrounded)
            {
                m_MoveDir.y = 0f;
            }

            m_PreviouslyGrounded = m_CharacterController.isGrounded;

            if (Input.GetKeyDown(slideKey) && CanInitiateSlide())
            {
                StartSlide();
            }
        }

        private void FixedUpdate()
        {
            float speed;
            GetInput(out speed);

            if (isSliding)
            {
                HandleSlide();
            }
            else
            {
                Vector3 desiredMove = transform.forward * m_Input.y + transform.right * m_Input.x;

                RaycastHit hitInfo;
                Physics.SphereCast(transform.position, m_CharacterController.radius, Vector3.down, out hitInfo,
                                   m_CharacterController.height / 2f, Physics.AllLayers, QueryTriggerInteraction.Ignore);
                desiredMove = Vector3.ProjectOnPlane(desiredMove, hitInfo.normal).normalized;

                m_MoveDir.x = desiredMove.x * speed;
                m_MoveDir.z = desiredMove.z * speed;

                if (m_CharacterController.isGrounded)
                {
                    m_MoveDir.y = -m_StickToGroundForce;
                    jumpsRemaining = maxJumps;

                    if (m_Jump)
                    {
                        m_MoveDir.y = m_JumpSpeed;
                        m_Jump = false;
                        m_Jumping = true;
                        jumpsRemaining--;
                        if (playerMovementAudio != null) playerMovementAudio.PlayJumpSound();
                    }
                }
                else
                {
                    if (m_Jump && jumpsRemaining > 0)
                    {
                        if (jumpsRemaining < maxJumps && playerStats != null && !playerStats.UseStaminaForDoubleJump())
                        {
                            m_Jump = false;
                        }
                        else
                        {
                            m_MoveDir.y = m_JumpSpeed;
                            m_Jump = false;
                            jumpsRemaining--;
                            if (playerMovementAudio != null) playerMovementAudio.PlayJumpSound();
                        }
                    }
                    else
                    {
                        // Out of jumps: drop the queued press. It used to survive until
                        // landing, so a third press in mid-air bounced you straight back
                        // up on touchdown with the jump SFX over the land SFX.
                        m_Jump = false;
                    }

                    m_MoveDir += Physics.gravity * m_GravityMultiplier * Time.fixedDeltaTime;
                    isFalling = true;
                }

                m_CollisionFlags = m_CharacterController.Move(m_MoveDir * Time.fixedDeltaTime);

                ProgressStepCycle(speed);
                UpdateCameraPosition(speed);

                if (activeWeapon != null)
                {
                    float movementSpeed = new Vector2(m_MoveDir.x, m_MoveDir.z).magnitude;

                    // Real movement, not "not sprinting": m_IsWalking is true while
                    // standing still, so passing it as the moving flag meant the weapon
                    // never settled back to its neutral pose.
                    bool isMoving = m_Input.sqrMagnitude > 0.01f && m_CharacterController.isGrounded;
                    activeWeapon.UpdateWeaponBob(isMoving, !m_IsWalking, movementSpeed);
                }
            }
        }

        private void ProgressStepCycle(float speed)
        {
            if (m_CharacterController.velocity.sqrMagnitude > 0 && (m_Input.x != 0 || m_Input.y != 0))
            {
                m_StepCycle += (m_CharacterController.velocity.magnitude + (speed * (m_IsWalking ? 1f : m_RunstepLenghten))) *
                             Time.fixedDeltaTime;
            }

            if (!(m_StepCycle > m_NextStep)) return;

            m_NextStep = m_StepCycle + m_StepInterval;
            PlayFootstepSound();
        }

        private void PlayFootstepSound()
        {
            if (!m_CharacterController.isGrounded || playerMovementAudio == null) return;
            string surfaceType = surfaceDetector.GetSurfaceType(transform.position);
            playerMovementAudio.PlayFootstep(surfaceType);
        }

        private void UpdateCameraPosition(float speed)
        {
            if (!m_UseHeadBob) return;

            Vector3 newCameraPosition;
            if (m_CharacterController.velocity.magnitude > 0 && m_CharacterController.isGrounded)
            {
                m_Camera.transform.localPosition =
                    m_HeadBob.DoHeadBob(m_CharacterController.velocity.magnitude +
                                      (speed * (m_IsWalking ? 1f : m_RunstepLenghten)));
                newCameraPosition = m_Camera.transform.localPosition;
                newCameraPosition.y = m_Camera.transform.localPosition.y - m_JumpBob.Offset();
            }
            else
            {
                newCameraPosition = m_Camera.transform.localPosition;
                newCameraPosition.y = m_OriginalCameraPosition.y - m_JumpBob.Offset();
            }
            m_Camera.transform.localPosition = newCameraPosition;
        }

        private void GetInput(out float speed)
        {
            float horizontal = CrossPlatformInputManager.GetAxis("Horizontal");
            float vertical = CrossPlatformInputManager.GetAxis("Vertical");

            bool waswalking = m_IsWalking;

#if !MOBILE_INPUT
            bool canSprint = playerStats != null && playerStats.HasStaminaForSprinting();
            m_IsWalking = !Input.GetKey(KeyCode.LeftShift) || !canSprint;

            if (playerStats != null)
            {
                playerStats.SetSprinting(!m_IsWalking && (horizontal != 0 || vertical != 0));
            }
#endif
            speed = m_IsWalking ? m_WalkSpeed : m_RunSpeed;
            m_Input = new Vector2(horizontal, vertical);

            if (m_Input.sqrMagnitude > 1)
            {
                m_Input.Normalize();
            }

            if (m_IsWalking != waswalking && m_UseFovKick && m_CharacterController.velocity.sqrMagnitude > 0)
            {
                // Only restart the FOV kick — StopAllCoroutines() here also killed
                // the jump bob and slide stand-up lerp mid-transition.
                if (m_FovKickRoutine != null) StopCoroutine(m_FovKickRoutine);
                m_FovKickRoutine = StartCoroutine(!m_IsWalking ? m_FovKick.FOVKickUp() : m_FovKick.FOVKickDown());
            }

            if (isSliding)
            {
                m_Input = Vector2.zero;
                speed = 0;
            }
        }

        private void RotateView()
        {
            m_MouseLook.LookRotation(transform, m_Camera.transform);
        }

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            Rigidbody body = hit.collider.attachedRigidbody;
            if (m_CollisionFlags == CollisionFlags.Below) return;
            if (body == null || body.isKinematic) return;
            
            body.AddForceAtPosition(m_CharacterController.velocity * 0.1f, hit.point, ForceMode.Impulse);
        }

        private void HandleSlotTap(int slotIndex)
        {
            bool secondTap = slotIndex == lastSlotTapped
                             && Time.unscaledTime - lastSlotTapTime <= slotDoubleTapSeconds;

            if (secondTap)
            {
                // Consumed: a third press starts a fresh count rather than dropping again.
                lastSlotTapped = -1;
                DropInventorySlot(slotIndex);
                return;
            }

            lastSlotTapped = slotIndex;
            lastSlotTapTime = Time.unscaledTime;
            SelectInventorySlot(slotIndex);
        }

#if UNITY_EDITOR
        /// <summary>Editor-only: a number-key press, for a check that cannot press keys.</summary>
        public void EditorTapSlot(int slotIndex) => HandleSlotTap(slotIndex);
#endif

        /// <summary>
        /// Drops the item in a slot at the player's feet, where it becomes an ordinary
        /// pickup again (a double-tap of its number key). A raised weapon or rod is lowered first, so
        /// nothing stays in the hands after its item has left the inventory.
        /// </summary>
        public void DropInventorySlot(int slotIndex)
        {
            Item item = inventory != null ? inventory.GetItem(slotIndex) : null;
            if (item == null) return;

            if (item is WeaponItem weaponItem)
            {
                weaponItem.ForceToLowered();
                if (activeWeapon == weaponItem.GetWeaponController()) activeWeapon = null;
            }
            else if (item is FishingRodItem fishingRod)
            {
                fishingRod.ForceToLowered();
            }

            inventory.RemoveItem(slotIndex);

            // 0.8m IN FRONT of the player and 1.2m above the ground (owner requests
            // 2026-09-02/03): dropped at the feet, the item hid inside the player's own
            // view until they stepped backwards. The forward probe stops short of a wall
            // so the item cannot be pushed through one; the down ray then grounds the
            // height from the forward point, so dropping toward a ledge follows the drop.
            const float dropForward = 0.8f;
            Vector3 forward = transform.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 0.001f ? forward.normalized : Vector3.forward;

            float reach = dropForward;
            Vector3 probeOrigin = transform.position;
            if (Physics.Raycast(probeOrigin, forward, out RaycastHit wall, dropForward + 0.2f))
            {
                reach = Mathf.Max(0.1f, wall.distance - 0.2f);
            }

            Vector3 dropPoint = transform.position + forward * reach;
            if (Physics.Raycast(dropPoint, Vector3.down, out RaycastHit ground, 5f))
            {
                dropPoint = ground.point + Vector3.up * 1.2f;
            }

            item.Drop(dropPoint);

            if (UIAudioManager.Instance != null)
            {
                UIAudioManager.Instance.PlayDropSound();
            }
        }

        public void SelectInventorySlot(int slotIndex)
        {
            Item selectedItem = inventory.GetItem(slotIndex);

            if (selectedItem == null)
            {
                if (activeWeapon != null && activeWeapon.IsWeaponRaised())
                {
                    activeWeapon.ToggleWeaponPosition();
                    activeWeapon = null;
                }
                return;
            }

            if (selectedItem is WeaponItem weaponItem)
            {
                HandleWeaponSelection(weaponItem);
            }
            else if (selectedItem is FishingRodItem fishingRodItem)
            {
                // Lower any raised weapon before using fishing rod
                if (activeWeapon != null && activeWeapon.IsWeaponRaised())
                {
                    activeWeapon.ToggleWeaponPosition();
                    activeWeapon = null;
                }
                
                fishingRodItem.UseItem();
            }
            else
            {
                selectedItem.UseItem();
            }
        }

        private void HandleWeaponSelection(WeaponItem weaponItem)
        {
            WeaponController weaponController = weaponItem.GetWeaponController();
            if (weaponController != null)
            {
                // Always lower fishing rod if it's raised when selecting a weapon
                // Iterate slots, not the item *count*: RemoveItem nulls a slot in place
                // without compacting, so a rod in a later slot was missed and left
                // raised alongside the drawn weapon.
                FishingRodItem fishingRod = null;
                Item[] slots = inventory.GetAllItems();
                for (int i = 0; i < slots.Length; i++)
                {
                    if (slots[i] is FishingRodItem rod)
                    {
                        fishingRod = rod;
                        break;
                    }
                }
                
                if (fishingRod != null)
                {
                    fishingRod.ForceToLowered();
                }

                if (activeWeapon == weaponController)
                {
                    weaponController.ToggleWeaponPosition();
                    if (!weaponController.IsWeaponRaised())
                    {
                        activeWeapon = null;
                    }
                }
                else
                {
                    if (activeWeapon != null && activeWeapon.IsWeaponRaised())
                    {
                        activeWeapon.ToggleWeaponPosition();
                    }

                    activeWeapon = weaponController;
                    if (!weaponController.IsWeaponRaised())
                    {
                        weaponController.ToggleWeaponPosition();
                    }
                }
            }
            else
            {
                Debug.LogError($"No weapon controller found for {weaponItem.GetItemName()}");
            }
        }

        private bool CanInitiateSlide()
        {
            bool isSprinting = !m_IsWalking;
            bool hasSpeed = m_CharacterController.velocity.magnitude > minSpeedToSlide;
            // Check affordability only. UseStaminaForSlide() *spends* stamina, and being
            // evaluated here meant every rejected slide (standing still, mid-air,
            // already sliding) silently drained the bar.
            bool hasStamina = playerStats != null && playerStats.CanAffordSlide();
            return m_CharacterController.isGrounded && isSprinting && hasSpeed && !isSliding && hasStamina;
        }

        private void StartSlide()
        {
            // Charge for the slide here, where it actually happens.
            if (playerStats != null)
            {
                playerStats.UseStaminaForSlide();
            }

            isSliding = true;
            slideTimer = slideDuration;
            
            slideDirection = transform.forward * m_Input.y + transform.right * m_Input.x;
            slideDirection.Normalize();

            if (m_UseFovKick)
            {
                if (m_FovKickRoutine != null) StopCoroutine(m_FovKickRoutine);
                m_FovKickRoutine = StartCoroutine(m_FovKick.FOVKickUp());
            }
        }

        private void HandleSlide()
        {
            if (slideTimer > 0)
            {
                float targetHeight = originalHeight * slideYScale;
                currentSlideHeight = Mathf.Lerp(currentSlideHeight, targetHeight, Time.deltaTime * slideLerpSpeed);
                m_CharacterController.height = currentSlideHeight;

                Vector3 targetCenter = new Vector3(originalCenter.x, originalCenter.y * slideYScale, originalCenter.z);
                currentCharacterCenter = Vector3.Lerp(currentCharacterCenter, targetCenter, Time.deltaTime * slideLerpSpeed);
                m_CharacterController.center = currentCharacterCenter;

                float targetCameraHeight = m_OriginalCameraPosition.y + slideCameraYOffset;
                currentCameraHeight = Mathf.Lerp(currentCameraHeight, targetCameraHeight, Time.deltaTime * slideLerpSpeed);
                m_Camera.transform.localPosition = new Vector3(
                    m_Camera.transform.localPosition.x,
                    currentCameraHeight,
                    m_Camera.transform.localPosition.z
                );

                float speedMultiplier = slideTimer / slideDuration;
                Vector3 slideMove = slideDirection * slideMovementSpeed * speedMultiplier;
                slideMove.y = m_MoveDir.y;
                
                m_CollisionFlags = m_CharacterController.Move(slideMove * Time.fixedDeltaTime);
                
                slideTimer -= Time.fixedDeltaTime;
            }
            else
            {
                EndSlide();
            }
        }

        private void EndSlide()
        {
            isSliding = false;
            StartCoroutine(LerpToStanding());
        }

        private IEnumerator LerpToStanding()
        {
            float lerpTime = 0f;
            float returnDuration = 0.5f;

            float startHeight = currentSlideHeight;
            Vector3 startCenter = currentCharacterCenter;
            float startCameraHeight = currentCameraHeight;

            while (lerpTime < returnDuration)
            {
                lerpTime += Time.deltaTime;
                float t = lerpTime / returnDuration;

                currentSlideHeight = Mathf.Lerp(startHeight, originalHeight, t);
                currentCharacterCenter = Vector3.Lerp(startCenter, originalCenter, t);
                currentCameraHeight = Mathf.Lerp(startCameraHeight, m_OriginalCameraPosition.y, t);

                m_CharacterController.height = currentSlideHeight;
                m_CharacterController.center = currentCharacterCenter;
                m_Camera.transform.localPosition = new Vector3(
                    m_Camera.transform.localPosition.x,
                    currentCameraHeight,
                    m_Camera.transform.localPosition.z
                );

                yield return null;
            }

            m_CharacterController.height = originalHeight;
            m_CharacterController.center = originalCenter;
            m_Camera.transform.localPosition = m_OriginalCameraPosition;

            if (m_UseFovKick)
            {
                StartCoroutine(m_FovKick.FOVKickDown());
            }
        }

        public Inventory GetInventory()
        {
            return inventory;
        }
    }
}
