using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using TankNet;

namespace Complete
{
    public class GameManager : MonoBehaviour
    {
        public int m_NumRoundsToWin = 5;            // The number of rounds a single player has to win to win the game.
        public float m_StartDelay = 3f;             // The delay between the start of RoundStarting and RoundPlaying phases.
        public float m_EndDelay = 3f;               // The delay between the end of RoundPlaying and RoundEnding phases.
        public CameraControl m_CameraControl;       // Reference to the CameraControl script for control during different phases.
        public GameObject m_TankPrefab;             // Reference to the prefab the players will control.
        public TankManager[] m_Tanks;               // A collection of managers for enabling and disabling different aspects of the tanks.

        [Header("Online")]
        public bool m_OnlineMode = false;
        public uint m_MyPlayerId = 1;
        public string m_ServerHost = "127.0.0.1";
        public int m_ServerPort = 8080;
        public uint m_MatchId = 1;
        public GameObject m_RemoteTankPrefab;
        public GameObject m_RemoteBulletPrefab;

        [System.Serializable]
        public struct TankPrefabMapping
        {
            public int typeIndex;       // 0=BULLDOG, 1=PHOENIX, 2=TITAN, 3=RHINO
            public string tankName;     // For self-documenting and readability in Inspector
            public GameObject prefab;   // The gameplay prefab
        }

        [Header("Available Tank Prefabs")]
        [Tooltip("Prefab list mapping to server tank indices explicitly")]
        public List<TankPrefabMapping> m_TankPrefabMappings;

        [Header("Match End UI")]
        public string lobbySceneName = "Lobby";
        
        [Header("Match End Intermediate Screens")]
        private bool _skipEndDelay = false;

        [Header("Cinematic Intro")]
        public float cinematicPhase1Duration = 1.5f;
        public float cinematicPhase2Duration = 1.5f;
        public float cinematicPhase3Duration = 1.5f;
        public Vector3 cinematicFrontOffset = new Vector3(0, 2f, 5f);
        public Vector3 cinematicBackOffset = new Vector3(0, 3f, -4f);
        public float cinematicSideBulge = 6f;
        public float cinematicLookAtHeight = 1.5f;
        public float cameraShakeDuration = 0.4f;
        public float cameraShakeMagnitude = 0.25f;
        public float postCinematicWait = 0.6f;

        // ── Match tracking (online) ───────────────────────────────────────────
        private float   _matchStartTime;
        private bool    _matchEnded;
        private int     _myKills;
        private int     _myDeaths;
        private string  _opponentId = "bot-1";
        private int     _totalPlayersSeen;
        private float   _serverTimeRemaining = -1f;
        private float   _highestTimeRemaining = 0f;
        private bool    _isPlaying = false;
        private readonly Dictionary<uint, bool> _prevAliveStatus = new();

        private readonly Dictionary<uint, GameObject> _remoteTanks   = new();
        private readonly Dictionary<uint, GameObject> _remoteBullets = new();

        // ── Snapshot interpolation ───────────────────────────────────────────
        private struct SnapEntry { public float t; public Vector3 pos; public Quaternion rot; public float turretYaw; }

        // Delay behind real-time to interpolate. 2 snapshot intervals @ 60Hz = ~33ms.
        private const float INTERP_DELAY = 0.033f;
        private const int   SNAP_BUFFER  = 8;

        private readonly List<SnapEntry> _localSnaps  = new();
        private readonly Dictionary<uint, List<SnapEntry>> _remoteSnaps = new();

        private int m_RoundNumber;                  // Which round the game is currently on.
        private WaitForSeconds m_StartWait;         // Used to have a delay whilst the round starts.
        private WaitForSeconds m_EndWait;           // Used to have a delay whilst the round or game ends.
        private TankManager m_RoundWinner;          // Reference to the winner of the current round.  Used to make an announcement of who won.
        private TankManager m_GameWinner;           // Reference to the winner of the game.  Used to make an announcement of who won.


        private void Awake()
        {
            _matchStartTime = Time.time;

            if (GlobalMatchState.HasMatchInfo)
            {
                m_OnlineMode  = true;
                m_MatchId     = GlobalMatchState.MatchId;
                m_ServerHost  = GlobalMatchState.ServerHost;
                m_ServerPort  = GlobalMatchState.ServerPort;
                if (GlobalMatchState.PlayerId > 0)
                    m_MyPlayerId = GlobalMatchState.PlayerId;
                Debug.Log($"[GameManager] Loaded MatchInfo: MatchId={m_MatchId}, {m_ServerHost}:{m_ServerPort}, playerId={m_MyPlayerId}");
            }

            if (GlobalMatchState.LocalTankPrefab != null)
            {
                m_TankPrefab = GlobalMatchState.LocalTankPrefab;
                Debug.Log($"[GameManager] Using deployed tank prefab: {m_TankPrefab.name}");
            }

            // Đọc -playerid từ command line: TankLegends.exe -playerid 2
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-playerid" && uint.TryParse(args[i + 1], out uint id))
                {
                    m_MyPlayerId = id;
                    Debug.Log($"[GameManager] playerid từ args: {id}");
                }
            }
        }

        private void Start()
        {
            // Match Unity physics tick to server tick rate so dt is identical on both sides
            if (m_OnlineMode)
            {
                Time.fixedDeltaTime = 1f / 60f;
                // Match server BULLET_GRAVITY = 3.0 so bullet arc and landing point are identical
                Physics.gravity = new Vector3(0f, -3.0f, 0f);
            }

            m_StartWait = new WaitForSeconds (m_StartDelay);
            m_EndWait = new WaitForSeconds (m_EndDelay);

            SpawnAllTanks();
            SetCameraTargets();

            if (m_OnlineMode)
            {
                if (TankNetClient.Instance == null)
                {
                    Debug.LogError("[GameManager] m_OnlineMode=true nhưng không tìm thấy TankNetClient trong scene. " +
                                   "Tạo GameObject [Network] và attach TankNetClient component.");
                    m_OnlineMode = false;
                }
                else
                {
                    // Clear stale remote tanks from any previous session before subscribing
                    foreach (var go in _remoteTanks.Values)
                        if (go != null) Destroy(go);
                    _remoteTanks.Clear();
                    _remoteSnaps.Clear();

                    TankNetClient.Instance.OnSnapshot    += HandleSnapshot;
                    TankNetClient.Instance.OnMatchEnd    += HandleMatchEnd;
                    TankNetClient.Instance.OnForceLogout += HandleForceLogout;
                    TankNetClient.Instance.OnEventShoot  += HandleEventShoot;

                    TankNetClient.Instance.Connect(m_ServerHost, m_ServerPort, m_MatchId, m_MyPlayerId);
                }
            }

            StartCoroutine (GameLoop ());
        }



