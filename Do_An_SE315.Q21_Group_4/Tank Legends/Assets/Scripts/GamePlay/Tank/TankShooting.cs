using UnityEngine;
using UnityEngine.UI;
using TankNet;

namespace Complete
{
    /// <summary>
    /// Bắn đạn: charge + UI slider; offline / mobile qua InputManager hoặc axis Unity;
    /// khi có <see cref="TankNetClient"/> và đã kết nối thì gửi <see cref="TankNetClient.RequestShoot"/> (client prediction).
    /// </summary>
    public class TankShooting : MonoBehaviour
    {
        [Header("Tank Definition Link")]
        [Tooltip("Optional TankDefinition ScriptableObject to dynamically override fire rate and bullet damage.")]
        public TankDefinitionSO m_Definition;

        public int m_PlayerNumber = 1;              // Used to identify the different players.
        public Rigidbody m_Shell;                   // Prefab of the shell.
        public Transform m_FireTransform;           // A child of the tank where the shells are spawned (legacy fallback).
        [Tooltip("Multiple barrel muzzle spawn points. If empty, falls back to m_FireTransform.")]
        public Transform[] m_FireTransforms;        // Multiple barrel muzzles.
        public Slider m_AimSlider;                  // A child of the tank that displays the current launch force.
        public AudioSource m_ShootingAudio;         // Reference to the audio source used to play the shooting audio. NB: different to the movement audio source.
        public AudioClip m_ChargingClip;            // Audio that plays when each shot is charging up.
        public GameObject[] m_ChargingEffects;      // Visual effects that turn on while charging
        public AudioClip m_FireClip;                // Audio that plays when each shot is fired.
        public float m_MinLaunchForce = 15f;        // The force given to the shell if the fire button is not held.
        public float m_MaxLaunchForce = 30f;        // The force given to the shell if the fire button is held for the max charge time.
        public float m_MaxChargeTime = 0.75f;       // How long the shell can charge for before it is fired at max force.

        [Header("Turret and Shooting Direction")]
        public Transform m_TankHead;                // The turret / head object of the tank.

        [Header("Movement Freeze Option")]
        public bool m_CanMoveWhileShooting = true;  // If false, the tank will freeze after shooting.
        public float m_MovementFreezeDuration = 0.5f; // The duration of the freeze in seconds.

        [Header("Fire Rate / Cooldown")]
        [Tooltip("Number of shots per second")]
        public float m_FireRate = 1.5f;             // Number of shots per second.

        [Header("UI Display")]
        public bool m_ShowAimSlider = false;        // Toggle to show/hide the launch force aim slider.

        [Header("Reloading & Audio")]
        public AudioClip m_ReloadClip;              // Audio clip played when reloading begins

        [Header("Shooting Style")]
        public bool m_HoldToCharge = false;         // If true, holding the fire button/joystick charges up the shot. If false, it fires instantly at max rate.

        public bool m_IsLocalPlayer = true;         // Flag to distinguish between local and remote tanks

        private TankAnimation m_TankAnimation;      // Reference to the external TankAnimation component.
        private Vector3 m_RemoteTargetDir;          // Target direction for remote turret
        private float m_RemoteAimTimer = 0f;        // Timer for remote aiming
        private Quaternion m_RemoteTurretTarget;    // Smooth target rotation for remote turret
        private bool m_HasRemoteTurretTarget = false;

        private string m_FireButton;                // The input axis that is used for launching shells.
        private float m_CurrentLaunchForce;         // The force that will be given to the shell when the fire button is released.
        private float m_ChargeSpeed;                // How fast the launch force increases, based on the max charge time.
        private bool m_Fired;                       // Whether or not the shell has been launched with this button press.
        private TankMovement m_Movement;
        private float m_FireCooldownTimer = 0f;     // Cooldown tracking timer.
        private Quaternion m_TurretToMuzzleOffset;  // Cache the rotation offset between turret bone and muzzle.
        private float m_HitscanVisualTimer = 0f;    // Timer to turn off hitscan visuals for remote tanks.

        private int m_CurrentAmmo = 1;
        private float m_ReloadTimer = 0f;
        private bool m_IsReloading = false;
        private bool m_WantsReloadIntent = false;

