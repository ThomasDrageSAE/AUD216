using UnityEngine;
using UnityEngine.SceneManagement;

public class MenuSceneLoader : MonoBehaviour
{
    public void MoveToMainMenu()
    {
        SceneManager.LoadScene("MainMenu"); // Load the main menu scene
    }
}
