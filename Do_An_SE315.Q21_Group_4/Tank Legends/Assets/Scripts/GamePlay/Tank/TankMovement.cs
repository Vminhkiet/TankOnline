using UnityEngine;
using TankNet;

namespace Complete
{
    public class TankMovement : MonoBehaviour
    {
        [Header("Tank Definition Link")]
        [Tooltip("Optional TankDefinition ScriptableObject to dynamically override movement speed.")]
        public TankDefinitionSO m_Definition;

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

        private TankAnimation m_TankAnimation;      // Reference to the external TankAnimation component.
        private TankShooting m_TankShooting;        // Cached — GetComponent mỗi FixedUpdate = thủ phạm 35ms

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
        [HideInInspector] public bool m_IsInputFrozen = false;

        private Vector3 m_CachedColliderCenter = new Vector3(0, 0.85f, 0);
        private Vector3 m_CachedColliderExtents = new Vector3(0.75f, 0.85f, 0.9f);

        private static readonly RaycastHit[] s_HitBuffer = new RaycastHit[8];

        // Physics throttle: trên thiết bị yếu, cast mỗi 2 FixedUpdate thay vì mỗi frame
        private int m_PhysicsTickCounter = 0;
        private Vector3 m_CachedWallSlideMove = Vector3.zero;
        private float  m_CachedTerrainY = 0f;
        private static bool s_LowEndPhysics = false;

        public static void SetLowEndPhysicsMode(bool enabled) { s_LowEndPhysics = enabled; }

        [HideInInspector] public bool m_HasNetworkCorrection = false;
        [HideInInspector] public Vector3 m_NetworkTargetPosition;
        [HideInInspector] public Quaternion m_NetworkTargetRotation;
        [HideInInspector] public bool m_HasNetworkTargetY = false;
        [HideInInspector] public float m_NetworkTargetY;

