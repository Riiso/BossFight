using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

public class PauseMenuController : MonoBehaviour
{
    public GameObject pausePanel;
    public TMP_Text stateText;

    public GameObject resumeButton;
    public GameObject restartButton;
    public GameObject mainMenuButton;

    private bool isPaused = false;
    private bool gameFinished = false;

    void Start()
    {
        if (pausePanel != null)
            pausePanel.SetActive(false);

        Time.timeScale = 1f;
        LockCursor();
    }

    void Update()
    {
        if (gameFinished) return;

        if (Input.GetKeyDown(KeyCode.P))
        {
            if (isPaused) ResumeGame();
            else PauseGame();
        }
    }

    public void PauseGame()
    {
        isPaused = true;

        if (pausePanel != null)
            pausePanel.SetActive(true);

        if (stateText != null)
            stateText.text = "PAUSED";

        if (resumeButton != null) resumeButton.SetActive(true);
        if (restartButton != null) restartButton.SetActive(true);
        if (mainMenuButton != null) mainMenuButton.SetActive(true);

        Time.timeScale = 0f;
        UnlockCursor();
    }

    public void ResumeGame()
    {
        if (gameFinished) return;

        isPaused = false;

        if (pausePanel != null)
            pausePanel.SetActive(false);

        Time.timeScale = 1f;
        LockCursor();
    }

    public void ShowLoseMenu()
    {
        gameFinished = true;
        isPaused = true;

        if (pausePanel != null)
            pausePanel.SetActive(true);

        if (stateText != null)
            stateText.text = "YOU DIED";

        if (resumeButton != null) resumeButton.SetActive(false);
        if (restartButton != null) restartButton.SetActive(true);
        if (mainMenuButton != null) mainMenuButton.SetActive(true);

        Time.timeScale = 0f;
        UnlockCursor();
    }

    public void ShowWinMenu()
    {
        gameFinished = true;
        isPaused = true;

        if (pausePanel != null)
            pausePanel.SetActive(true);

        if (stateText != null)
            stateText.text = "YOU WIN";

        if (resumeButton != null) resumeButton.SetActive(false);
        if (restartButton != null) restartButton.SetActive(true);
        if (mainMenuButton != null) mainMenuButton.SetActive(true);

        Time.timeScale = 0f;
        UnlockCursor();
    }

    public void RestartScene()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void GoToMainMenu()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene("MainMenu");
    }

    void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void UnlockCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}