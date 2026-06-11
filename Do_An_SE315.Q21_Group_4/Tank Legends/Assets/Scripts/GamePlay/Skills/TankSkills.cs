using UnityEngine;
using Complete.Skills;
using System.Collections;

namespace Complete
{
    public class TankSkills : MonoBehaviour
    {
        [Header("Setup")]
        public SkillData skillData;
        [Tooltip("Gắn một Transform (như nòng súng hoặc đầu xe) để làm vị trí gốc cho skill. Nếu để trống sẽ dùng tâm của xe.")]
        public Transform m_SkillSpawnPoint;
        public bool m_IsLocalPlayer = false;
        public uint OwnerId;

        private SkillIndicator m_Indicator;
        private SkillBase m_SkillInstance;
        private bool m_IsAiming = false;
        private TankMovement m_Movement;

        private float m_CurrentChargeTime = 0f;
        private float m_PostFireLockTimer = 0f;
        private Vector3 m_LockedTurretDir = Vector3.zero;

        private void Awake()
        {
            m_Movement = GetComponent<TankMovement>();

            // Fetch skill from TankHealth if not assigned
            if (skillData == null)
            {
                var health = GetComponent<TankHealth>();
                if (health != null && health.m_Definition != null && health.m_Definition.Skills != null && health.m_Definition.Skills.Count > 0)
                {
                    skillData = health.m_Definition.Skills[0];
                    Debug.Log($"[TankSkills] Auto-assigned skill: {skillData.skillName}");
                }
                else
                {
                    Debug.LogWarning("[TankSkills] No SkillData assigned and none found on TankDefinitionSO!");
                }
            }

            // Find or create Indicator
            m_Indicator = GetComponentInChildren<SkillIndicator>();
            if (m_Indicator == null)
            {
                GameObject indicatorObj = new GameObject("SkillIndicator");
                indicatorObj.transform.SetParent(m_SkillSpawnPoint != null ? m_SkillSpawnPoint : transform);
                indicatorObj.transform.localPosition = Vector3.zero;
                
                var lr = indicatorObj.AddComponent<LineRenderer>();
                // Cố gắng dùng shader Legacy có hỗ trợ Vertex Color, hoặc Sprites/Default
                Shader s = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
                if (s == null) s = Shader.Find("Sprites/Default");
                lr.material = new Material(s);
                
                m_Indicator = indicatorObj.AddComponent<SkillIndicator>();
            }

            // Initialize the correct SkillBase implementation
            if (skillData != null)
            {
                m_SkillInstance = CreateSkillInstance(skillData);
            }
        }

        private SkillBase CreateSkillInstance(SkillData data)
        {
            switch (data.skillType)
            {
                case SkillType.Dash: return new DashSkill(data, gameObject);
                case SkillType.Buff: return new BuffSkill(data, gameObject);
                case SkillType.ShieldDome: return new ShieldDomeSkill(data, gameObject);
                case SkillType.Laser: return new LaserSkill(data, gameObject);
                case SkillType.Generic:
                default:
                    return new GenericSkill(data, gameObject);
            }
        }