        private void Awake()
        {
            m_Rigidbody = GetComponent<Rigidbody>();
            m_Collider = GetComponent<Collider>();
            m_TankAnimation = GetComponent<TankAnimation>();
            m_TankShooting = GetComponent<TankShooting>();
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
            if (m_Definition != null)
            {
                m_Speed = m_Definition.RealStats.MovementSpeed;
            }

            bool online = TankNetClient.Instance != null;
            
            bool useKinematic = online && m_UseCustomOnlinePhysics;
            m_Rigidbody.isKinematic = useKinematic;
            m_Rigidbody.useGravity = !useKinematic;
            m_Rigidbody.interpolation = RigidbodyInterpolation.Interpolate;

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


        private float m_MovementFreezeTimer = 0f;

        public void FreezeMovement(float duration)
        {
            m_MovementFreezeTimer = duration;
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
            if (m_MovementFreezeTimer > 0f)
            {
                m_MovementFreezeTimer -= Time.deltaTime;
            }

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

            // Apply freeze override if timer is active or input is explicitly frozen
            if (m_MovementFreezeTimer > 0f || m_IsInputFrozen)
            {
                m_MovementInputValue = 0f;
                m_TurnInputValue = 0f;
                m_UseMobileDirectionalMove = false;
                m_MobileMoveDirection = Vector3.zero;
            }

            if (m_TankAnimation != null)
            {
                bool isMoving = Mathf.Abs(m_MovementInputValue) > 0.1f || Mathf.Abs(m_TurnInputValue) > 0.1f;
                m_TankAnimation.SetMoving(isMoving);
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
                if (m_MovementFreezeTimer > 0f)
                {
                    mx = 0;
                    mz = 0;
                }
                else if (m_UseMobileDirectionalMove)
                {
                    GetMobileDiscreteMove(out mx, out mz);
                }
                else
                {
                    // Use the smoothed values from Update()
                    mx = DiscreteFromAxis(m_TurnInputValue);
                    mz = DiscreteFromAxis(m_MovementInputValue);
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

            if (m_MovementFreezeTimer > 0f)
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


        private void ApplyOnlineKinematicStep(ref int mx, ref int mz)
        {
            Vector3 pos = m_Rigidbody.position;

            // 1. Sync Y from server (terrain height — client doesn't simulate this)
            if (m_HasNetworkTargetY)
            {
                pos.y = m_NetworkTargetY;
                m_HasNetworkTargetY = false;
            }

            // 2. Soft lerp rotation towards server if not actively turning
            if (m_HasNetworkCorrection && mx == 0)
            {
                float angleErr = Quaternion.Angle(m_Rigidbody.rotation, m_NetworkTargetRotation);
                if (angleErr > 1f)
                {
                    m_Rigidbody.MoveRotation(Quaternion.RotateTowards(
                        m_Rigidbody.rotation, m_NetworkTargetRotation, 15f * Time.fixedDeltaTime));
                }
            }

            // 3. Apply player turn input
            if (mx != 0)
            {
                m_Rigidbody.MoveRotation(m_Rigidbody.rotation *
                    Quaternion.Euler(0f, mx * m_TurnSpeed * Time.fixedDeltaTime, 0f));
            }

            // 4. Apply player movement
            if (mz != 0)
            {
                Vector3 move = transform.forward * mz * m_Speed * Time.fixedDeltaTime;

                // Throttle: thiết bị yếu chỉ cast mỗi 2 tick, tick xen kẽ dùng kết quả cache
                bool doFullCast = !s_LowEndPhysics || (m_PhysicsTickCounter % 2 == 0);
                m_PhysicsTickCounter++;

                if (doFullCast)
                {
                    m_CachedWallSlideMove = ResolveWallSlide(move);
                    if (m_CachedWallSlideMove.sqrMagnitude > 0.0001f)
                        m_CachedTerrainY = SampleTerrainHeight(pos + m_CachedWallSlideMove);
                }

                // Scale cache theo direction hiện tại (tốc độ thay đổi nhưng hướng giữ nguyên)
                Vector3 finalMove = m_CachedWallSlideMove.sqrMagnitude > 0.0001f
                    ? m_CachedWallSlideMove.normalized * move.magnitude
                    : Vector3.zero;

                if (finalMove.sqrMagnitude > 0.0001f)
                {
                    pos += finalMove;
                    float sampledY = doFullCast ? m_CachedTerrainY : m_CachedTerrainY;
                    if (sampledY > pos.y && sampledY - pos.y <= 0.85f)
                        pos.y = sampledY;
                    else if (sampledY < pos.y)
                        pos.y = sampledY;
                }
                else
                    mz = 0;
            }

            // 5. Soft lerp XZ position towards server if drifted too far
            if (m_HasNetworkCorrection)
            {
                Vector2 xzErr = new Vector2(pos.x - m_NetworkTargetPosition.x, pos.z - m_NetworkTargetPosition.z);
                if (xzErr.magnitude > 1.5f) // SOFT_THRESHOLD
                {
                  //  Debug.Log($"[TankMovement] Network Correction Triggered! Client YAW: {m_Rigidbody.rotation.eulerAngles.y:F1} | Server YAW: {m_NetworkTargetRotation.eulerAngles.y:F1} | Pos Diff: {xzErr.magnitude:F2}");
                    Vector3 target = new Vector3(m_NetworkTargetPosition.x, pos.y, m_NetworkTargetPosition.z);
                    pos = Vector3.MoveTowards(pos, target, 5.0f * Time.fixedDeltaTime);
                }
            }

            // 6. Single MovePosition call — keeps Rigidbody Interpolation intact
            m_Rigidbody.MovePosition(pos);
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

            if (!m_Rigidbody.isKinematic)
            {
                m_Rigidbody.angularVelocity = Vector3.zero;
            }
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
            {
                float turretYaw = 0f;
                bool reload = false;
                if (m_TankShooting != null) {
                    turretYaw = m_TankShooting.GetCurrentTurretYaw();
                    reload = m_TankShooting.ConsumeReloadIntent();
                }
                net.SetMove(moveX, moveZ, turretYaw, m_Rigidbody.rotation.eulerAngles.y, reload);
            }
        }

        private void SendOnlineMoveFromMobile()
        {
            var net = TankNetClient.Instance;
            if (net == null || !net.IsConnected)
                return;

            float turretYaw = 0f;
            bool reload = false;
            if (m_TankShooting != null) {
                turretYaw = m_TankShooting.GetCurrentTurretYaw();
                reload = m_TankShooting.ConsumeReloadIntent();
            }

            GetMobileDiscreteMove(out int mx, out int mz);
            net.SendMoveNow(mx, mz, turretYaw, m_Rigidbody.rotation.eulerAngles.y, reload);
        }
    }
}