        private void Update()
        {
            if (!m_OnlineMode) return;

            // Local tank: prediction handles movement, no interpolation needed here
            // Remote tanks: snapshot interpolation for smooth visuals
            float renderTime = Time.time - INTERP_DELAY;
            foreach (var kvp in _remoteTanks)
            {
                if (kvp.Value == null) continue;
                if (_remoteSnaps.TryGetValue(kvp.Key, out var buf))
                {
                    Vector3 oldPos = kvp.Value.transform.position;
                    ApplyInterp(buf, kvp.Value.transform, renderTime);
                    Vector3 newPos = kvp.Value.transform.position;

                    // Sync animation chạy bằng cách đo khoảng cách dịch chuyển
                    bool isMoving = Vector3.Distance(oldPos, newPos) > 0.01f;
                    var anim = kvp.Value.GetComponent<TankAnimation>();
                    if (anim != null) anim.SetMoving(isMoving);
                }
            }

            if (m_OnlineMode && GameUIManager.Instance != null && _serverTimeRemaining >= 0f)
            {
                if (_serverTimeRemaining > _highestTimeRemaining)
                {
                    _highestTimeRemaining = _serverTimeRemaining;
                }

                if (!_isPlaying)
                {
                    // Freeze timer at max seen time during cinematic
                    GameUIManager.Instance.SetMatchTimer(_highestTimeRemaining);
                }
                else
                {
                    GameUIManager.Instance.SetMatchTimer(_serverTimeRemaining);
                }
            }

            if (m_OnlineMode && GameUIManager.Instance != null && TankNetClient.Instance != null)
            {
                if (TankNetClient.Instance.IsConnected)
                {
                    int pingTime = (int)TankNetClient.Instance.PingMs;
                    GameUIManager.Instance.SetPing(pingTime);
                }
                else
                {
                    GameUIManager.Instance.SetPingOffline();
                }
            }
        }

        private static void ApplyInterp(List<SnapEntry> buf, Transform tr, float renderTime)
        {
            if (buf.Count == 0) return;

            // Find two entries bracketing renderTime
            int prev = -1, next = -1;
            for (int i = 0; i < buf.Count; i++)
            {
                if (buf[i].t <= renderTime) prev = i;
                else { next = i; break; }
            }

            float currentTurretYaw = 0f;

            if (prev == -1)      // all entries are in the future — show oldest
            { 
                tr.position = buf[0].pos; 
                tr.rotation = buf[0].rot; 
                currentTurretYaw = buf[0].turretYaw;
            }
            else if (next == -1) // all entries are in the past — show newest
            { 
                tr.position = buf[prev].pos; 
                tr.rotation = buf[prev].rot; 
                currentTurretYaw = buf[prev].turretYaw;
            }
            else                 // interpolate between prev and next
            {
                float span = buf[next].t - buf[prev].t;
                float f    = span > 0f ? (renderTime - buf[prev].t) / span : 1f;
                tr.position = Vector3.Lerp(buf[prev].pos, buf[next].pos, f);
                tr.rotation = Quaternion.Slerp(buf[prev].rot, buf[next].rot, f);
                
                // Interpolate turret yaw safely (handling -pi to +pi wrapping)
                float prevYaw = buf[prev].turretYaw;
                float nextYaw = buf[next].turretYaw;
                // Unwrap angles if difference is > PI
                if (nextYaw - prevYaw > Mathf.PI) nextYaw -= 2f * Mathf.PI;
                else if (prevYaw - nextYaw > Mathf.PI) nextYaw += 2f * Mathf.PI;
                
                currentTurretYaw = Mathf.Lerp(prevYaw, nextYaw, f);
            }

            var shooting = tr.GetComponent<Complete.TankShooting>();
            if (shooting != null)
            {
                shooting.SetRemoteTurretYaw(currentTurretYaw);
            }

            // Trim entries older than renderTime - 0.2s (keep a small tail)
            while (buf.Count > 2 && buf[1].t < renderTime - 0.2f)
                buf.RemoveAt(0);
        }

        private void OnDestroy()
        {
            if (m_OnlineMode && TankNetClient.Instance != null)
            {
                TankNetClient.Instance.OnSnapshot    -= HandleSnapshot;
                TankNetClient.Instance.OnMatchEnd    -= HandleMatchEnd;
                TankNetClient.Instance.OnForceLogout -= HandleForceLogout;
                TankNetClient.Instance.OnEventShoot  -= HandleEventShoot;
                TankNetClient.Instance.Disconnect();
            }

            // Xóa hết tank khi scene kết thúc
            foreach (var t in GameObject.FindGameObjectsWithTag("Tank"))
                if (t != null) Destroy(t);
            foreach (var go in _remoteTanks.Values)
                if (go != null) Destroy(go);
            _remoteTanks.Clear();
            _remoteSnaps.Clear();

            foreach (var go in _remoteBullets.Values)
                if (go != null) Destroy(go);
            _remoteBullets.Clear();
        }

        // Gọi từ Lobby sau khi matching service trả về thông tin server
        public void JoinMatch(uint matchId, string host, int port)
        {
            m_MatchId = matchId;
            m_ServerHost = host;
            m_ServerPort = port;
        }


        private void SpawnAllTanks()
        {
            // Xóa hết tank đang active trong scene trước khi spawn mới
            foreach (var t in GameObject.FindGameObjectsWithTag("Tank"))
                Destroy(t);
            for (int i = 0; i < m_Tanks.Length; i++)
                m_Tanks[i].m_Instance = null;

            for (int i = 0; i < m_Tanks.Length; i++)
            {
                // Online mode: không spawn gì cả, chờ server gửi vị trí qua snapshot
                if (m_OnlineMode) continue;

                m_Tanks[i].m_Instance =
                    Instantiate(m_TankPrefab, m_Tanks[i].m_SpawnPoint.position, m_Tanks[i].m_SpawnPoint.rotation) as GameObject;
                m_Tanks[i].m_Instance.SetActive(true);
                m_Tanks[i].m_PlayerNumber = i + 1;
                m_Tanks[i].Setup();
            }

            if (!m_OnlineMode && m_Tanks.Length > 0 && m_Tanks[0].m_Instance != null)
            {
                GameUIManager.Instance.UpdateHUDForLocalTank(m_Tanks[0].m_Instance);
            }
        }

