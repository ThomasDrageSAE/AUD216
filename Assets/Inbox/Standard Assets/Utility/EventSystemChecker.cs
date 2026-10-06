using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

public class EventSystemChecker : MonoBehaviour
{

	void Awake ()
	{
	    if(!FindFirstObjectByType<EventSystem>())
        {
            GameObject obj = new GameObject("EventSystem");
            obj.AddComponent<EventSystem>();
           // obj.AddComponent<StandaloneInputModule>().forceModuleActive = true;
        }
	}
}
