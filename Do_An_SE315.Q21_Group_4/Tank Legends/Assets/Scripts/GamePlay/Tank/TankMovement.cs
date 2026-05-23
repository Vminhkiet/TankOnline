using UnityEngine;
using TankNet;

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
        [Header("Auto Righting")]
        public bool m_AutoRightWhenFlipped = true;
        public float m_MaxTiltBeforeRighting = 70f;
        public float m_TimeBeforeRighting = 1f;
        public float m_RightingSpeed = 180f;
        
        [Header("Physics Sync")]
        [Tooltip("If true, uses custom kinematic wall slide. If false, uses Unity's default dynamic rigidbody colliders.")]
        public bool m_UseCustomOnlinePhysics = true;

        private string m_MovementAxisName;          // The name of the input axis for moving forward and back.
        private string m_TurnAxisName;              // The name of the input axis for turning.
        private Rigidbody m_Rigidbody;              // Reference used to move the tank.
        private Collider m_Collider;
        private float m_MovementInputValue;         // The current value of the movement input.
        private float m_TurnInputValue;             // The current value of the turn input.
        private bool m_UseMobileDirectionalMove;
        private Vector3 m_MobileMoveDirection;
        private float m_FlippedTimer;
        private float m_OriginalPitch;              // The pitch of the audio source at the start of the scene.
        private ParticleSystem[] m_particleSystems; // References to all the particles systems used by the Tanks

        private Vector3 m_CachedColliderCenter = new Vector3(0, 0.85f, 0);
        private Vector3 m_CachedColliderExtents = new Vector3(0.75f, 0.85f, 0.9f);

        private static readonly RaycastHit[] s_HitBuffer = new RaycastHit[8];

        private void Awake()
        {
            m_Rigidbody = GetComponent<Rigidbody>();
            m_Collider = GetComponent<Collider>();
            if (m_Collider is BoxCollider box)
            {
                m_CachedColliderCenter = box.center;
                m_CachedColliderExtents = box.size * 0.5f;
                // Áp dụng scale nếu có
                m_CachedColliderExtents.Scale(transform.lossyScale);
            }
        }


        private void OnEnable()
        {
            bool online = TankNetClient.Instance != null;
            
            bool useKinematic = online && m_UseCustomOnlinePhysics;
            m_Rigidbody.isKinematic = useKinematic;
            m_Rigidbody.useGravity = !useKinematic;

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


        private void OnDisable()
        {
            // When the tank is turned off, set it to kinematic so it stops moving.
            m_Rigidbody.isKinematic = true;

            // Stop all particle system so it "reset" it's position to the actual one instead of thinking we moved when spawning
            for (int i = 0; i < m_particleSystems.Length; ++i)
            {
                m_particleSystems[i].Stop();
            }
        }


        private void Start()
        {
            // The axes names are based on player number.
            m_MovementAxisName = "Vertical" + m_PlayerNumber;
            m_TurnAxisName = "Horizontal" + m_PlayerNumber;

            // Store the original pitch of the audio source.
            m_OriginalPitch = m_MovementAudio.pitch;
        }


        private void Update()
        {
            if (InputManager.Instance != null)
            {
                InputManager.Instance.GetTankMoveInput(m_PlayerNumber, out m_MovementInputValue, out m_TurnInputValue);

                float mobileMoveAmount;
                m_UseMobileDirectionalMove = InputManager.Instance.TryGetMobileMoveDirection(out m_MobileMoveDirection, out mobileMoveAmount);
                if (m_UseMobileDirectionalMove)
                {
                    m_MovementInputValue = mobileMoveAmount;
                    m_TurnInputValue = 0f;
                }
            }
            else
            {
                m_UseMobileDirectionalMove = false;
                m_MovementInputValue = Input.GetAxis(m_MovementAxisName);
                m_TurnInputValue = Input.GetAxis(m_TurnAxisName);
            }

            EngineAudio();
        }


        private void EngineAudio()
        {
            // If there is no input (the tank is stationary)...
            if (Mathf.Abs(m_MovementInputValue) < 0.1f && Mathf.Abs(m_TurnInputValue) < 0.1f)
            {
                // ... and if the audio source is currently playing the driving clip...
                if (m_MovementAudio.clip == m_EngineDriving)
                {
                    // ... change the clip to idling and play it.
                    m_MovementAudio.clip = m_EngineIdling;
                    m_MovementAudio.pitch = Random.Range(m_OriginalPitch - m_PitchRange, m_OriginalPitch + m_PitchRange);
                    m_MovementAudio.Play();
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


        private void FixedUpdate()
        {
            bool online = TankNetClient.Instance != null;

            if (online && m_UseCustomOnlinePhysics)
            {
                if (AutoRightTank())
                {
                    SendOnlineMoveDiscrete(0, 0);
                    return;
                }

                int mx, mz;
                if (m_UseMobileDirectionalMove)
                    GetMobileDiscreteMove(out mx, out mz);
                else
                {
                    // GetAxisRaw: snaps to 0 on key release (no Input Manager smoothing).
                    mx = (int)Input.GetAxisRaw(m_TurnAxisName);
                    mz = (int)Input.GetAxisRaw(m_MovementAxisName);
                }

                ApplyOnlineKinematicStep(ref mx, ref mz);
                SendOnlineMoveDiscrete(mx, mz);
                return;
            }

            // --- offline: mobile / keyboard + PhysX movement ---
            if (AutoRightTank())
            {
                SendOnlineMoveDiscrete(0, 0);
                return;
            }

            if (m_UseMobileDirectionalMove)
            {
                MoveAndTurnInMobileDirection();
                SendOnlineMoveFromMobile();
                return;
            }

            Move();
            Turn();
            SendOnlineMoveDiscrete(DiscreteFromAxis(m_TurnInputValue), DiscreteFromAxis(m_MovementInputValue));
        }


        /// <summary>Online kinematic step: discrete turn + forward with wall slide + terrain height (was TankMovementOnline).</summary>
        private void ApplyOnlineKinematicStep(ref int mx, ref int mz)
        {
            if (mx != 0)
            {
                m_Rigidbody.MoveRotation(m_Rigidbody.rotation *
                    Quaternion.Euler(0f, mx * m_TurnSpeed * Time.fixedDeltaTime, 0f));
            }

            if (mz != 0)
            {
                Vector3 move = transform.forward * mz * m_Speed * Time.fixedDeltaTime;
                move = ResolveWallSlide(move);
                if (move.sqrMagnitude > 0.0001f)
                {
                    Vector3 newPos = m_Rigidbody.position + move;
                    float sampledY = SampleTerrainHeight(newPos);
                    // Match server STEP_HEIGHT = 0.3f
                    if (sampledY > newPos.y && sampledY - newPos.y <= 0.85f)
                        newPos.y = sampledY;
                    else if (sampledY < newPos.y)
                        newPos.y = sampledY; // Allow falling down
                    m_Rigidbody.MovePosition(newPos);
                }
                else
                    mz = 0; // fully blocked — server should not move either
            }
        }

        private void GetMobileDiscreteMove(out int mx, out int mz)
        {
            mz = m_MovementInputValue > 0.1f ? 1 : 0;
            mx = 0;
            if (m_MobileMoveDirection.sqrMagnitude > 0.01f)
            {
                float angle = Vector3.SignedAngle(transform.forward, m_MobileMoveDirection, Vector3.up);
                if (angle > 12f)
                    mx = 1;
                else if (angle < -12f)
                    mx = -1;
            }
        }

        // If 'move' is unobstructed, return it unchanged.
        // If blocked by a wall, project onto the wall surface (slide) and re-check.
        // If the slide direction is also blocked (corner), return zero.
        private Vector3 ResolveWallSlide(Vector3 move)
        {
            Vector3 wallNormal;
            if (!IsBlocked(move, out wallNormal))
                return move;

            Vector3 slide = Vector3.ProjectOnPlane(move, wallNormal);
            if (slide.sqrMagnitude < 0.0001f)
                return Vector3.zero;

            if (IsBlocked(slide, out _))
                return Vector3.zero;

            return slide;
        }

        // Casts the tank's bounding box in 'move' direction.
        // Dynamic rigidbodies (remote tanks) are ignored — server handles those.
        // Upward-facing surfaces (terrain/slopes, normal.y > 0.4) are not treated as walls.
        private bool IsBlocked(Vector3 move, out Vector3 wallNormal)
        {
            wallNormal = Vector3.zero;

            // Do not rely on m_Collider, as GameManager may disable or destroy it 
            // when UseCustomOnlinePhysics is enabled. Use the cached bounds.
            Vector3 center = transform.TransformPoint(m_CachedColliderCenter);
            Vector3 extents = m_CachedColliderExtents;

            // Nâng đáy của BoxCast lên một chút để mô phỏng STEP_HEIGHT = 0.85f của server.
            // Điều này giúp BoxCast không bị vướng vào các con dốc nhỏ hoặc mặt đất.
            float stepHeight = 0.85f;
            center.y += stepHeight / 2f;
            extents.y -= stepHeight / 2f;

            Vector3 dir = move.normalized;
            float dist = move.magnitude;

            int count = Physics.BoxCastNonAlloc(
                center, extents * 0.95f, dir,
                s_HitBuffer, transform.rotation, dist + 0.05f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                if (s_HitBuffer[i].collider.transform.IsChildOf(transform))
                    continue;
                Rigidbody hr = s_HitBuffer[i].rigidbody;
                if (hr != null && !hr.isKinematic)
                    continue;

                // Unity's BoxCast can return normal = Vector3.up for initial overlaps.
                // If distance is 0, we are already intersecting a collider. Treat it as a wall block.
                if (s_HitBuffer[i].distance == 0)
                {
                    wallNormal = -dir;
                    return true;
                }

                if (s_HitBuffer[i].normal.y > 0.4f)
                    continue;
                if (HasSurfaceTag(s_HitBuffer[i].collider.transform))
                    continue;
                wallNormal = s_HitBuffer[i].normal;
                return true;
            }

            return false;
        }

        private static bool HasSurfaceTag(Transform t)
        {
            while (t != null)
            {
                if (t.CompareTag("Surface"))
                    return true;
                t = t.parent;
            }

            return false;
        }

        private float SampleTerrainHeight(Vector3 newPos)
        {
            Vector3 origin = new Vector3(newPos.x, newPos.y + 5f, newPos.z);
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 20f))
            {
                if (!hit.collider.transform.IsChildOf(transform))
                    return hit.point.y;
            }

            return newPos.y;
        }


        private bool AutoRightTank()
        {
            if (!m_AutoRightWhenFlipped)
                return false;

            float tilt = Vector3.Angle(transform.up, Vector3.up);
            if (tilt < m_MaxTiltBeforeRighting)
            {
                m_FlippedTimer = 0f;
                return false;
            }

            m_FlippedTimer += Time.fixedDeltaTime;
            if (m_FlippedTimer < m_TimeBeforeRighting)
                return false;

            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.ProjectOnPlane(transform.up, Vector3.up);

            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.forward;

            Quaternion targetRotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
            Quaternion newRotation = Quaternion.RotateTowards(m_Rigidbody.rotation, targetRotation, m_RightingSpeed * Time.fixedDeltaTime);

            m_Rigidbody.angularVelocity = Vector3.zero;
            m_Rigidbody.MoveRotation(newRotation);
            return true;
        }


        private void MoveAndTurnInMobileDirection()
        {
            Quaternion targetRotation = Quaternion.LookRotation(m_MobileMoveDirection, Vector3.up);
            Quaternion newRotation = Quaternion.RotateTowards(m_Rigidbody.rotation, targetRotation, m_TurnSpeed * Time.fixedDeltaTime);
            m_Rigidbody.MoveRotation(newRotation);

            Vector3 movement = (newRotation * Vector3.forward) * m_MovementInputValue * m_Speed * Time.fixedDeltaTime;
            m_Rigidbody.MovePosition(m_Rigidbody.position + movement);
        }


        private void Move()
        {
            Vector3 movement = transform.forward * m_MovementInputValue * m_Speed * Time.fixedDeltaTime;
            m_Rigidbody.MovePosition(m_Rigidbody.position + movement);
        }


        private void Turn()
        {
            float turn = m_TurnInputValue * m_TurnSpeed * Time.fixedDeltaTime;
            Quaternion turnRotation = Quaternion.Euler(0f, turn, 0f);
            m_Rigidbody.MoveRotation(m_Rigidbody.rotation * turnRotation);
        }

        /// <summary>Server chỉ nhận hướng rời rạc {-1,0,1}; gửi mỗi FixedUpdate khi có TankNet.</summary>
        private static int DiscreteFromAxis(float v)
        {
            if (v > 0.1f) return 1;
            if (v < -0.1f) return -1;
            return 0;
        }

        private void SendOnlineMoveDiscrete(int moveX, int moveZ)
        {
            var net = TankNetClient.Instance;
            if (net != null && net.IsConnected)
                net.SendMoveNow(moveX, moveZ);
        }

        private void SendOnlineMoveFromMobile()
        {
            var net = TankNetClient.Instance;
            if (net == null || !net.IsConnected)
                return;

            GetMobileDiscreteMove(out int mx, out int mz);
            net.SendMoveNow(mx, mz);
        }
    }
}
