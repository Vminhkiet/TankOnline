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

        private string m_MovementAxisName;          // The name of the input axis for moving forward and back.
        private string m_TurnAxisName;              // The name of the input axis for turning.
        private Rigidbody m_Rigidbody;              // Reference used to move the tank.
        private float m_MovementInputValue;         // The current value of the movement input.
        private float m_TurnInputValue;             // The current value of the turn input.
        private float m_OriginalPitch;              // The pitch of the audio source at the start of the scene.
        private ParticleSystem[] m_particleSystems; // References to all the particles systems used by the Tanks
        private Collider m_Collider;

        private static readonly RaycastHit[] s_HitBuffer = new RaycastHit[8];

        private void Awake ()
        {
            m_Rigidbody = GetComponent<Rigidbody> ();
            m_Collider  = GetComponent<Collider>();
        }


        private void OnEnable ()
        {
            bool online = TankNet.TankNetClient.Instance != null;
            // Kinematic online: movement is fully position-based via BoxCast + MovePosition,
            // so PhysX velocity/bounce never interferes. Wall blocking is done manually.
            m_Rigidbody.isKinematic = online;
            if (online) m_Rigidbody.useGravity = false;

            // Also reset the input values.
            m_MovementInputValue = 0f;
            m_TurnInputValue = 0f;

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
            // Store the value of both input axes.
            m_MovementInputValue = Input.GetAxis (m_MovementAxisName);
            m_TurnInputValue = Input.GetAxis (m_TurnAxisName);

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
            if (TankNet.TankNetClient.Instance != null)
            {
                // GetAxisRaw: snaps to 0 immediately on key release (no Input Manager smoothing).
                // m_MovementInputValue/m_TurnInputValue (GetAxis) are kept for EngineAudio only.
                int mx = (int)Input.GetAxisRaw(m_TurnAxisName);
                int mz = (int)Input.GetAxisRaw(m_MovementAxisName);

                if (mx != 0)
                    m_Rigidbody.MoveRotation(m_Rigidbody.rotation *
                        Quaternion.Euler(0f, mx * m_TurnSpeed * Time.fixedDeltaTime, 0f));

                if (mz != 0)
                {
                    Vector3 move = transform.forward * mz * m_Speed * Time.fixedDeltaTime;
                    move = ResolveWallSlide(move);
                    if (move.sqrMagnitude > 0.0001f)
                    {
                        Vector3 newPos = m_Rigidbody.position + move;
                        float sampledY = SampleTerrainHeight(newPos);
                        if (sampledY > newPos.y) newPos.y = sampledY;
                        m_Rigidbody.MovePosition(newPos);
                    }
                    else
                        mz = 0; // fully blocked by real wall — server should not move either
                }

                TankNet.TankNetClient.Instance.SendMoveNow(mx, mz);
            }
            else
            {
                Move ();
                Turn ();
            }
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


        // If 'move' is unobstructed, return it unchanged.
        // If blocked by a wall, project onto the wall surface (slide) and re-check.
        // If the slide direction is also blocked (corner), return zero.
        private Vector3 ResolveWallSlide(Vector3 move)
        {
            Vector3 wallNormal;
            if (!IsBlocked(move, out wallNormal))
                return move;

            // Remove the component going into the wall, keep the parallel component.
            Vector3 slide = Vector3.ProjectOnPlane(move, wallNormal);
            if (slide.sqrMagnitude < 0.0001f)
                return Vector3.zero;

            // Re-check the slide direction (corner case: two walls meeting).
            if (IsBlocked(slide, out _))
                return Vector3.zero;

            return slide;
        }

        // Casts the tank's bounding box in 'move' direction.
        // Returns true if a static obstacle is hit; outputs the wall normal.
        // Dynamic rigidbodies (remote tanks) are ignored — server handles those.
        // Upward-facing surfaces (terrain/slopes, normal.y > 0.4) are not treated as walls.
        private bool IsBlocked(Vector3 move, out Vector3 wallNormal)
        {
            wallNormal = Vector3.zero;
            if (m_Collider == null) return false;

            Bounds b    = m_Collider.bounds;
            Vector3 dir = move.normalized;
            float dist  = move.magnitude;

            int count = Physics.BoxCastNonAlloc(
                b.center, b.extents * 0.95f, dir,
                s_HitBuffer, transform.rotation, dist + 0.05f);

            for (int i = 0; i < count; i++)
            {
                if (s_HitBuffer[i].collider.transform.IsChildOf(transform)) continue;
                Rigidbody hr = s_HitBuffer[i].rigidbody;
                if (hr != null && !hr.isKinematic) continue;
                if (s_HitBuffer[i].normal.y > 0.4f) continue; // slope/terrain
                if (HasSurfaceTag(s_HitBuffer[i].collider.transform)) continue; // walkable surface
                wallNormal = s_HitBuffer[i].normal;
                return true;
            }
            return false;
        }

        // Returns true if the transform or any of its ancestors has the "Surface" tag.
        // Bridge deck colliders may have the tag on a parent rather than directly on themselves.
        private static bool HasSurfaceTag(Transform t)
        {
            while (t != null)
            {
                if (t.CompareTag("Surface")) return true;
                t = t.parent;
            }
            return false;
        }

        // Raycast xuống để lấy chiều cao địa hình tại vị trí newPos.
        private float SampleTerrainHeight(Vector3 newPos)
        {
            Vector3 origin = new Vector3(newPos.x, newPos.y + 5f, newPos.z);
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 20f))
                if (!hit.collider.transform.IsChildOf(transform))
                    return hit.point.y;
            return newPos.y;
        }
    }
}