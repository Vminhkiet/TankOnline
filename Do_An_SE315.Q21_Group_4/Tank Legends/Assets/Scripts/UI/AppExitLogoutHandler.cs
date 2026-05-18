using UnityEngine;

public class AppExitLogoutHandler : MonoBehaviour
{
    private void OnApplicationQuit()
    {
        if (AuthSessionRuntime.Instance == null)
        {
            Debug.LogWarning("[AppExitLogoutHandler] AuthSessionRuntime.Instance is null.");
            return;
        }

        // Delegated to singleton runtime
        AuthenticationUIManager.LogoutSilently(AuthSessionRuntime.Instance, GameApiClient.BuildUrl(AuthSessionRuntime.Instance.logoutApiPath));
        AuthenticationUIManager.ClearLocalAuth();
    }
}
