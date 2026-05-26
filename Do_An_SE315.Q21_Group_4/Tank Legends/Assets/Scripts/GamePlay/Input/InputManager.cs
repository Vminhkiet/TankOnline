using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Complete
{
    /// <summary>
    /// Gán joystick và nút bắn một lần trên scene; xe tăng chỉ gọi GetTankMoveInput / GetTankFireInput.
    /// </summary>
    [DefaultExecutionOrder (-100)]
    public class InputManager : MonoBehaviour
    {
        private enum InputMode
        {
            Mobile,
            PC
        }

        public static InputManager Instance { get; private set; }

        [SerializeField] private InputMode m_InputMode = InputMode.Mobile;
        [SerializeField] private GameObject m_MobileControlsRoot;
        [SerializeField] private VariableJoystick m_MoveJoystick;
        [SerializeField] private bool m_InvertJoystickVertical;
        [SerializeField] private bool m_JoystickRelativeToCamera = true;
        [SerializeField] private VariableJoystick m_FireJoystick;
        [SerializeField] private float m_JoystickDeadZone = 0.1f;

        private bool m_UIFireHeld;
        private bool m_UIFireHeldPrev;
        private bool m_FireDown;
        private bool m_FireUp;
        private bool m_FireHeld;
        private bool m_FireUiHooked;

        public bool IsMobileMode => m_InputMode == InputMode.Mobile;


        private void Awake ()
        {
            if (Instance != null && Instance != this)
            {
                Destroy (gameObject);
                return;
            }

            Instance = this;
            ApplyInputMode ();
        }


        private void OnDestroy ()
        {
            if (Instance == this)
                Instance = null;
        }


        private void OnValidate ()
        {
            ApplyInputMode ();
        }


        private void ApplyInputMode ()
        {
            bool showMobileControls = m_InputMode == InputMode.Mobile;

            if (m_MobileControlsRoot != null)
            {
                m_MobileControlsRoot.SetActive (showMobileControls);
                return;
            }

            if (m_MoveJoystick != null)
                m_MoveJoystick.gameObject.SetActive (showMobileControls);


            if (m_FireJoystick != null)
                m_FireJoystick.gameObject.SetActive (showMobileControls);
        }

        private void Update ()
        {
            if (m_InputMode == InputMode.PC)
            {
                m_UIFireHeld = false;
                m_UIFireHeldPrev = false;
                m_FireHeld = false;
                m_FireDown = false;
                m_FireUp = false;
                return;
            }

            if (m_FireJoystick != null)
            {
                float dragMagnitude = new Vector2 (m_FireJoystick.Horizontal, m_FireJoystick.Vertical).magnitude;
                m_UIFireHeld = dragMagnitude > m_JoystickDeadZone;
            }

            m_FireHeld = m_UIFireHeld;
            m_FireDown = m_FireHeld && !m_UIFireHeldPrev;
            m_FireUp = !m_FireHeld && m_UIFireHeldPrev;
            m_UIFireHeldPrev = m_UIFireHeld;
        }

        public bool TryGetMobileFireDirection (out Vector3 direction, out float magnitude)
        {
            direction = Vector3.zero;
            magnitude = 0f;

            if (m_InputMode != InputMode.Mobile || m_FireJoystick == null)
                return false;

            Vector2 joystickInput = new Vector2 (m_FireJoystick.Horizontal, m_FireJoystick.Vertical);
            magnitude = Mathf.Clamp01 (joystickInput.magnitude);
            if (magnitude <= m_JoystickDeadZone)
            {
                magnitude = 0f;
                return false;
            }

            Vector3 right = Vector3.right;
            Vector3 forward = Vector3.forward;

            if (m_JoystickRelativeToCamera && Camera.main != null)
            {
                right = Camera.main.transform.right;
                right.y = 0f;
                right.Normalize ();

                forward = Camera.main.transform.forward;
                forward.y = 0f;
                forward.Normalize ();
            }

            direction = (right * joystickInput.x + forward * joystickInput.y).normalized;
            return direction.sqrMagnitude > 0.001f;
        }


        /// <summary> Tiến/lùi và xoay (-1..1). Có joystick UI thì dùng joystick; không thì Vertical/Horizontal + player. </summary>
        public void GetTankMoveInput (int playerNumber, out float forward, out float turn)
        {
            forward = Input.GetAxis ("Vertical" + playerNumber);
            turn = Input.GetAxis ("Horizontal" + playerNumber);

            if (m_InputMode == InputMode.Mobile && m_MoveJoystick != null)
            {
                float joystickForward = m_MoveJoystick.Vertical;
                if (m_InvertJoystickVertical)
                    joystickForward = -joystickForward;

                float joystickTurn = m_MoveJoystick.Horizontal;
                if (Mathf.Abs (joystickForward) > m_JoystickDeadZone || Mathf.Abs (joystickTurn) > m_JoystickDeadZone)
                {
                    forward = joystickForward;
                    turn = joystickTurn;
                }
            }
        }


        public bool TryGetMobileMoveDirection (out Vector3 direction, out float magnitude)
        {
            direction = Vector3.zero;
            magnitude = 0f;

            if (m_InputMode != InputMode.Mobile || m_MoveJoystick == null)
                return false;

            float joystickForward = m_MoveJoystick.Vertical;
            if (m_InvertJoystickVertical)
                joystickForward = -joystickForward;

            Vector2 joystickInput = new Vector2 (m_MoveJoystick.Horizontal, joystickForward);
            magnitude = Mathf.Clamp01 (joystickInput.magnitude);
            if (magnitude <= m_JoystickDeadZone)
            {
                magnitude = 0f;
                return false;
            }

            Vector3 right = Vector3.right;
            Vector3 forward = Vector3.forward;

            if (m_JoystickRelativeToCamera && Camera.main != null)
            {
                right = Camera.main.transform.right;
                right.y = 0f;
                right.Normalize ();

                forward = Camera.main.transform.forward;
                forward.y = 0f;
                forward.Normalize ();
            }

            direction = (right * joystickInput.x + forward * joystickInput.y).normalized;
            return direction.sqrMagnitude > 0.001f;
        }


        /// <summary> Giữ/thả nạp đạn. Có nút UI thì dùng nút; không thì Fire + player. </summary>
        public void GetTankFireInput (int playerNumber, out bool fireDown, out bool fireHeld, out bool fireUp)
        {
            string axis = "Fire" + playerNumber;
            bool inputFireDown = Input.GetButtonDown (axis);
            bool inputFireHeld = Input.GetButton (axis);
            bool inputFireUp = Input.GetButtonUp (axis);

            if (m_InputMode == InputMode.PC)
            {
                fireDown = inputFireDown;
                fireHeld = inputFireHeld;
                fireUp = inputFireUp;
                return;
            }

            fireHeld = inputFireHeld || m_FireHeld;
            fireDown = inputFireDown || m_FireDown;
            fireUp = (inputFireUp || m_FireUp) && !fireHeld;
        }
    }
}