        private bool m_Started = false;
        private bool m_IsChargingEffectActive = false;
        private float m_ChargeDelayTimer = 0f;

        private void StartBlinking()
        {
            if (m_IsLocalPlayer && GameUIManager.Instance != null)
            {
                GameUIManager.Instance.SetOutOfAmmoBlinking(true);
            }
        }
        
        private void StopBlinking()
        {
            if (m_IsLocalPlayer && GameUIManager.Instance != null)
            {
                GameUIManager.Instance.SetOutOfAmmoBlinking(false);
            }
        }

        public bool ConsumeReloadIntent()
        {
            if (m_WantsReloadIntent)
            {
                m_WantsReloadIntent = false;
                return true;
            }
            return false;
        }

        public void TriggerReload()
        {
            if (m_Definition == null) return;
            if (m_IsReloading || m_CurrentAmmo >= m_Definition.RealStats.MagazineCapacity) return;
            
            m_IsReloading = true;
            m_ReloadTimer = m_Definition.RealStats.ReloadTime;
            m_WantsReloadIntent = true;
            
            if (m_Definition != null && m_Definition.WeaponType == WeaponType.Hitscan)
            {
                SetHitscanVisualsActive(false);
            }
            
            if (m_ShootingAudio != null && m_ReloadClip != null)
            {
                m_ShootingAudio.clip = m_ReloadClip;
                m_ShootingAudio.loop = false;
                m_ShootingAudio.Play();
            }
        }

        private void SetupUI()
        {
            if (GameUIManager.Instance != null && m_IsLocalPlayer)
            {
                if (GameUIManager.Instance.m_ReloadButton != null)
                {
                    GameUIManager.Instance.m_ReloadButton.onClick.RemoveListener(OnReloadButtonClicked);
                    GameUIManager.Instance.m_ReloadButton.onClick.AddListener(OnReloadButtonClicked);
                }
                UpdateAmmoUI();
            }
        }

        private void UpdateAmmoUI()
        {
            if (m_Definition != null && GameUIManager.Instance != null && m_IsLocalPlayer)
            {
                GameUIManager.Instance.UpdateAmmoUI(m_CurrentAmmo, m_Definition.RealStats.MagazineCapacity);
            }
        }

        private void SetHitscanVisualsActive(bool active)
        {
            if (m_FireTransforms == null) return;
            bool stateChanged = false;
            for (int i = 0; i < m_FireTransforms.Length; i++)
            {
                if (m_FireTransforms[i] != null && m_FireTransforms[i].gameObject.activeSelf != active)
                {
                    m_FireTransforms[i].gameObject.SetActive(active);
                    stateChanged = true;
                }
            }

            if (stateChanged && m_ShootingAudio != null && m_FireClip != null)
            {
                if (active)
                {
                    m_ShootingAudio.clip = m_FireClip;
                    m_ShootingAudio.loop = true;
                    if (!m_ShootingAudio.isPlaying) m_ShootingAudio.Play();
                }
                else
                {
                    m_ShootingAudio.loop = false;
                    m_ShootingAudio.Stop();
                }
            }
        }

        private void SetChargingEffectsActive(bool active)
        {
            if (m_ChargingEffects == null) return;
            for (int i = 0; i < m_ChargingEffects.Length; i++)
            {
                if (m_ChargingEffects[i] != null)
                {
                    if (m_ChargingEffects[i].activeSelf != active)
                    {
                        m_ChargingEffects[i].SetActive(active);
                    }
                    
                    if (active)
                    {
                        ParticleSystem[] psList = m_ChargingEffects[i].GetComponentsInChildren<ParticleSystem>();
                        foreach (var ps in psList)
                        {
                            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                            ps.Play(true);
                        }
                    }
                }
            }
        }

        private void StartChargingEffect()
        {
            if (m_IsChargingEffectActive) return;
            m_IsChargingEffectActive = true;

            if (m_ShootingAudio != null && m_ChargingClip != null)
            {
                m_ShootingAudio.clip = m_ChargingClip;
                m_ShootingAudio.loop = false;
                m_ShootingAudio.Play();
            }
            SetChargingEffectsActive(true);
        }

