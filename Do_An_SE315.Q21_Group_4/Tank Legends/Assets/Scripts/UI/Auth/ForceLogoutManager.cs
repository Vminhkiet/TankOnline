using UnityEngine;

public class ForceLogoutManager : MonoBehaviour
{
    public void HandleForceLogout(int code, string message, int countdownSec = 10)
    {
        if (AuthSessionRuntime.Instance == null)
        {
            Debug.LogWarning("[ForceLogoutManager] AuthSessionRuntime.Instance is null. Please place AuthSessionRuntime in bootstrap scene.");
            return;
        }

        AuthSessionRuntime.Instance.HandleForceLogout(code, message, countdownSec);
    }

    public void TriggerDuplicateLoginKickTest()
    {
        if (AuthSessionRuntime.Instance == null)
        {
            Debug.LogWarning("[ForceLogoutManager] AuthSessionRuntime.Instance is null. Please place AuthSessionRuntime in bootstrap scene.");
            return;
        }

        AuthSessionRuntime.Instance.TriggerDuplicateLoginKickTest();
    }
}