        // Loại bỏ hoặc chuyển collider vật lý trên tank khi online.
        // Server là authoritative — Unity physics không được phép di chuyển tank.
        // Giữ lại BulletHitVolume (trigger) vì nó dùng để detect visual hit, không phải physics.
        //
        // BoxCollider gốc trên root tank object sẽ được chuyển thành isTrigger
        // để hỗ trợ phát hiện va chạm với bụi rậm (Bush stealth).
        // Các collider con khác (không liên quan) sẽ bị disable/destroy.
        //
        // KHÔNG destroy Rigidbody: TankMovement cache m_Rigidbody trong Awake() và gọi
        // MovePosition/MoveRotation mỗi FixedUpdate. Destroy rb → MissingReferenceException
        // ngay frame sau spawn → tank crash. Giữ rb kinematic là đủ (kinematic rb không
        // tham gia collision resolution, chỉ dùng cho movement API).
        private static void DisablePhysicsColliders(GameObject tankGo)
        {
            // Identify the root BoxCollider — keep it as a trigger for bush detection
            var rootBox = tankGo.GetComponent<BoxCollider>();

            foreach (var col in tankGo.GetComponentsInChildren<Collider>())
            {
                if (col.gameObject.name == "BulletHitVolume") continue;

                // Keep the root BoxCollider alive as a trigger for bush stealth
                if (col == rootBox)
                {
                    col.isTrigger = true;
                    continue;
                }

#if UNITY_EDITOR
                col.enabled = false;
#else
                Object.Destroy(col);
#endif
            }
        }

        // Add a separate tall trigger BoxCollider (child) for bullet-only hit detection.
        // The original BoxCollider is untouched so physics (wall/tank collisions) is unaffected.
        // Bullet collision is XZ-only — mirrors the server which ignores Y when checking hits.
        private static void AddBulletHitTrigger(GameObject tankGo)
        {
            var src = tankGo.GetComponent<BoxCollider>() ?? tankGo.GetComponentInChildren<BoxCollider>();

            var child = new GameObject("BulletHitVolume");
            child.transform.SetParent(tankGo.transform, false);

            var col = child.AddComponent<BoxCollider>();
            col.isTrigger = true;
            if (src != null)
            {
                col.size   = new Vector3(src.size.x, 100f, src.size.z);
                col.center = new Vector3(src.center.x, 0f,  src.center.z);
            }
            else
            {
                col.size   = new Vector3(2f, 100f, 2f);
                col.center = Vector3.zero;
            }
        }

        private void SpawnLocalTankAt(TankState ts)
        {
            if (m_Tanks.Length == 0) return;

            var pos = new Vector3(ts.x, ts.y, ts.z);
            var rot = Quaternion.Euler(0, ts.yaw * Mathf.Rad2Deg, 0);

            // Unpack the tank type index from flags (bits 2-7)
            int typeIndex = (ts.flags >> 2) & 0x3F;
            GameObject prefabToSpawn = m_TankPrefab; // fallback to default local tank prefab

            if (m_TankPrefabMappings != null)
            {
                foreach (var mapping in m_TankPrefabMappings)
                {
                    if (mapping.typeIndex == typeIndex && mapping.prefab != null)
                    {
                        prefabToSpawn = mapping.prefab;
                        break;
                    }
                }
            }

            m_Tanks[0].m_Instance    = Instantiate(prefabToSpawn, pos, rot) as GameObject;
            m_Tanks[0].m_Instance.SetActive(true);
            m_Tanks[0].m_PlayerNumber = 1;
            m_Tanks[0].Setup();

            var tm = m_Tanks[0].m_Instance.GetComponent<Complete.TankMovement>();
            bool customPhysics = tm == null || tm.m_UseCustomOnlinePhysics;

            var rb = m_Tanks[0].m_Instance.GetComponent<Rigidbody>();
            if (rb != null) { rb.isKinematic = customPhysics; rb.useGravity = !customPhysics; }

            AddBulletHitTrigger(m_Tanks[0].m_Instance);
            if (customPhysics) DisablePhysicsColliders(m_Tanks[0].m_Instance);

            // Bush stealth for local tank
            var localStealth = m_Tanks[0].m_Instance.GetComponent<TankStealth>()
                            ?? m_Tanks[0].m_Instance.AddComponent<TankStealth>();
            localStealth.IsLocalTank = true;

            // Camera follow local tank
            m_CameraControl.m_Targets = new Transform[] { m_Tanks[0].m_Instance.transform };

            GameUIManager.Instance.UpdateHUDForLocalTank(m_Tanks[0].m_Instance);
        }


        private void SetCameraTargets()
        {
            // Online: camera sẽ được set trong SpawnLocalTankAt() khi nhận snapshot đầu tiên
            if (m_OnlineMode) return;

            Transform[] targets = new Transform[m_Tanks.Length];
            for (int i = 0; i < targets.Length; i++)
                targets[i] = m_Tanks[i].m_Instance.transform;
            m_CameraControl.m_Targets = targets;
        }


        // This is called from start and will run each phase of the game one after another.
        private IEnumerator GameLoop ()
        {
            // Start off by running the 'RoundStarting' coroutine but don't return until it's finished.
            yield return StartCoroutine (RoundStarting ());

            // Once the 'RoundStarting' coroutine is finished, run the 'RoundPlaying' coroutine but don't return until it's finished.
            yield return StartCoroutine (RoundPlaying());

            // Once execution has returned here, run the 'RoundEnding' coroutine, again don't return until it's finished.
            yield return StartCoroutine (RoundEnding());

            // This code is not run until 'RoundEnding' has finished.  At which point, check if a game winner has been found.
            if (m_GameWinner != null)
            {
                // If there is a game winner, restart the level.
                SceneManager.LoadScene (0);
            }
            else
            {
                // If there isn't a winner yet, restart this coroutine so the loop continues.
                // Note that this coroutine doesn't yield.  This means that the current version of the GameLoop will end.
                StartCoroutine (GameLoop ());
            }
        }


