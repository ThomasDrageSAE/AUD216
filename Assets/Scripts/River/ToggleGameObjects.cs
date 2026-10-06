using System.Collections.Generic;
using UnityEngine;

public class ToggleGameObjects : MonoBehaviour
{
    // Public field to assign the key in the inspector
    public KeyCode toggleKey = KeyCode.Alpha0; // Default key is '0'

    // List to hold references to the GameObjects to be toggled
    public List<GameObject> gameObjectsToToggle;

    private void Awake()
    {
        // Dev tool - disable outside the editor and development builds.
        if (!Application.isEditor && !Debug.isDebugBuild)
        {
            enabled = false;
        }
    }

    private void Update()
    {
        // Check if the assigned key is pressed
        if (Input.GetKeyDown(toggleKey))
        {
            // Toggle the active state of each GameObject in the list
            foreach (GameObject obj in gameObjectsToToggle)
            {
                if (obj != null) // Ensure the GameObject is not null
                {
                    obj.SetActive(!obj.activeSelf);
                }
            }
        }
    }
}
