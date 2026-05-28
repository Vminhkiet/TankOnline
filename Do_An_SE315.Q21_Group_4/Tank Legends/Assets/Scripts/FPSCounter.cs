using UnityEngine;

public class FPSCounter : MonoBehaviour
{
    private float deltaTime = 0.0f;
    private float displayMsec = 0.0f;
    private float displayFps = 0.0f;
    private float updateTimer = 0.0f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Initialize()
    {
        // Uncap FPS according to user request
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 30;

        // Auto-instantiate the FPS Counter so the user doesn't have to place it in every scene
        GameObject go = new GameObject("FPS_Counter_Debug");
        DontDestroyOnLoad(go);
        go.AddComponent<FPSCounter>();
    }

    void Update()
    {
        // Smooth out delta time
        deltaTime += (Time.unscaledDeltaTime - deltaTime) * 0.1f;

        updateTimer += Time.unscaledDeltaTime;
        if (updateTimer >= 0.5f)
        {
            displayMsec = deltaTime * 1000.0f;
            displayFps = 1.0f / deltaTime;
            updateTimer = 0.0f;
        }
    }

    void OnGUI()
    {
        int w = Screen.width, h = Screen.height;

        GUIStyle style = new GUIStyle();

        Rect rect = new Rect(20, 20, w, h * 2 / 100);
        style.alignment = TextAnchor.UpperLeft;
        style.fontSize = 30; // Fixed font size for better visibility
        style.normal.textColor = Color.green;

        // Change color based on FPS
        if (displayFps < 30)
            style.normal.textColor = Color.red;
        else if (displayFps < 60)
            style.normal.textColor = Color.yellow;
        else
            style.normal.textColor = Color.green;

        string text = string.Format("{0:0.0} ms ({1:0.} FPS)", displayMsec, displayFps);

        // Draw shadow for readability
        style.normal.textColor = Color.black;
        Rect shadowRect = new Rect(rect.x + 2, rect.y + 2, rect.width, rect.height);
        GUI.Label(shadowRect, text, style);

        // Draw actual text
        style.normal.textColor = displayFps < 30 ? Color.red : (displayFps < 60 ? Color.yellow : Color.green);
        GUI.Label(rect, text, style);
    }
}