        private IEnumerator RoundStarting ()
        {
            _isPlaying = false;

            // As soon as the round starts reset the tanks and make sure they can't move.
            ResetAllTanks ();
            DisableTankControl ();

            // Wait for tank to spawn (especially in online mode where it waits for server snapshot)
            Transform targetTank = null;
            float waitSpawnTimeout = 5f;
            while (targetTank == null && waitSpawnTimeout > 0)
            {
                for (int i = 0; i < m_Tanks.Length; i++)
                {
                    if (m_Tanks[i].m_Instance != null && m_Tanks[i].m_Instance.activeSelf)
                    {
                        // In online mode, ensure we pick our local tank
                        if (m_OnlineMode && i != 0) continue; 
                        
                        targetTank = m_Tanks[i].m_Instance.transform;
                        break;
                    }
                }

                if (targetTank == null)
                {
                    waitSpawnTimeout -= Time.deltaTime;
                    yield return null;
                }
            }
            
            // Re-apply disable control in case the tank spawned AFTER we called it earlier
            DisableTankControl ();

            if (targetTank != null)
            {
                // Start cinematic
                m_CameraControl.m_IsCinematicMode = true;
                
                Camera cam = m_CameraControl.GetComponentInChildren<Camera>();
                Vector3 originalLocalPos = cam.transform.localPosition;
                Quaternion originalLocalRot = cam.transform.localRotation;

                // Move rig to origin to allow camera local movement
                m_CameraControl.transform.position = Vector3.zero;

                // Phase 1: Front of tank
                float elapsed = 0f;
                while (elapsed < cinematicPhase1Duration)
                {
                    elapsed += Time.deltaTime;
                    
                    // Dynamic dots with invisible characters to preserve alignment
                    int numDots = (int)((elapsed * 3f) % 4);
                    string visibleDots = new string('.', numDots);
                    string invisibleDots = new string('.', 3 - numDots);
                    if (GameUIManager.Instance != null) GameUIManager.Instance.SetMessageText($"Initializing{visibleDots}<color=#00000000>{invisibleDots}</color>");

                    Vector3 currentFrontPos = targetTank.position + targetTank.rotation * cinematicFrontOffset;
                    // Interpolate smoothly towards the front pos to absorb any landing jitters
                    cam.transform.position = Vector3.Lerp(cam.transform.position, currentFrontPos, Time.deltaTime * 10f);
                    cam.transform.LookAt(targetTank.position + Vector3.up * cinematicLookAtHeight);
                    
                    yield return null;
                }

                elapsed = 0f;
                Vector3 startPos = cam.transform.position;
                while (elapsed < cinematicPhase2Duration)
                {
                    elapsed += Time.deltaTime;
                    
                    // Dynamic dots with invisible characters to preserve alignment
                    int numDots = (int)((elapsed * 3f) % 4);
                    string visibleDots = new string('.', numDots);
                    string invisibleDots = new string('.', 3 - numDots);
                    if (GameUIManager.Instance != null) GameUIManager.Instance.SetMessageText($"Combat Mode Activated{visibleDots}<color=#00000000>{invisibleDots}</color>");

                    float t = Mathf.SmoothStep(0f, 1f, elapsed / cinematicPhase2Duration);
                    
                    Vector3 currentBackPos = targetTank.position + targetTank.rotation * cinematicBackOffset;
                    Vector3 linearPos = Vector3.Lerp(startPos, currentBackPos, t);
                    
                    // Add sine wave offset to push camera to the side during the slide
                    float sideOffset = Mathf.Sin(t * Mathf.PI) * cinematicSideBulge;
                    cam.transform.position = linearPos + targetTank.right * sideOffset;
                    
                    Quaternion targetRot = Quaternion.LookRotation((targetTank.position + Vector3.up * cinematicLookAtHeight) - cam.transform.position);
                    cam.transform.rotation = Quaternion.Slerp(cam.transform.rotation, targetRot, Time.deltaTime * 15f);
                    
                    yield return null;
                }

                // Phase 3: Fly up to top-down gameplay view
                m_CameraControl.m_IsCinematicMode = false; 
                m_CameraControl.SetStartPositionAndSize();
                
                Vector3 targetRigPos = m_CameraControl.transform.position;
                m_CameraControl.transform.position = Vector3.zero; 
                m_CameraControl.m_IsCinematicMode = true;
                
                Vector3 finalWorldPos = targetRigPos + m_CameraControl.transform.TransformVector(originalLocalPos);
                Quaternion finalWorldRot = m_CameraControl.transform.rotation * originalLocalRot; 
                
                startPos = cam.transform.position;
                Quaternion startRot = cam.transform.rotation;
                
                elapsed = 0f;
                while (elapsed < cinematicPhase3Duration)
                {
                    elapsed += Time.deltaTime;
                    // Dynamic dots with invisible characters to preserve alignment
                    int numDots = (int)((elapsed * 3f) % 4);
                    string visibleDots = new string('.', numDots);
                    string invisibleDots = new string('.', 3 - numDots);
                    if (GameUIManager.Instance != null) GameUIManager.Instance.SetMessageText($"Combat Mode Activated{visibleDots}<color=#00000000>{invisibleDots}</color>");

                    float t = Mathf.SmoothStep(0f, 1f, elapsed / cinematicPhase3Duration);
                    
                    cam.transform.position = Vector3.Lerp(startPos, finalWorldPos, t);
                    
                    // Always calculate a look-at rotation towards the tank from current position
                    Quaternion lockOnRot = Quaternion.LookRotation((targetTank.position + Vector3.up * cinematicLookAtHeight) - cam.transform.position);
                    // Gradually blend from locking onto the tank to the final gameplay rotation
                    cam.transform.rotation = Quaternion.Slerp(lockOnRot, finalWorldRot, t);
                    
                    yield return null;
                }
                
                // End cinematic
                m_CameraControl.transform.position = targetRigPos;
                cam.transform.localPosition = originalLocalPos;
                cam.transform.localRotation = originalLocalRot;
                m_CameraControl.m_IsCinematicMode = false;
            }
            else
            {
                // Fallback if no tank
                m_CameraControl.SetStartPositionAndSize();
                yield return m_StartWait;
            }

            // Increment the round number
            m_RoundNumber++;
            
            // Add slight camera shake
            StartCoroutine(CameraShake(cameraShakeDuration, cameraShakeMagnitude));

            // Wait a tiny bit before allowing control
            yield return new WaitForSeconds(postCinematicWait);
        }

