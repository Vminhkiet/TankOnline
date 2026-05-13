using UnityEngine;

namespace Complete
{
    public class TankMovement : MonoBehaviour
    {
        public int m_PlayerNumber = 1;              // Used to identify which tank belongs to which player.  This is set by this tank's manager.
        public float m_Speed = 12f;                 // How fast the tank moves forward and back.
        public float m_TurnSpeed = 180f;            // How fast the tank turns in degrees per second.
        public AudioSource m_MovementAudio;         // Reference to the audio source used to play engine sounds. NB: different to the shooting audio source.
        public AudioClip m_EngineIdling;            // Audio to play when the tank isn't moving.
        public AudioClip m_EngineDriving;           // Audio to play when the tank is moving.
		public float m_PitchRange = 0.2f;           // The amount by which the pitch of the engine noises can vary.
        [Header ("Auto Righting")]
        public bool m_AutoRightWhenFlipped = true;
        public float m_MaxTiltBeforeRighting = 70f;
        public float m_TimeBeforeRighting = 1f;
        public float m_RightingSpeed = 180f;

        private string m_MovementAxisName;          // The name of the input axis for moving forward and back.
        private string m_TurnAxisName;              // The name of the input axis for turning.
        private Rigidbody m_Rigidbody;              // Reference used to move the tank.
        private float m_MovementInputValue;         // The current value of the movement input.
        private float m_TurnInputValue;             // The current value of the turn input.
        private bool m_UseMobileDirectionalMove;
        private Vector3 m_MobileMoveDirection;
        private float m_FlippedTimer;
        private float m_OriginalPitch;              // The pitch of the audio source at the start of the scene.
        private ParticleSystem[] m_particleSystems; // References to all the particles systems used by the Tanks

        private void Awake ()
        {
            m_Rigidbody = GetComponent<Rigidbody> ();
        }


        private void OnEnable ()
        {
            // When the tank is turned on, make sure it's not kinematic.
            m_Rigidbody.isKinematic = false;

            // Also reset the input values.
            m_MovementInputValue = 0f;
            m_TurnInputValue = 0f;
            m_UseMobileDirectionalMove = false;
            m_MobileMoveDirection = Vector3.zero;
            m_FlippedTimer = 0f;

            // We grab all the Particle systems child of that Tank to be able to Stop/Play them on Deactivate/Activate
            // It is needed because we move the Tank when spawning it, and if the Particle System is playing while we do that
            // it "think" it move from (0,0,0) to the spawn point, creating a huge trail of smoke
            m_particleSystems = GetComponentsInChildren<ParticleSystem>();
            for (int i = 0; i < m_particleSystems.Length; ++i)
            {
                m_particleSystems[i].Play();
            }
        }


        private void OnDisable ()
        {
            // When the tank is turned off, set it to kinematic so it stops moving.
            m_Rigidbody.isKinematic = true;

            // Stop all particle system so it "reset" it's position to the actual one instead of thinking we moved when spawning
            for(int i = 0; i < m_particleSystems.Length; ++i)
            {
                m_particleSystems[i].Stop();
            }
        }


        private void Start ()
        {
            // The axes names are based on player number.
            m_MovementAxisName = "Vertical" + m_PlayerNumber;
            m_TurnAxisName = "Horizontal" + m_PlayerNumber;

            // Store the original pitch of the audio source.
            m_OriginalPitch = m_MovementAudio.pitch;
        }


        private void Update ()
        {
            if (InputManager.Instance != null)
            {
                InputManager.Instance.GetTankMoveInput (m_PlayerNumber, out m_MovementInputValue, out m_TurnInputValue);

                float mobileMoveAmount;
                m_UseMobileDirectionalMove = InputManager.Instance.TryGetMobileMoveDirection (out m_MobileMoveDirection, out mobileMoveAmount);
                if (m_UseMobileDirectionalMove)
                {
                    m_MovementInputValue = mobileMoveAmount;
                    m_TurnInputValue = 0f;
                }
            }
            else
            {
                m_UseMobileDirectionalMove = false;
                m_MovementInputValue = Input.GetAxis (m_MovementAxisName);
                m_TurnInputValue = Input.GetAxis (m_TurnAxisName);
            }

            EngineAudio ();
        }


