using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenuUI : MonoBehaviour
{
    public void StartClassic()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene("ArenaClassic");
    }

    public void StartRL()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene("ArenaRL");
    }

    public void QuitGame()
    {
        Application.Quit();
    }
}