        // Generic fallback skill
        private class GenericSkill : SkillBase
        {
            public GenericSkill(SkillData data, GameObject caster) : base(data, caster) { }
            public override void ServerExecute(Vector3 targetPos, Vector3 targetDir) { }
            public override void ClientExecute(Vector3 targetPos, Vector3 targetDir)
            {
                if (Data.vfxPrefab != null)
                {
                    // Lấy Pivot từ TankSkills
                    var ts = Owner.GetComponent<TankSkills>();
                    Transform pivot = (ts != null && ts.m_SkillSpawnPoint != null) ? ts.m_SkillSpawnPoint : Owner.transform;

                    // Nếu là buff bản thân thì dính vào pivot. 
                    // Các loại khác (Direction, Position) thì spawn rời ở ngoài.
                    bool shouldParent = (Data.targetingType == TargetingType.Self);
                    
                    // Direction (như laser) spawn ở pivot. Position (như lựu đạn) spawn ở targetPos
                    Vector3 spawnLocation = (Data.targetingType == TargetingType.Self || Data.targetingType == TargetingType.Direction) ? pivot.position : targetPos;

                    // Instantiate với Rotation gốc của Prefab để giữ nguyên trục X (hoặc các trục được thiết lập sẵn trong prefab)
                    var vfx = UnityEngine.Object.Instantiate(Data.vfxPrefab, spawnLocation, Data.vfxPrefab.transform.rotation);
                    
                    if (shouldParent)
                    {
                        vfx.transform.SetParent(pivot);
                        vfx.transform.localPosition = Vector3.zero;
                    }

                    // Xoay trục Y và Z theo hướng ngắm, giữ nguyên trục X của prefab
                    if (targetDir != Vector3.zero && Data.targetingType != TargetingType.Self)
                    {
                        float targetYaw = Mathf.Atan2(targetDir.x, targetDir.z) * Mathf.Rad2Deg;
                        Vector3 currentEuler = vfx.transform.eulerAngles;
                        // Giữ X gốc, cập nhật Y theo hướng ngắm. (Z có thể cập nhật nếu cần, tạm giữ nguyên)
                        vfx.transform.eulerAngles = new Vector3(currentEuler.x, targetYaw, currentEuler.z);
                    }
                    
                    float scaleFactor = 1f;
                    if (Data.vfxBaseSize > 0f)
                    {
                        Vector3 pScale = shouldParent ? pivot.lossyScale : Vector3.one;
                        Vector3 prefabScale = Data.vfxPrefab.transform.localScale;

                        if (Data.shapeType == ShapeType.Circle || Data.shapeType == ShapeType.Cone)
                        {
                            scaleFactor = Data.radius / Data.vfxBaseSize;
                            vfx.transform.localScale = new Vector3(
                                scaleFactor / pScale.x,
                                scaleFactor / pScale.y,
                                scaleFactor / pScale.z
                            );
                        }
                        else if (Data.shapeType == ShapeType.Line)
                        {
                            scaleFactor = Data.length / Data.vfxBaseSize;
                            vfx.transform.localScale = new Vector3(
                                prefabScale.x / pScale.x,
                                prefabScale.y / pScale.y,
                                scaleFactor / pScale.z
                            );
                        }
                    }

                    float destroyTime = Data.vfxDuration > 0f ? Data.vfxDuration : 3f;
                    UnityEngine.Object.Destroy(vfx, destroyTime);
                }

                if (Data.castSound != null)
                {
                    AudioSource.PlayClipAtPoint(Data.castSound, targetPos);
                }
            }
        }

        private void OnDestroy()
        {
            if (m_Indicator != null)
            {
                Destroy(m_Indicator.gameObject);
            }
        }

        public void RemoteCastSkill(string skillName, Vector3 targetPos, Vector3 targetDir)
        {
            Debug.Log($"[TankSkills] RemoteCastSkill called for {skillName}. Current skillData is: {(skillData != null ? skillData.name : "NULL")}. m_SkillInstance: {(m_SkillInstance != null ? "NOT NULL" : "NULL")}");
            if (skillData != null && skillData.name == skillName && m_SkillInstance != null)
            {
                Debug.Log($"[TankSkills] Executing ClientExecute for {skillName}");
                
                m_SkillInstance.ClientCancelCharge(); // Turn off charging VFX for remote

                // Mới dùng chiêu (remote player)
                m_SkillInstance.ClientExecute(targetPos, targetDir);
                
                // M_SkillInstance.ServerExecute should NOT be called on clients for remote players
            }
            else
            {
                Debug.LogWarning($"[TankSkills] RemoteCastSkill FAILED check! skillData: {skillData != null}, nameMatch: {(skillData != null ? skillData.name == skillName : false)}, m_SkillInstance: {m_SkillInstance != null}");
            }
        }

        public void RemoteStartChargeSkill(string skillName, Vector3 targetPos, Vector3 targetDir)
        {
            if (skillData != null && skillData.name == skillName && m_SkillInstance != null)
            {
                Debug.Log($"[TankSkills] Executing ClientStartCharge for {skillName}");
                // Indicator is local-only, so we don't enable it for remote players
                m_SkillInstance.ClientStartCharge(targetPos, targetDir);
            }
        }