        private void StopChargingEffect()
        {
            if (!m_IsChargingEffectActive) return;
            m_IsChargingEffectActive = false;

            SetChargingEffectsActive(false);
            if (m_ShootingAudio != null && m_ShootingAudio.clip == m_ChargingClip)
            {
                m_ShootingAudio.Stop();
            }
        }

        private void Awake()
        {
            m_Movement = GetComponent<TankMovement>();
            m_TankAnimation = GetComponent<TankAnimation>();

            // Fallback to m_FireTransform if the array is empty
            if (m_FireTransforms == null || m_FireTransforms.Length == 0)
            {
                if (m_FireTransform != null)
                {
                    m_FireTransforms = new Transform[] { m_FireTransform };
                }
            }

            if (m_TankHead != null && m_FireTransforms != null && m_FireTransforms.Length > 0)
            {
                // Capture the exact rotation offset from the turret bone to the first muzzle in default state
                m_TurretToMuzzleOffset = Quaternion.Inverse(m_TankHead.rotation) * m_FireTransforms[0].rotation;
            }
        }


        private void OnEnable()
        {
            if (m_Definition != null)
            {
                m_FireRate = m_Definition.RealStats.FireRate;
            }

            // When the tank is turned on, reset the launch force and the UI
            m_CurrentLaunchForce = m_MinLaunchForce;
            if (m_AimSlider != null)
            {
                m_AimSlider.value = m_MinLaunchForce;
                m_AimSlider.gameObject.SetActive(m_ShowAimSlider && m_IsLocalPlayer);
            }
            m_FireCooldownTimer = 0f;
            
            if (m_Definition != null)
            {
                m_CurrentAmmo = m_Definition.RealStats.MagazineCapacity;
            }
            m_IsReloading = false;
            m_WantsReloadIntent = false;
            if (m_IsLocalPlayer && GameUIManager.Instance != null)
            {
                GameUIManager.Instance.SetOutOfAmmoBlinking(false);
            }
            StopChargingEffect();
            m_ChargeDelayTimer = 0f;
            
            if (m_Started)
            {
                SetupUI();
            }
        }

        private void OnDisable()
        {
            if (GameUIManager.Instance != null && m_IsLocalPlayer && GameUIManager.Instance.m_ReloadButton != null)
            {
                GameUIManager.Instance.m_ReloadButton.onClick.RemoveListener(OnReloadButtonClicked);
            }
        }

        private void OnReloadButtonClicked()
        {
            if (m_IsLocalPlayer)
            {
                TriggerReload();
            }
        }


        private void Start ()
        {
            m_Started = true;
            SetupUI();

            // The fire axis is based on the player number.
            m_FireButton = "Fire" + m_PlayerNumber;

            // The rate that the launch force charges up is the range of possible forces by the max charge time.
            m_ChargeSpeed = (m_MaxLaunchForce - m_MinLaunchForce) / m_MaxChargeTime;

            if (m_AimSlider != null)
            {
                m_AimSlider.gameObject.SetActive(m_ShowAimSlider && m_IsLocalPlayer);
            }

            bool isHitscan = m_Definition != null && m_Definition.WeaponType == WeaponType.Hitscan;
            if (isHitscan)
            {
                SetHitscanVisualsActive(false);
            }
        }


