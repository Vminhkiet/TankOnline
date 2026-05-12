using UnityEngine;

public class PreviewOrbitCamera : MonoBehaviour
{
    private const float LegacyMinPitch = -15f;
    private const float LegacyMaxPitch = 45f;
    private const float UpdatedMinPitch = -20f;
    private const float UpdatedMaxPitch = 80f;

    [Header("References")]
    [SerializeField] private TankSelectionManager selectionManager;
    [SerializeField] private Transform focusTarget;
    [SerializeField] private RectTransform interactionArea;
    [SerializeField] private Camera interactionAreaCamera;

    [Header("Orbit")]
    [SerializeField] private float horizontalRotationSpeed = 0.2f;
    [SerializeField] private float verticalRotationSpeed = 0.15f;
    [SerializeField] private float minPitch = UpdatedMinPitch;
    [SerializeField] private float maxPitch = UpdatedMaxPitch;

    private float yaw;
    private float pitch;
    private float distance;

    private float defaultYaw;
    private float defaultPitch;
    private float defaultDistance;

    private bool hasDefaultOrbit;
    private bool isMouseDragging;
    private int activeTouchId = -1;

    private Vector2 lastPointerPosition;

    private void Awake()
    {
        ResolveFocusTarget();
        UpgradeLegacyPitchLimits();
        CacheDefaultOrbit();
        ResetToDefaultOrbit();
    }

    private void OnEnable()
    {
        if (selectionManager != null)
            selectionManager.SelectionChanged += HandleSelectionChanged;
    }

    private void OnDisable()
    {
        if (selectionManager != null)
            selectionManager.SelectionChanged -= HandleSelectionChanged;
    }

    private void LateUpdate()
    {
        ResolveFocusTarget();

        if (focusTarget == null)
            return;

        if (!hasDefaultOrbit)
        {
            CacheDefaultOrbit();

            if (!hasDefaultOrbit)
                return;
        }

        if (Input.touchCount > 0)
        {
            HandleTouchInput();
        }
        else
        {
            activeTouchId = -1;
            HandleMouseInput();
        }

        ApplyOrbit();
    }

    public void ResetToDefaultOrbit()
    {
        if (!hasDefaultOrbit)
            CacheDefaultOrbit();

        if (!hasDefaultOrbit)
            return;

        yaw = defaultYaw;
        pitch = defaultPitch;
        distance = defaultDistance;
        ApplyOrbit();
    }

    private void HandleSelectionChanged(TankDefinitionSO _)
    {
        ResetDragState();
        ResetToDefaultOrbit();
    }

    private void ResolveFocusTarget()
    {
        if (focusTarget == null && selectionManager != null)
            focusTarget = selectionManager.PreviewAnchor;
    }

    private void CacheDefaultOrbit()
    {
        if (focusTarget == null)
            return;

        Vector3 offset = transform.position - focusTarget.position;
        float offsetMagnitude = offset.magnitude;

        if (offsetMagnitude <= Mathf.Epsilon)
            return;

        defaultDistance = offsetMagnitude;
        defaultYaw = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
        defaultPitch = Mathf.Asin(Mathf.Clamp(offset.y / offsetMagnitude, -1f, 1f)) * Mathf.Rad2Deg;
        hasDefaultOrbit = true;
    }

    private void HandleMouseInput()
    {
        Vector2 mousePosition = Input.mousePosition;

        if (Input.GetMouseButtonDown(0) && IsInsideInteractionArea(mousePosition))
        {
            isMouseDragging = true;
            lastPointerPosition = mousePosition;
        }

        if (Input.GetMouseButtonUp(0))
        {
            isMouseDragging = false;
            return;
        }

        if (!isMouseDragging || !Input.GetMouseButton(0))
            return;

        Vector2 delta = mousePosition - lastPointerPosition;
        lastPointerPosition = mousePosition;
        Rotate(delta);
    }

    private void HandleTouchInput()
    {
        if (activeTouchId == -1)
        {
            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch touch = Input.GetTouch(i);

                if (touch.phase != TouchPhase.Began || !IsInsideInteractionArea(touch.position))
                    continue;

                activeTouchId = touch.fingerId;
                lastPointerPosition = touch.position;
                break;
            }
        }

        if (activeTouchId == -1)
            return;

        for (int i = 0; i < Input.touchCount; i++)
        {
            Touch touch = Input.GetTouch(i);

            if (touch.fingerId != activeTouchId)
                continue;

            if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
            {
                activeTouchId = -1;
                return;
            }

            Vector2 delta = touch.position - lastPointerPosition;
            lastPointerPosition = touch.position;

            if (touch.phase == TouchPhase.Moved)
                Rotate(delta);

            return;
        }

        activeTouchId = -1;
    }

    private void Rotate(Vector2 delta)
    {
        yaw += delta.x * horizontalRotationSpeed;
        pitch = Mathf.Clamp(pitch + delta.y * verticalRotationSpeed, minPitch, maxPitch);
    }

    private void ApplyOrbit()
    {
        if (focusTarget == null)
            return;

        Vector3 offset = Quaternion.Euler(-pitch, yaw, 0f) * (Vector3.forward * distance);
        transform.position = focusTarget.position + offset;
        transform.rotation = Quaternion.LookRotation(focusTarget.position - transform.position, Vector3.up);
    }

    private bool IsInsideInteractionArea(Vector2 screenPosition)
    {
        if (interactionArea == null)
            return true;

        return RectTransformUtility.RectangleContainsScreenPoint(interactionArea, screenPosition, interactionAreaCamera);
    }

    private void ResetDragState()
    {
        isMouseDragging = false;
        activeTouchId = -1;
    }

    private void UpgradeLegacyPitchLimits()
    {
        if (!Mathf.Approximately(minPitch, LegacyMinPitch) || !Mathf.Approximately(maxPitch, LegacyMaxPitch))
            return;

        minPitch = UpdatedMinPitch;
        maxPitch = UpdatedMaxPitch;
    }
}