        private void Update()
        {
            if (m_SkillInstance != null)
            {
                m_SkillInstance.TickCooldown(Time.deltaTime);
            }

            if (!m_IsLocalPlayer) return;

            // Update Cooldown UI
            if (m_SkillInstance != null && skillData != null && GameUIManager.Instance != null)
            {
                GameUIManager.Instance.UpdateSkillCooldown(m_SkillInstance.CurrentCooldown, skillData.cooldown);
            }

            if (m_SkillInstance == null) return;

            // Nếu Tank bị khóa điều khiển (Cinematic đầu game, kết thúc game...)
            if (m_Movement != null && m_Movement.m_IsInputFrozen)
            {
                if (m_IsAiming)
                {
                    m_IsAiming = false;
                    m_Indicator.DisableIndicator();
                    m_SkillInstance.ClientCancelCharge();
                    var ts = GetComponent<TankShooting>();
                    if (ts != null) ts.OverrideTurretTarget(Vector3.zero);
                }
                return;
            }

            // Get Input from InputManager
            bool skillDown, skillHeld, skillUp;
            if (InputManager.Instance != null)
            {
                InputManager.Instance.GetTankSkillInput(out skillDown, out skillHeld, out skillUp);
            }
            else
            {
                // Fallback PC input
                skillDown = Input.GetKeyDown(KeyCode.E);
                skillHeld = Input.GetKey(KeyCode.E);
                skillUp = Input.GetKeyUp(KeyCode.E);
            }

            Vector3 spawnPos = m_SkillSpawnPoint != null ? m_SkillSpawnPoint.position : transform.position;
            Vector3 spawnForward = m_SkillSpawnPoint != null ? m_SkillSpawnPoint.forward : transform.forward;

            Vector3 targetDir = spawnForward;
            Vector3 targetPos = spawnPos + targetDir * skillData.castRange;
            bool hasDir = false;

            if (InputManager.Instance != null && InputManager.Instance.IsMobileMode)
            {
                hasDir = InputManager.Instance.TryGetMobileSkillDirection(out Vector3 dir, out _);
                if (hasDir)
                {
                    targetDir = dir;
                    targetPos = spawnPos + targetDir * skillData.castRange;
                }
            }
            else
            {
                // PC Mouse Aim
                if (Camera.main != null && (skillHeld || skillDown || skillUp))
                {
                    Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
                    if (Physics.Raycast(ray, out RaycastHit hit, 100f, LayerMask.GetMask("Ground", "Surface")))
                    {
                        targetPos = hit.point;
                        targetPos.y = spawnPos.y; // Keep it on the same plane as the spawn point
                        targetDir = (targetPos - spawnPos).normalized;
                        targetDir.y = 0;
                        hasDir = true;
                    }
                }
            }

            if (m_PostFireLockTimer > 0f)
            {
                m_PostFireLockTimer -= Time.deltaTime;
                var tsLocked = GetComponent<TankShooting>();
                if (tsLocked != null)
                {
                    tsLocked.OverrideTurretTarget(m_LockedTurretDir);
                }
            }
            else if (!m_IsAiming)
            {
                // Release turret override and movement freeze if we just stopped aiming and lock expired
                var tsFree = GetComponent<TankShooting>();
                if (tsFree != null) 
                {
                    tsFree.OverrideTurretTarget(Vector3.zero);
                }

                if (skillData != null && m_Movement != null)
                {
                    m_Movement.m_SkillSpeedMultiplier = 1f;
                }
            }

            // Aiming Start Logic
            // Don't allow new cast while locked from a previous heavy skill or if skill is on cooldown
            if ((skillDown || (skillHeld && !m_IsAiming)) && m_PostFireLockTimer <= 0f)
            {
                if (m_SkillInstance.CanCast())
                {
                    m_IsAiming = true;
                    m_CurrentChargeTime = 0f;

                    m_Indicator.EnableIndicator(skillData);
                    // Kích hoạt Charging VFX ngay lúc bắt đầu giữ phím (Hold to Charge)
                    m_SkillInstance.ClientStartCharge(targetPos, targetDir);

                    // Sync charging VFX to other clients
                    TankNet.TankNetClient.Instance.SendCastSkill(skillData.name, targetPos, targetDir, true);
                }
            }

            bool doExecute = false;
            bool isCancelled = false;

            if (m_IsAiming)
            {
                m_CurrentChargeTime += Time.deltaTime;

                if (skillData != null && m_Movement != null)
                {
                    float multiplier = 1f - (skillData.speedReductionPercent / 100f);
                    m_Movement.m_SkillSpeedMultiplier = Mathf.Clamp01(multiplier);
                }

                // Update Indicator
                if (hasDir && m_SkillInstance.CanCast())
                {
                    m_Indicator.transform.rotation = Quaternion.LookRotation(targetDir, Vector3.up);
                }
                
                // Quay nòng súng theo joystick
                var ts = GetComponent<TankShooting>();
                if (ts != null)
                {
                    ts.OverrideTurretTarget(targetDir);
                }

                // Auto fire if max charge time reached
                if (skillData.chargeTime > 0f && m_CurrentChargeTime >= skillData.chargeTime)
                {
                    doExecute = true;
                }
            }

            // Casting Logic (Release early)
            if (skillUp && m_IsAiming)
            {
                if (skillData.chargeTime > 0f && m_CurrentChargeTime < skillData.chargeTime)
                {
                    isCancelled = true;
                }
                else
                {
                    doExecute = true;
                }
            }

            if (doExecute || isCancelled)
            {
                m_IsAiming = false;
                m_Indicator.DisableIndicator();

                if (skillData != null && m_Movement != null)
                {
                    m_Movement.m_SkillSpeedMultiplier = 1f;
                }

                // Ngừng Charging VFX
                m_SkillInstance.ClientCancelCharge();

                var ts = GetComponent<TankShooting>();

                if (doExecute)
                {
                    if (m_SkillInstance.CanCast())
                    {
                        Vector3 finalDir = targetDir;
                        
                        // Lấy góc quay thực tế của nòng súng để bắn chuẩn xác
                        if (ts != null && ts.m_TankHead != null && skillData.targetingType != TargetingType.Position)
                        {
                            finalDir = ts.m_TankHead.forward;
                            finalDir.y = 0;
                            if (finalDir != Vector3.zero) finalDir.Normalize();
                        }

                        // Lock turret direction for a short time after firing to give a "heavy" feel
                        if (skillData.postFireTurretLockTime > 0f)
                        {
                            m_PostFireLockTimer = skillData.postFireTurretLockTime;
                            m_LockedTurretDir = finalDir;
                            if (ts != null) ts.OverrideTurretTarget(finalDir);
                        }
                        else
                        {
                            if (ts != null) ts.OverrideTurretTarget(Vector3.zero); // Trả lại nòng súng
                        }

                        Vector3 finalPos = spawnPos + finalDir * skillData.castRange;

                        if (TankNet.TankNetClient.Instance != null && TankNet.TankNetClient.Instance.IsConnected)
                        {
                            TankNet.TankNetClient.Instance.SendCastSkill(skillData.name, finalPos, finalDir);
                            // We do not wait for the server to confirm if we want prediction for local player
                            // But usually skills are sent to server and we simulate them locally right away
                            m_SkillInstance.ClientExecute(finalPos, finalDir);
                        }
                        else
                        {
                            m_SkillInstance.ServerExecute(finalPos, finalDir);
                            m_SkillInstance.ClientExecute(finalPos, finalDir);
                        }

                        m_SkillInstance.StartCooldown();
                    }
                    else
                    {
                        Debug.Log($"[TankSkills] Skill on cooldown! {m_SkillInstance.CurrentCooldown:F1}s");
                        if (ts != null) ts.OverrideTurretTarget(Vector3.zero);
                    }
                }
                else if (isCancelled)
                {
                    // Trả lại nòng súng nếu hủy
                    if (ts != null) ts.OverrideTurretTarget(Vector3.zero);
                }
            }
        }
    }
}