        private void Update ()
        {
            if (!m_IsLocalPlayer)
            {
                // Smooth remote turret rotation
                if (m_HasRemoteTurretTarget && m_TankHead != null)
                {
                    m_TankHead.rotation = Quaternion.RotateTowards(
                        m_TankHead.rotation, m_RemoteTurretTarget, 720f * Time.deltaTime);
                }

                if (m_HitscanVisualTimer > 0f)
                {
                    m_HitscanVisualTimer -= Time.deltaTime;
                    if (m_HitscanVisualTimer <= 0f)
                    {
                        SetHitscanVisualsActive(false);
                    }
                }
                return;
            }

            if (m_IsReloading)
            {
                // Allow movement during reload even for tanks that can't move while shooting
                if (!m_CanMoveWhileShooting && m_Movement != null)
                {
                    m_Movement.m_IsInputFrozen = false;
                }

                m_ReloadTimer -= Time.deltaTime;
                if (GameUIManager.Instance != null && m_Definition != null && m_Definition.RealStats.ReloadTime > 0f && m_IsLocalPlayer)
                {
                    GameUIManager.Instance.UpdateReloadProgress(1f - (m_ReloadTimer / m_Definition.RealStats.ReloadTime));
                }
                
                if (m_ReloadTimer <= 0f)
                {
                    m_IsReloading = false;
                    if (m_Definition != null) m_CurrentAmmo = m_Definition.RealStats.MagazineCapacity;
                    UpdateAmmoUI();
                    StopBlinking();
                }
                return; // cannot shoot while reloading
            }
            
            if (Input.GetKeyDown(KeyCode.R) || (InputManager.Instance != null && InputManager.Instance.IsMobileMode == false && Input.GetKeyDown(KeyCode.R)))
            {
                TriggerReload();
                return;
            }

            bool fireDown;
            bool fireHeld;
            bool fireUp;

            if (InputManager.Instance != null)
                InputManager.Instance.GetTankFireInput (m_PlayerNumber, out fireDown, out fireHeld, out fireUp);
            else
            {
                fireDown = Input.GetButtonDown (m_FireButton);
                fireHeld = Input.GetButton (m_FireButton);
                fireUp = Input.GetButtonUp (m_FireButton);
            }

            bool isTryingToFire = fireHeld || fireDown;
            if (m_TankAnimation != null)
            {
                m_TankAnimation.SetShooting(isTryingToFire);
            }

            bool isHitscan = m_Definition != null && m_Definition.WeaponType == WeaponType.Hitscan;
            if (isHitscan)
            {
                SetHitscanVisualsActive(isTryingToFire);
            }

            if (!m_CanMoveWhileShooting && m_Movement != null)
            {
                m_Movement.m_IsInputFrozen = isTryingToFire;
            }

            // Head rotation based on shooting direction
            Vector3 shootDir = Vector3.zero;
            bool hasTargetDir = false;

            if (InputManager.Instance != null && InputManager.Instance.IsMobileMode)
            {
                hasTargetDir = InputManager.Instance.TryGetMobileFireDirection(out shootDir, out _);
            }
            else
            {
                // On PC, we can aim using the mouse cursor when aiming (holding fire or just hovering)
                if (Camera.main != null)
                {
                    Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
                    if (Physics.Raycast(ray, out RaycastHit hit, 100f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    {
                        shootDir = hit.point - transform.position;
                        shootDir.y = 0f;
                        shootDir.Normalize();
                        hasTargetDir = true;
                    }
                }
            }

            if (m_TankHead != null && m_FireTransform != null)
            {
                Vector3 targetDir = transform.forward;
                if (hasTargetDir)
                {
                    targetDir = shootDir;
                }

                // The target rotation for the muzzle (pointing at target, standing up along tank's deck)
                Quaternion targetMuzzleRot = Quaternion.LookRotation(targetDir, transform.up);

                // Back-calculate the required rotation for the turret bone using the cached offset
                Quaternion targetTurretRot = targetMuzzleRot * Quaternion.Inverse(m_TurretToMuzzleOffset);

                // Smoothly rotate the turret bone in world space
                m_TankHead.rotation = Quaternion.RotateTowards(m_TankHead.rotation, targetTurretRot, 500f * Time.deltaTime);
            }

            if (m_HoldToCharge)
            {
                // --- Hold to Charge Logic ---
                // The slider displays current charge force
                if (m_AimSlider != null)
                {
                    m_AimSlider.value = m_CurrentLaunchForce;
                }

                // If the max force has been exceeded and the shell hasn't yet been launched...
                if (m_CurrentLaunchForce >= m_MaxLaunchForce && !m_Fired)
                {
                    m_CurrentLaunchForce = m_MaxLaunchForce;
                    Fire ();
                }
                // Otherwise, if the fire button has just started being pressed...
                else if (fireDown)
                {
                    m_Fired = false;
                    m_CurrentLaunchForce = m_MinLaunchForce;
                    StartChargingEffect();
                }
                // Otherwise, if the fire button is being held and the shell hasn't been launched yet...
                else if (fireHeld && !m_Fired)
                {
                    m_CurrentLaunchForce += m_ChargeSpeed * Time.deltaTime;
                    if (m_AimSlider != null)
                    {
                        m_AimSlider.value = m_CurrentLaunchForce;
                    }
                }
                // Otherwise, if the fire button is released and the shell hasn't been launched yet...
                else if (fireUp && !m_Fired)
                {
                    Fire ();
                }
            }
            else
            {
                // --- Instant Auto Fire Logic ---
                if (m_AimSlider != null)
                {
                    m_AimSlider.value = m_MaxLaunchForce;
                }

                // Update cooldown timers
                if (m_FireCooldownTimer > 0f)
                {
                    m_FireCooldownTimer -= Time.deltaTime;
                }
                if (m_ChargeDelayTimer > 0f)
                {
                    m_ChargeDelayTimer -= Time.deltaTime;
                }

                // Shoot continuously if the fire button/joystick is held
                isTryingToFire = fireHeld || fireDown;
                if (isTryingToFire)
                {
                    if (m_FireCooldownTimer <= 0f)
                    {
                        m_CurrentLaunchForce = m_MaxLaunchForce;
                        Fire();
                        m_FireCooldownTimer = 1f / m_FireRate;

                        // Set delay before the charging effect for the next shot starts
                        // Delay is 30% of the cooldown time, up to max 0.5s
                        m_ChargeDelayTimer = Mathf.Min((1f / m_FireRate) * 0.3f, 0.5f);
                    }
                    else
                    {
                        // Holding the button during cooldown -> wait for delay to expire before charging effect
                        if (m_ChargeDelayTimer <= 0f)
                        {
                            StartChargingEffect();
                        }
                    }
                }
                else
                {
                    StopChargingEffect();
                    // Optional: we can clear the delay timer if they let go, so tapping immediately resumes charge
                    m_ChargeDelayTimer = 0f;
                }
            }
        }


        private void Fire ()
        {
            // Set the fired flag so only Fire is only called once.
            m_Fired = true;
            StopChargingEffect();

            if (!m_CanMoveWhileShooting && m_Movement != null)
            {
                m_Movement.FreezeMovement(m_MovementFreezeDuration);
            }

            var net = TankNetClient.Instance;
            if (net != null && net.IsConnected)
            {
                float turretYaw = transform.eulerAngles.y * Mathf.Deg2Rad;
                if (m_FireTransforms != null && m_FireTransforms.Length > 0 && m_FireTransforms[0] != null)
                {
                    turretYaw = m_FireTransforms[0].eulerAngles.y * Mathf.Deg2Rad;
                }
                else if (m_TankHead != null)
                {
                    turretYaw = m_TankHead.eulerAngles.y * Mathf.Deg2Rad;
                }
                
                byte barrelCount = (byte)(m_FireTransforms != null && m_FireTransforms.Length > 0 ? m_FireTransforms.Length : 1);
                net.RequestShoot(m_CurrentLaunchForce, turretYaw, barrelCount);
            }

            // Spawn shells from all muzzles (only if NOT hitscan)
            bool isHitscan = m_Definition != null && m_Definition.WeaponType == WeaponType.Hitscan;
            if (!isHitscan && m_FireTransforms != null && m_FireTransforms.Length > 0)
            {
                for (int i = 0; i < m_FireTransforms.Length; i++)
                {
                    Transform muzzle = m_FireTransforms[i];
                    if (muzzle == null) continue;

                    Rigidbody shellInstance =
                        Instantiate (m_Shell, muzzle.position, muzzle.rotation) as Rigidbody;

                    // Pass owner and scale damage by charge ratio if hold-to-charge is active
                    ShellExplosion shell = shellInstance.GetComponent<ShellExplosion>();
                    if (shell != null)
                    {
                        shell.m_Owner = gameObject;
                        if (m_Definition != null)
                        {
                            shell.m_MaxDamage = m_Definition.RealStats.Damage;
                        }
                        if (m_HoldToCharge)
                        {
                            float chargeRatio = m_CurrentLaunchForce / m_MaxLaunchForce;
                            shell.m_MaxDamage *= chargeRatio;
                        }
                    }

                    Vector3 fireDir = muzzle.forward;
                    fireDir.y = 0f;
                    fireDir.Normalize();
                    shellInstance.velocity = m_CurrentLaunchForce * fireDir;
                }
            }

            // Change the clip to the firing clip and play it (for non-hitscan weapons).
            if (!isHitscan && m_ShootingAudio != null && m_FireClip != null)
            {
                m_ShootingAudio.clip = m_FireClip;
                m_ShootingAudio.Play ();
            }

            if (m_Definition != null)
            {
                m_CurrentAmmo--;
                UpdateAmmoUI();
                if (m_CurrentAmmo <= 0)
                {
                    StartBlinking();
                    TriggerReload();
                }
            }

            // Reset the launch force.  This is a precaution in case of missing button events.
            m_CurrentLaunchForce = m_MinLaunchForce;
        }

        public void PlayRemoteShoot(Vector3 bulletForward)
        {
            // Note: We no longer set m_RemoteTargetDir here because bulletForward 
            // calculated from (spawnPos - tankCenter) is incorrect for multi-barrel tanks.
            // m_RemoteTargetDir is now perfectly set in RemoteFire using the exact yaw.

            // Play shooting audio (if not already playing to prevent double audio for multi-barrel shots)
            if (m_ShootingAudio != null && m_FireClip != null)
            {
                if (!m_ShootingAudio.isPlaying || m_ShootingAudio.time > 0.1f)
                {
                    m_ShootingAudio.clip = m_FireClip;
                    m_ShootingAudio.Play();
                }
            }
        }
        public void RemoteFire(float yaw, int barrelIndex, byte weaponType = 0)
        {
            if (m_TankHead != null)
            {
                m_RemoteTargetDir = new Vector3(Mathf.Sin(yaw), 0, Mathf.Cos(yaw));
                m_RemoteAimTimer = 1.0f;
            }

            if (weaponType != 1 && m_ShootingAudio != null && m_FireClip != null)
            {
                if (!m_ShootingAudio.isPlaying || m_ShootingAudio.time > 0.1f)
                {
                    m_ShootingAudio.clip = m_FireClip;
                    m_ShootingAudio.Play();
                }
            }

            // Play the recoil/shoot animation for remote tanks (only for hitscan, since projectiles handle it in GameManager.UpdateRemoteBullets)
            if (weaponType == 1)
            {
                var anim = GetComponent<TankAnimation>();
                if (anim != null)
                {
                    anim.PlayRemoteShoot();
                }
            }

            // For hitscan weapons (weaponType == 1), enable visuals temporarily instead of spawning shell
            if (weaponType == 1)
            {
                SetHitscanVisualsActive(true);
                // Dynamically bridge the gap between continuous fire packets (+0.15s buffer for latency jitter)
                float expectedGap = (m_FireRate > 0f) ? (1f / m_FireRate) : 0.2f;
                m_HitscanVisualTimer = expectedGap + 0.15f; 
            }
        }

        public float GetCurrentTurretYaw()
        {
            float turretYaw = transform.eulerAngles.y * Mathf.Deg2Rad;
            if (m_FireTransforms != null && m_FireTransforms.Length > 0 && m_FireTransforms[0] != null)
            {
                turretYaw = m_FireTransforms[0].eulerAngles.y * Mathf.Deg2Rad;
            }
            else if (m_TankHead != null)
            {
                turretYaw = m_TankHead.eulerAngles.y * Mathf.Deg2Rad;
            }
            return turretYaw;
        }

        public void SetRemoteTurretYaw(float yaw)
        {
            if (m_TankHead != null)
            {
                Vector3 targetDir = new Vector3(Mathf.Sin(yaw), 0, Mathf.Cos(yaw));
                Quaternion targetMuzzleRot = Quaternion.LookRotation(targetDir, transform.up);
                m_RemoteTurretTarget = targetMuzzleRot * Quaternion.Inverse(m_TurretToMuzzleOffset);
                m_HasRemoteTurretTarget = true;
            }
        }
    }
}