        private void EngineAudio ()
        {
            // If there is no input (the tank is stationary)...
            if (Mathf.Abs (m_MovementInputValue) < 0.1f && Mathf.Abs (m_TurnInputValue) < 0.1f)
            {
                // ... and if the audio source is currently playing the driving clip...
                if (m_MovementAudio.clip == m_EngineDriving)
                {
                    // ... change the clip to idling and play it.
                    m_MovementAudio.clip = m_EngineIdling;
                    m_MovementAudio.pitch = Random.Range (m_OriginalPitch - m_PitchRange, m_OriginalPitch + m_PitchRange);
                    m_MovementAudio.Play ();
                }
            }
            else
            {
                // Otherwise if the tank is moving and if the idling clip is currently playing...
                if (m_MovementAudio.clip == m_EngineIdling)
                {
                    // ... change the clip to driving and play.
                    m_MovementAudio.clip = m_EngineDriving;
                    m_MovementAudio.pitch = Random.Range(m_OriginalPitch - m_PitchRange, m_OriginalPitch + m_PitchRange);
                    m_MovementAudio.Play();
                }
            }
        }


        private void FixedUpdate ()
        {
            if (AutoRightTank ())
                return;

            if (m_UseMobileDirectionalMove)
            {
                MoveAndTurnInMobileDirection ();
                return;
            }

            // Adjust the rigidbodies position and orientation in FixedUpdate.
            Move ();
            Turn ();
        }


        private bool AutoRightTank ()
        {
            if (!m_AutoRightWhenFlipped)
                return false;

            float tilt = Vector3.Angle (transform.up, Vector3.up);
            if (tilt < m_MaxTiltBeforeRighting)
            {
                m_FlippedTimer = 0f;
                return false;
            }

            m_FlippedTimer += Time.fixedDeltaTime;
            if (m_FlippedTimer < m_TimeBeforeRighting)
                return false;

            Vector3 forward = Vector3.ProjectOnPlane (transform.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.ProjectOnPlane (transform.up, Vector3.up);

            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.forward;

            Quaternion targetRotation = Quaternion.LookRotation (forward.normalized, Vector3.up);
            Quaternion newRotation = Quaternion.RotateTowards (m_Rigidbody.rotation, targetRotation, m_RightingSpeed * Time.fixedDeltaTime);

            m_Rigidbody.angularVelocity = Vector3.zero;
            m_Rigidbody.MoveRotation (newRotation);
            return true;
        }


        private void MoveAndTurnInMobileDirection ()
        {
            Quaternion targetRotation = Quaternion.LookRotation (m_MobileMoveDirection, Vector3.up);
            Quaternion newRotation = Quaternion.RotateTowards (m_Rigidbody.rotation, targetRotation, m_TurnSpeed * Time.fixedDeltaTime);
            m_Rigidbody.MoveRotation (newRotation);

            Vector3 movement = (newRotation * Vector3.forward) * m_MovementInputValue * m_Speed * Time.fixedDeltaTime;
            m_Rigidbody.MovePosition (m_Rigidbody.position + movement);
        }


        private void Move ()
        {
            // Create a vector in the direction the tank is facing with a magnitude based on the input, speed and the time between frames.
            Vector3 movement = transform.forward * m_MovementInputValue * m_Speed * Time.fixedDeltaTime;

            // Apply this movement to the rigidbody's position.
            m_Rigidbody.MovePosition(m_Rigidbody.position + movement);
        }


        private void Turn ()
        {
            // Determine the number of degrees to be turned based on the input, speed and time between frames.
            float turn = m_TurnInputValue * m_TurnSpeed * Time.deltaTime;

            // Make this into a rotation in the y axis.
            Quaternion turnRotation = Quaternion.Euler (0f, turn, 0f);

            // Apply this rotation to the rigidbody's rotation.
            m_Rigidbody.MoveRotation (m_Rigidbody.rotation * turnRotation);
        }
    }
}
