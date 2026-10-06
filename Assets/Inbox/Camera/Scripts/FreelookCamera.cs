using UnityEngine;
using UnityEngine.Serialization;

public class FreelookCamera : MonoBehaviour {	
	[Tooltip("Default speed in m/s")]
	public float Speed = 10;
	[FormerlySerializedAs("EnableFastSpeed")]
	[Tooltip("Whether to use the faster speed option while holding the boost key")]
	public bool EnableBoostSpeed = true;
	[FormerlySerializedAs("FastSpeed")]
	[Tooltip("Speed in m/s while holding the boost key")]
	public float BoostSpeed = 20;

	[Tooltip("Hotkey used to boost movement speed")]
	public KeyCode BoostKey = KeyCode.LeftShift;

	[Tooltip("The speed at which your camera rotates when moving the mouse")]
	public float MouseSensitivity = 3;

	[Tooltip("Whether the freelook is initially enabled")]
	public bool IsEnabled = true;		//intern isEnabled bool. The reason we dont use MonoBehaviour's .enabled property is so that we can turn it back on again with the hotkey in Update

	[Tooltip("Whether to lock the cursor while using the freelook camera")]
	public bool LockCursor = true;

	[Tooltip("The hotkey used to enable or disable the freelook camera script")]
	public KeyCode ToggleKey = KeyCode.T;	//hotkey used for enabling or disabling the freelook script

	[Tooltip("Hotkey used to move upwards on the vertical world axis")]
	public KeyCode UpKey = KeyCode.Space;

	[Tooltip("Hotkey used to move downwards on the vertical world axis")]
	public KeyCode DownKey = KeyCode.C;

	private Quaternion originalRotation;
	private float rotationX;
	private float rotationY;

	private Transform myTransform;			//cache of the .tranform property for better performance

	private bool wasUsingKinematic;			//was the (if attached) rigidbody using Kinamtic mode before the freelook was enabled?

	// Use this for initialization
	public void Start () {
		myTransform = transform;
		originalRotation = myTransform.localRotation;

		if(IsEnabled)
			EnableNoClip();
	}

	//called by unity
	public void OnEnable() {
		if(IsEnabled)
			EnableNoClip();
	}

	//called by unity
	public void OnDisable() {
		if(IsEnabled)
			DisableNoClip();
	}
	
	// Update is called once per frame
	public void Update () {
		#region Inputhandling
		//handle enabling/disabling of the component
		if( Input.GetKeyUp (ToggleKey) ) {
			if( IsEnabled ) {
				DisableNoClip();
			}
			else {
				EnableNoClip();
			}
		}

		//lock the cursor if specified to do so in the inspector
		if (LockCursor) {
			Cursor.lockState = CursorLockMode.Locked;
			Cursor.visible = false;
			//Screen.lockCursor = true;	//this was the pre-Unity 5.0 way of locking the cursor
		}

		//Get the speed based on user input
		float currentSpeed = Speed;
		if(EnableBoostSpeed && Input.GetKey(BoostKey)) {
			currentSpeed = BoostSpeed;
		}

		float verticalMovement = 0;
		if (Input.GetKey(UpKey)) {
			verticalMovement += 1f;
		}

		if (Input.GetKey(DownKey)) {
			verticalMovement -= 1f;
		}
		#endregion

		if(!IsEnabled) return;

		#region MouseLook
		// Read the mouse input axis
		rotationX += Input.GetAxis("Mouse X") * MouseSensitivity;
		rotationY += Input.GetAxis("Mouse Y") * MouseSensitivity;

		rotationY = Mathf.Clamp(rotationY, -89f, 89f);

		Quaternion xQuaternion = Quaternion.AngleAxis(rotationX, Vector3.up);
		Quaternion yQuaternion = Quaternion.AngleAxis(rotationY, -Vector3.right);

		myTransform.rotation = originalRotation * xQuaternion * yQuaternion;
		#endregion

		#region Movement
		//make speed frame-independant
		currentSpeed *= Time.deltaTime;

		verticalMovement = verticalMovement * currentSpeed;

		//Read from Unity input and apply speed
		float currentForwardSpeed = Input.GetAxis ("Vertical") * currentSpeed;
		float currentSidewardSpeed = Input.GetAxis("Horizontal") * currentSpeed;

		Vector3 fwd = myTransform.forward;
		Vector3 left = myTransform.right;
		myTransform.position = myTransform.position + ((fwd * currentForwardSpeed) + (left * currentSidewardSpeed) + Vector3.up * verticalMovement);
		#endregion
	}
	
	/// <summary>
	/// Enables the freelook camera, disabling or configuring any other component that might be present and interfere with the freelook camera
	/// </summary>
	private void EnableNoClip() {
		//Integration with the Unity's standard assets' character motor. If you are not using this, you can delete the following 4 lines
		Behaviour motor = gameObject.GetComponent("CharacterMotor") as MonoBehaviour;
		if (motor != null) {
			motor.enabled = false;
		}

		//enables isKinematic on the rigidbody, if present. (this saves the previous state, to not interfere with your own game logic)
		Rigidbody body = gameObject.GetComponent<Rigidbody>();
		if (body != null) {
			wasUsingKinematic = body.isKinematic;
			if (!body.isKinematic) {
				body.isKinematic = true;
			}
		}
		
		IsEnabled = true;
	}
	
	/// <summary>
	/// Disables the freelook camera, enabling or configuring any other component that might be present and interfere with the freelook camera
	/// </summary>
	private void DisableNoClip() {
		//Integration with the Unity's standard assets' character motor. If you are not using this, you can delete the following 4 lines
		Behaviour motor = gameObject.GetComponent("CharacterMotor") as MonoBehaviour;
		if(motor != null) {
			motor.enabled = true;
		}

		//disables isKinematic on the rigidbody if it was before, if present.
		Rigidbody body = gameObject.GetComponent<Rigidbody>();
		if(body != null) {
			if (!body.isKinematic) return;		//the body isKinematic was set to false by something for some reason, no need to force the saved value

			body.isKinematic = wasUsingKinematic;
		}
		
		IsEnabled = false;
	}
}