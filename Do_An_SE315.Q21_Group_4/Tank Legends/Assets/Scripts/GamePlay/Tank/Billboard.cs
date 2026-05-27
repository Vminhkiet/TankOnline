using UnityEngine;

namespace Complete
{
    /// <summary>
    /// Attach to a World Space Canvas (or any transform) to make it always face the camera.
    /// Uses LateUpdate so it runs after the parent tank has finished rotating.
    /// </summary>
    public class Billboard : MonoBehaviour
    {
        private Transform m_CameraTransform;

        private void Start()
        {
            // Cache the main camera transform
            if (Camera.main != null)
            {
                m_CameraTransform = Camera.main.transform;
            }
        }

        private void LateUpdate()
        {
            // Fallback in case camera wasn't found at start (e.g. offline mode waits for spawn)
            if (m_CameraTransform == null)
            {
                if (Camera.main != null)
                    m_CameraTransform = Camera.main.transform;
                else
                    return;
            }

            // Match the camera's rotation so the canvas is always parallel to the screen.
            // This avoids perspective distortion that LookAt() would cause at screen edges.
            transform.rotation = m_CameraTransform.rotation;
        }
    }
}