        private IEnumerator CameraShake(float duration, float magnitude)
        {
            Camera cam = m_CameraControl.GetComponentInChildren<Camera>();
            if (cam == null) yield break;

            Vector3 originalLocalPos = cam.transform.localPosition;

            float elapsed = 0f;
            while (elapsed < duration)
            {
                float x = UnityEngine.Random.Range(-1f, 1f) * magnitude;
                float y = UnityEngine.Random.Range(-1f, 1f) * magnitude;

                cam.transform.localPosition = originalLocalPos + new Vector3(x, y, 0);

                elapsed += Time.deltaTime;
                yield return null;
            }

            cam.transform.localPosition = originalLocalPos;
        }


        private IEnumerator RoundPlaying ()
        {
            _isPlaying = true;

            // As soon as the round begins playing let the players control the tanks.
            EnableTankControl ();

            // Show Engage!
            if (GameUIManager.Instance != null) GameUIManager.Instance.SetMessageText("Engage.");
            StartCoroutine(ClearMessageAfterDelay(1.5f));

            // While there is not one tank left...
            while (!OneTankLeft())
            {
                // ... return on the next frame.
                yield return null;
            }
        }

        private IEnumerator ClearMessageAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            if (_isPlaying && GameUIManager.Instance != null) 
            {
                GameUIManager.Instance.SetMessageText(string.Empty);
            }
        }

        private IEnumerator RoundEnding ()
        {
            // Stop tanks from moving.
            DisableTankControl ();

            // Clear the winner from the previous round.
            m_RoundWinner = null;

            // See if there is a winner now the round is over.
            m_RoundWinner = GetRoundWinner ();

            // If there is a winner, increment their score.
            if (m_RoundWinner != null)
                m_RoundWinner.m_Wins++;

            // Now the winner's score has been incremented, see if someone has one the game.
            m_GameWinner = GetGameWinner ();

            // Get a message based on the scores and whether or not there is a game winner and display it.
            if (GameUIManager.Instance != null)
            {
                GameUIManager.Instance.ShowEndMessage(m_OnlineMode, m_Tanks, m_RoundWinner, m_GameWinner);
            }

            // Wait for the specified length of time until yielding control back to the game loop.
            yield return m_EndWait;
        }


        private bool OneTankLeft()
        {
            // Online mode: server quyết định khi nào round kết thúc
            if (m_OnlineMode) return false;

            int numTanksLeft = 0;
            for (int i = 0; i < m_Tanks.Length; i++)
            {
                if (m_Tanks[i].m_Instance != null && m_Tanks[i].m_Instance.activeSelf)
                    numTanksLeft++;
            }
            return numTanksLeft <= 1;
        }
        
        
        private TankManager GetRoundWinner()
        {
            for (int i = 0; i < m_Tanks.Length; i++)
            {
                if (m_Tanks[i].m_Instance != null && m_Tanks[i].m_Instance.activeSelf)
                    return m_Tanks[i];
            }
            return null;
        }


        // This function is to find out if there is a winner of the game.
        private TankManager GetGameWinner()
        {
            // Go through all the tanks...
            for (int i = 0; i < m_Tanks.Length; i++)
            {
                // ... and if one of them has enough rounds to win the game, return it.
                if (m_Tanks[i].m_Wins == m_NumRoundsToWin)
                    return m_Tanks[i];
            }

            // If no tanks have enough rounds to win, return null.
            return null;
        }



        private void ResetAllTanks()
        {
            for (int i = 0; i < m_Tanks.Length; i++)
            {
                if (m_Tanks[i].m_Instance != null)
                    m_Tanks[i].Reset();
            }
        }

        // ── Online: xử lý snapshot từ server (20Hz) ──────────────────────────

        private void HandleSnapshot(SnapshotData snap)
        {
            // Server tells us our assigned ID — sync it once
            if (snap.LocalPlayerId > 0 && m_MyPlayerId != snap.LocalPlayerId)
            {
                Debug.Log($"[GameManager] Server assigned playerId={snap.LocalPlayerId} (was {m_MyPlayerId})");
                m_MyPlayerId = snap.LocalPlayerId;
            }

            var seen = new HashSet<uint>();
            foreach (var ts in snap.Tanks)
            {
                seen.Add(ts.tankId);
                if (ts.tankId == m_MyPlayerId)
                    ApplyLocalCorrection(ts);
                else
                    UpdateRemoteTank(ts);
            }

            // Xóa remote tank không còn trong snapshot
            foreach (var id in new List<uint>(_remoteTanks.Keys))
                if (!seen.Contains(id)) DespawnRemote(id);

            // Render bullets fired by opponent tanks (skip own bullets — already shown locally)
            UpdateRemoteBullets(snap.Bullets);

            // Check if match should end
            if (!_matchEnded) CheckMatchEnd(snap);

            // Sync server time remaining for HUD timer
            _serverTimeRemaining = snap.TimeRemaining;
        }

        private void HandleEventShoot(EventShootPacket pkt)
        {
            if (pkt.shooterId == m_MyPlayerId) return; // We already spawned our own bullet visually

            if (_remoteTanks.TryGetValue(pkt.shooterId, out var go))
            {
                var shooting = go.GetComponent<Complete.TankShooting>();
                if (shooting != null)
                {
                    for (int i = 0; i < pkt.barrelCount; i++)
                    {
                        shooting.RemoteFire(pkt.turretYaw, i, pkt.weaponType);
                    }
                }
            }
        }

        // Cached bullet prefab: m_RemoteBulletPrefab if assigned, otherwise auto-detected
        // from TankShooting.m_Shell so no manual inspector step is required.
        private GameObject _bulletPrefabCache;

        private GameObject GetBulletPrefab()
        {
            if (_bulletPrefabCache != null) return _bulletPrefabCache;

            if (m_RemoteBulletPrefab != null)
            {
                _bulletPrefabCache = m_RemoteBulletPrefab;
                return _bulletPrefabCache;
            }

            // Auto-detect: grab the shell prefab from the tank prefab's TankShooting component
            if (m_TankPrefab != null)
            {
                var ts = m_TankPrefab.GetComponent<TankShooting>();
                if (ts != null && ts.m_Shell != null)
                {
                    _bulletPrefabCache = ts.m_Shell.gameObject;
                    return _bulletPrefabCache;
                }
            }

            Debug.LogWarning("[GameManager] Cannot find bullet prefab for remote bullets. " +
                             "Assign m_RemoteBulletPrefab or ensure m_TankPrefab has TankShooting.");
            return null;
        }

        private void UpdateRemoteBullets(TankNet.BulletState[] bullets)
        {
            var prefab = GetBulletPrefab();
            if (prefab == null) return;

            var activeBulletIds = new HashSet<uint>();

            foreach (var bs in bullets)
            {
                // Skip bullets fired by this client — TankShooting already spawned them locally
                if (bs.ownerId == m_MyPlayerId) continue;

                activeBulletIds.Add(bs.bulletId);
                var pos = new Vector3(bs.x, bs.y, bs.z);

                if (!_remoteBullets.TryGetValue(bs.bulletId, out var go) || go == null)
                {
                    go = Instantiate(prefab, pos, Quaternion.identity);

                    // Server is authoritative — disable local physics and damage
                    var rb = go.GetComponent<Rigidbody>();
                    if (rb != null) { rb.isKinematic = true; }
                    var explosion = go.GetComponent<ShellExplosion>();
                    if (explosion != null) explosion.enabled = false;
                    var col = go.GetComponent<Collider>();
                    if (col != null) col.enabled = false;

                    _remoteBullets[bs.bulletId] = go;

                    // Đồng bộ animation bắn cho xe tăng remote khi đạn của nó xuất hiện
                    if (_remoteTanks.TryGetValue(bs.ownerId, out var shooterTank) && shooterTank != null)
                    {
                        var anim = shooterTank.GetComponent<TankAnimation>();
                        if (anim != null) anim.PlayRemoteShoot();

                        var shooting = shooterTank.GetComponent<TankShooting>();
                        if (shooting != null)
                        {
                            Vector3 bulletDir = pos - shooterTank.transform.position;
                            bulletDir.y = 0f;
                            if (bulletDir.sqrMagnitude > 0.001f)
                            {
                                go.transform.rotation = Quaternion.LookRotation(bulletDir.normalized);
                            }
                            shooting.PlayRemoteShoot(bulletDir);
                        }
                    }
                }

                // Cập nhật vị trí và xoay đầu đạn theo hướng bay
                Vector3 oldPos = go.transform.position;
                go.transform.position = pos;
                
                Vector3 moveDir = pos - oldPos;
                if (moveDir.sqrMagnitude > 0.0001f)
                {
                    go.transform.rotation = Quaternion.LookRotation(moveDir.normalized);
                }
            }

            // Remove bullets the server no longer reports (hit something or expired)
            foreach (var id in new List<uint>(_remoteBullets.Keys))
            {
                if (!activeBulletIds.Contains(id))
                {
                    DestroyRemoteBullet(_remoteBullets[id]);
                    _remoteBullets.Remove(id);
                }
            }
        }

        private void HandleForceLogout(ushort code, string message, uint disconnectAfterMs)
        {
            int seconds = disconnectAfterMs > 0 ? (int)(disconnectAfterMs / 1000) : 10;
            if (AuthSessionRuntime.Instance != null)
                AuthSessionRuntime.Instance.HandleForceLogout(code, message, seconds);
        }

        // Plays explosion effect then destroys the bullet GO.
        // Mirrors ShellExplosion.OnTriggerEnter without applying local damage.
        private void DestroyRemoteBullet(GameObject go)
        {
            if (go == null) return;

            // Bullet's last snapshot position lags ~1 snapshot (50ms) behind the actual hit.
            // At 20-30 m/s that is 1-1.5 m of visual offset.
            // If the bullet was close to the local tank, snap the explosion there instead.
            Vector3 explosionPos = go.transform.position;
            const float STRIKE_RANGE = 5f;
            if (m_Tanks.Length > 0 && m_Tanks[0].m_Instance != null)
            {
                float d = Vector3.Distance(explosionPos, m_Tanks[0].m_Instance.transform.position);
                if (d < STRIKE_RANGE)
                    explosionPos = m_Tanks[0].m_Instance.transform.position;
            }

            var exp = go.GetComponent<ShellExplosion>();
            if (exp != null && exp.m_ExplosionParticles != null)
            {
                exp.m_ExplosionParticles.transform.SetParent(null);
                exp.m_ExplosionParticles.transform.position = explosionPos;
                exp.m_ExplosionParticles.Play();
                if (exp.m_ExplosionAudio != null) exp.m_ExplosionAudio.Play();
                Destroy(exp.m_ExplosionParticles.gameObject,
                        exp.m_ExplosionParticles.main.duration);
            }

            Destroy(go);
        }

        private void ApplyLocalCorrection(TankState ts)
        {
            // Update local TankManager stats
            if (m_Tanks.Length > 0)
            {
                m_Tanks[0].m_Score = ts.score;
                m_Tanks[0].m_Placement = ts.placement;
            }

            // Lần đầu nhận snapshot: spawn local tank đúng vị trí server
            if (m_Tanks.Length == 0) return;
            if (m_Tanks[0].m_Instance == null)
            {
                SpawnLocalTankAt(ts);
                return;
            }

            var go = m_Tanks[0].m_Instance;
            var serverPos = new Vector3(ts.x, ts.y, ts.z);
            var serverRot = Quaternion.Euler(0, ts.yaw * Mathf.Rad2Deg, 0);

            var tm = go.GetComponent<Complete.TankMovement>();
            bool customPhysics = tm == null || tm.m_UseCustomOnlinePhysics;
            var rb0 = go.GetComponent<Rigidbody>();

            // Sync Y: truyền Y mục tiêu cho TankMovement để áp dụng trong FixedUpdate
            // (KHÔNG ghi đè rb.position trực tiếp ở Update — sẽ phá vỡ Rigidbody Interpolation)
            if (customPhysics && tm != null)
            {
                tm.m_NetworkTargetY = serverPos.y;
                tm.m_HasNetworkTargetY = true;
            }

            // XZ error = khoảng cách giữa client prediction và server position.
            // Server snapshot trễ hơn client: snapshotInterval + RTT one-way.
            // Với speed=12, snapshot 20Hz, LAN RTT~16ms:
            //   expected xzErr ≈ 12 * (0.05 + 0.008) ≈ 0.7 units khi di chuyển thẳng.
            // Threshold 1.0f dễ bị trigger → teleport. Dùng 3 mức:
            //   < SOFT_THRESHOLD : bình thường, không sửa
            //   SOFT..HARD       : soft lerp — tank trôi nhẹ về server pos, không teleport
            //   > HARD_THRESHOLD : hard snap — death/respawn/wall push mạnh
            const float SOFT_THRESHOLD = 1.5f;
            const float HARD_THRESHOLD = 4.0f;
            const float LERP_SPEED     = 5.0f;  // units/s correction khi lỗi vừa

            // Bù trừ độ trễ Ping (Latency compensation) cho vị trí
            float pingSec = TankNetClient.Instance != null && TankNetClient.Instance.PingMs > 0 
                            ? TankNetClient.Instance.PingMs / 1000f : 0f;
            
            Vector3 expectedServerPos = serverPos;
            if (tm != null && pingSec > 0f)
            {
                // Ước tính vị trí thực sự của server hiện tại dựa trên input đang giữ
                float mz = Input.GetAxis("Vertical" + m_Tanks[0].m_PlayerNumber);
                if (Mathf.Abs(mz) > 0.1f) mz = mz > 0 ? 1 : -1;
                else mz = 0;
                expectedServerPos += go.transform.forward * mz * tm.m_Speed * (pingSec / 2f);
            }

            float xzErr = new Vector2(
                go.transform.position.x - expectedServerPos.x,
                go.transform.position.z - expectedServerPos.z).magnitude;

            // Xử lý góc quay: nội suy nhẹ về góc quay của Server (Server là nguồn chân lý)
            float angleErr = Quaternion.Angle(go.transform.rotation, serverRot);

            if (xzErr > HARD_THRESHOLD)
            {
                // Lỗi lớn: death, respawn, server push — snap tức thời
                var rb = go.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.position = serverPos; // Hard snap thì dùng position luôn cũng được
                    if (!rb.isKinematic)
                    {
                        rb.velocity = Vector3.zero;
                        rb.angularVelocity = Vector3.zero;
                    }
                }
                else go.transform.position = serverPos;
                go.transform.rotation = serverRot;
                
                if (tm != null) tm.m_HasNetworkCorrection = false;
            }
            else if (xzErr > SOFT_THRESHOLD || angleErr > 1f)
            {
                // Truyền vị trí/góc quay kỳ vọng cho TankMovement để nó tự nội suy mượt trong FixedUpdate
                if (tm != null)
                {
                    tm.m_HasNetworkCorrection = true;
                    tm.m_NetworkTargetPosition = expectedServerPos;
                    tm.m_NetworkTargetRotation = serverRot;
                }
            }
            else
            {
                if (tm != null) tm.m_HasNetworkCorrection = false;
            }

            // Sync health bar and trigger death sequence when server reports dead
            var localHealth = go.GetComponent<TankHealth>();
            if (localHealth != null) localHealth.SyncFromServer(ts.health);
            else if (!ts.IsAlive) go.SetActive(false);

            // Forward server InBush flag to local TankStealth (server is authoritative backup)
            var localStealth = go.GetComponent<TankStealth>();
            if (localStealth != null) localStealth.ServerInBush = ts.IsInBush;
        }

        private void UpdateRemoteTank(TankState ts)
        {
            if (!_remoteTanks.TryGetValue(ts.tankId, out var go) || go == null)
            {
                // Unpack the tank type index from flags (bits 2-7)
                int typeIndex = (ts.flags >> 2) & 0x3F;
                GameObject prefabToSpawn = m_RemoteTankPrefab;
                if (m_TankPrefabMappings != null)
                {
                    foreach (var mapping in m_TankPrefabMappings)
                    {
                        if (mapping.typeIndex == typeIndex && mapping.prefab != null)
                        {
                            prefabToSpawn = mapping.prefab;
                            break;
                        }
                    }
                }

                go = Instantiate(prefabToSpawn,
                    new Vector3(ts.x, ts.y, ts.z),
                    Quaternion.Euler(0, ts.yaw * Mathf.Rad2Deg, 0));
                go.SetActive(true);
                _remoteTanks[ts.tankId] = go;

                var remoteRb = go.GetComponent<Rigidbody>();
                if (remoteRb != null) 
                { 
                    remoteRb.isKinematic = true; 
                    remoteRb.useGravity = false;
                    remoteRb.interpolation = RigidbodyInterpolation.None;
                }

                AddBulletHitTrigger(go);
                
                var tm = go.GetComponent<Complete.TankMovement>();
                bool customPhysics = tm == null || tm.m_UseCustomOnlinePhysics;
                if (customPhysics) DisablePhysicsColliders(go);

                // Remote tank is driven purely by snapshots — disable all local input components
                // so they don't read keyboard input or send packets to the server.
                var remoteShooting = go.GetComponent<TankShooting>();
                if (remoteShooting != null) remoteShooting.m_IsLocalPlayer = false;
                var remoteMovement = go.GetComponent<TankMovement>();
                if (remoteMovement != null) remoteMovement.enabled = false;

                // Bush stealth for remote tank (server-driven)
                var remoteStealth = go.GetComponent<TankStealth>()
                                 ?? go.AddComponent<TankStealth>();
                remoteStealth.IsLocalTank = false;
            }
            
            // Find remote TankManager to sync score/placement
            for (int i = 1; i < m_Tanks.Length; i++)
            {
                if (m_Tanks[i].m_PlayerNumber == ts.tankId)
                {
                    m_Tanks[i].m_Score = ts.score;
                    m_Tanks[i].m_Placement = ts.placement;
                    break;
                }
            }

            bool wasAlive = go.activeSelf;

            if (ts.IsAlive)
            {
                go.SetActive(true);
                var health = go.GetComponent<TankHealth>();
                if (health != null) health.SyncFromServer(ts.health);

                // Forward server InBush flag to remote TankStealth
                var stealth = go.GetComponent<TankStealth>();
                if (stealth != null)
                {
                    stealth.ServerInBush = ts.IsInBush;
                }

                if (!_remoteSnaps.ContainsKey(ts.tankId))
                    _remoteSnaps[ts.tankId] = new List<SnapEntry>();
                PushSnap(_remoteSnaps[ts.tankId], ts);
            }
            else if (wasAlive)
            {
                // Trigger full death sequence (explosion + audio) instead of raw SetActive(false)
                var health = go.GetComponent<TankHealth>();
                if (health != null) health.SyncFromServer(0f);
                else                go.SetActive(false);
            }
        }

        private void PushSnap(List<SnapEntry> buf, TankState ts)
        {
            buf.Add(new SnapEntry
            {
                t   = Time.time,
                pos = new Vector3(ts.x, ts.y, ts.z),
                rot = Quaternion.Euler(0, ts.yaw * Mathf.Rad2Deg, 0),
                turretYaw = ts.turretYaw
            });
            if (buf.Count > SNAP_BUFFER) buf.RemoveAt(0);
        }

        private void DespawnRemote(uint tankId)
        {
            if (!_remoteTanks.TryGetValue(tankId, out var go)) return;
            Destroy(go);
            _remoteTanks.Remove(tankId);
            _remoteSnaps.Remove(tankId);
        }


        private void EnableTankControl()
        {
            for (int i = 0; i < m_Tanks.Length; i++)
            {
                if (m_Tanks[i].m_Instance != null)
                    m_Tanks[i].EnableControl();
            }
        }


        private void DisableTankControl()
        {
            for (int i = 0; i < m_Tanks.Length; i++)
            {
                if (m_Tanks[i].m_Instance != null)
                    m_Tanks[i].DisableControl();
            }
        }

        // ── Match end detection (online mode) ────────────────────────────────

        private void CheckMatchEnd(SnapshotData snap)
        {
            // Track alive status changes to count kills/deaths
            foreach (var ts in snap.Tanks)
            {
                if (!_prevAliveStatus.ContainsKey(ts.tankId))
                {
                    // First time seeing this tank
                    _prevAliveStatus[ts.tankId] = ts.IsAlive;
                    _totalPlayersSeen++;
                    // Record opponent ID (anyone who isn't us)
                    if (ts.tankId != m_MyPlayerId && ts.tankId != 0)
                        _opponentId = ts.tankId.ToString();
                }
                else
                {
                    bool wasAlive = _prevAliveStatus[ts.tankId];
                    if (wasAlive && !ts.IsAlive)
                    {
                        if (ts.tankId == m_MyPlayerId) _myDeaths++;
                        else                           _myKills++;
                    }
                    _prevAliveStatus[ts.tankId] = ts.IsAlive;
                }
            }

            // Wait until we've seen all players before judging
            if (_totalPlayersSeen < 2) return;

            int alivePlayers   = 0;
            foreach (var ts in snap.Tanks)
            {
                if (ts.IsAlive) alivePlayers++;
            }
        }

        private void HandleMatchEnd(MatchEndData end)
        {
            if (_matchEnded) return;
            _matchEnded = true;

            bool won = (end.Outcome == 0); // 0=win, 1=lose, 2=draw, 3=timeout
            bool draw = (end.Outcome == 2);
            StartCoroutine(TriggerMatchEnd(won, draw, end));
        }

        private IEnumerator TriggerMatchEnd(bool won, bool draw, MatchEndData end = null)
        {
            DisableTankControl();
            TankNetClient.Instance?.Disconnect();

            // Hiển thị màn hình trung gian (Victory / You Died)
            if (GameUIManager.Instance != null)
            {
                if (won) GameUIManager.Instance.ShowVictoryScreen(true);
                else GameUIManager.Instance.ShowYouDiedScreen(true);
            }

            _skipEndDelay = false;
            float waitTimer = 30f;
            while (waitTimer > 0f && !_skipEndDelay)
            {
                waitTimer -= Time.deltaTime;
                yield return null;
            }

            // Tắt màn hình trung gian trước khi bật bảng kết quả
            if (GameUIManager.Instance != null)
            {
                GameUIManager.Instance.ShowVictoryScreen(false);
                GameUIManager.Instance.ShowYouDiedScreen(false);
            }

            int durationSecs = Mathf.Max(1, (int)(Time.time - _matchStartTime));

            // Show match end panel
            if (GameUIManager.Instance != null)
            {
                GameUIManager.Instance.ShowMatchEndPanel(won, draw, _myKills, _myDeaths, durationSecs, end);
            }

            // Save match to history service
            MatchHistoryUIManager.SaveMatchResult(
                caller:      this,
                matchId:     (long)m_MatchId,
                opponentId:  _opponentId,
                won:         won,
                draw:        draw,
                kills:       _myKills,
                deaths:      _myDeaths,
                durationSecs: durationSecs
            );

            // Không tự động chuyển cảnh nữa, chờ người chơi tương tác với các nút trên Panel kết thúc
            yield return null;
        }

        /// <summary>
        /// Được gọi khi người chơi bấm nút "Main Menu" trên Panel kết thúc trận đấu.
        /// </summary>
        public void OnMainMenuClicked()
        {
            GlobalMatchState.Clear();
            SceneManager.LoadScene(
                string.IsNullOrEmpty(lobbySceneName) ? "Lobby" : lobbySceneName
            );
        }

        /// <summary>
        /// Được gọi khi người chơi bấm nút "Tìm trận mới" trên Panel kết thúc trận đấu.
        /// </summary>
        public void OnFindNewMatchClicked()
        {
            GlobalMatchState.Clear();
            GlobalMatchState.AutoMatchmake = true; // Kích hoạt cờ tự động tìm trận
            SceneManager.LoadScene(
                string.IsNullOrEmpty(lobbySceneName) ? "Lobby" : lobbySceneName
            );
        }

        /// <summary>
        /// Được gọi bởi nút Skip trên màn hình Victory/You Died để xem bảng kết quả ngay lập tức.
        /// </summary>
        public void SkipEndDelay()
        {
            _skipEndDelay = true;
        }
    }
}