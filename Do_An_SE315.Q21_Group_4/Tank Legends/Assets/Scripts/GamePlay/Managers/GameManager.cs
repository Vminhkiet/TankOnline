using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using TankNet;

namespace Complete
{
    public class GameManager : MonoBehaviour
    {
        public int m_NumRoundsToWin = 5;            // The number of rounds a single player has to win to win the game.
        public float m_StartDelay = 3f;             // The delay between the start of RoundStarting and RoundPlaying phases.
        public float m_EndDelay = 3f;               // The delay between the end of RoundPlaying and RoundEnding phases.
        public CameraControl m_CameraControl;       // Reference to the CameraControl script for control during different phases.
        public Text m_MessageText;                  // Reference to the overlay Text to display winning text, etc.
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

        [Header("Match End UI")]
        public GameObject     matchEndPanel;
        public TextMeshProUGUI matchEndResultText;
        public TextMeshProUGUI matchEndStatsText;
        public TextMeshProUGUI matchEndRpText;
        public TextMeshProUGUI matchEndCoinText;
        public MatchEndLeaderboardUIManager leaderboardUI;
        public string          lobbySceneName = "Lobby";

        [Header("Match HUD")]
        public TextMeshProUGUI matchTimerText;

        // ── Match tracking (online) ───────────────────────────────────────────
        private float   _matchStartTime;
        private bool    _matchEnded;
        private int     _myKills;
        private int     _myDeaths;
        private string  _opponentId = "bot-1";
        private int     _totalPlayersSeen;
        private float   _serverTimeRemaining = -1f;
        private readonly Dictionary<uint, bool> _prevAliveStatus = new();

        private readonly Dictionary<uint, GameObject> _remoteTanks   = new();
        private readonly Dictionary<uint, GameObject> _remoteBullets = new();

        // ── Snapshot interpolation ───────────────────────────────────────────
        private struct SnapEntry { public float t; public Vector3 pos; public Quaternion rot; }

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
            if (matchEndPanel != null) matchEndPanel.SetActive(false);

            // Read MatchInfo from GlobalMatchState if available
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
                    ApplyInterp(buf, kvp.Value.transform, renderTime);
            }

            // Update match timer UI
            if (m_OnlineMode && matchTimerText != null && _serverTimeRemaining >= 0f)
            {
                int totalSecs = Mathf.CeilToInt(_serverTimeRemaining);
                int minutes   = totalSecs / 60;
                int seconds   = totalSecs % 60;
                matchTimerText.text = string.Format("{0:00}:{1:00}", minutes, seconds);
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

            if (prev == -1)      // all entries are in the future — show oldest
            { tr.position = buf[0].pos; tr.rotation = buf[0].rot; }
            else if (next == -1) // all entries are in the past — show newest
            { tr.position = buf[prev].pos; tr.rotation = buf[prev].rot; }
            else                 // interpolate between prev and next
            {
                float span = buf[next].t - buf[prev].t;
                float f    = span > 0f ? (renderTime - buf[prev].t) / span : 1f;
                tr.position = Vector3.Lerp(buf[prev].pos, buf[next].pos, f);
                tr.rotation = Quaternion.Slerp(buf[prev].rot, buf[next].rot, f);
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
                m_Tanks[i].m_PlayerNumber = i + 1;
                m_Tanks[i].Setup();
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

            m_Tanks[0].m_Instance    = Instantiate(m_TankPrefab, pos, rot) as GameObject;
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

            Debug.Log($"[GameManager] local tank spawned tại server pos {pos}");
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
            // As soon as the round starts reset the tanks and make sure they can't move.
            ResetAllTanks ();
            DisableTankControl ();

            // Snap the camera's zoom and position to something appropriate for the reset tanks.
            m_CameraControl.SetStartPositionAndSize ();

            // Increment the round number and display text showing the players what round it is.
            m_RoundNumber++;
            m_MessageText.text = "ROUND " + m_RoundNumber;

            // Wait for the specified length of time until yielding control back to the game loop.
            yield return m_StartWait;
        }


        private IEnumerator RoundPlaying ()
        {
            // As soon as the round begins playing let the players control the tanks.
            EnableTankControl ();

            // Clear the text from the screen.
            m_MessageText.text = string.Empty;

            // While there is not one tank left...
            while (!OneTankLeft())
            {
                // ... return on the next frame.
                yield return null;
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
            string message = EndMessage ();
            m_MessageText.text = message;

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


        // Returns a string message to display at the end of each round.
        private string EndMessage()
        {
            if (m_OnlineMode)
            {
                // In online mode, we act as a final leaderboard based on score and placement
                string message = "MATCH ENDED\n\nLEADERBOARD:\n\n";
                
                var sortedTanks = new List<TankManager>();
                for (int i = 0; i < m_Tanks.Length; i++)
                {
                    if (m_Tanks[i].m_Instance != null && m_Tanks[i].m_Instance.activeSelf || m_Tanks[i].m_Placement > 0)
                    {
                        sortedTanks.Add(m_Tanks[i]);
                    }
                }
                
                sortedTanks.Sort((a, b) => {
                    int pCmp = a.m_Placement.CompareTo(b.m_Placement);
                    if (pCmp != 0) return pCmp;
                    return b.m_Score.CompareTo(a.m_Score); // Higher score wins tiebreaker
                });

                foreach (var t in sortedTanks)
                {
                    message += $"{t.m_ColoredPlayerText} - Rank #{t.m_Placement} - {t.m_Score} PTS\n";
                }
                return message;
            }

            // By default when a round ends there are no winners so the default end message is a draw.
            string offlineMessage = "DRAW!";

            // If there is a winner then change the message to reflect that.
            if (m_RoundWinner != null)
                offlineMessage = m_RoundWinner.m_ColoredPlayerText + " WINS THE ROUND!";

            // Add some line breaks after the initial message.
            offlineMessage += "\n\n\n\n";

            // Go through all the tanks and add each of their scores to the message.
            for (int i = 0; i < m_Tanks.Length; i++)
            {
                offlineMessage += m_Tanks[i].m_ColoredPlayerText + ": " + m_Tanks[i].m_Wins + " WINS\n";
            }

            // If there is a game winner, change the entire message to reflect that.
            if (m_GameWinner != null)
                offlineMessage = m_GameWinner.m_ColoredPlayerText + " WINS THE GAME!";

            return offlineMessage;
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
                    if (rb != null) { rb.isKinematic = true; rb.velocity = Vector3.zero; }
                    var explosion = go.GetComponent<ShellExplosion>();
                    if (explosion != null) explosion.enabled = false;
                    var col = go.GetComponent<Collider>();
                    if (col != null) col.enabled = false;

                    _remoteBullets[bs.bulletId] = go;
                }

                go.transform.position = pos;
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

            // Always sync Y if using custom online physics (client has no terrain height simulation).
            if (customPhysics)
            {
                if (rb0 != null)
                {
                    var p = rb0.position; p.y = serverPos.y; rb0.position = p;
                }
                else
                {
                    var pos = go.transform.position; pos.y = serverPos.y;
                    go.transform.position = pos;
                }
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

            float xzErr = new Vector2(
                go.transform.position.x - serverPos.x,
                go.transform.position.z - serverPos.z).magnitude;

            if (xzErr > HARD_THRESHOLD)
            {
                // Lỗi lớn: death, respawn, server push — snap tức thời
                var rb = go.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.position = serverPos;
                    rb.velocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
                else go.transform.position = serverPos;
                go.transform.rotation = serverRot;
            }
            else if (xzErr > SOFT_THRESHOLD)
            {
                // Lỗi vừa: drift từ latency/timer spike — lerp nhẹ, không gây teleport
                var rb = go.GetComponent<Rigidbody>();
                float step = LERP_SPEED * Time.deltaTime;
                Vector3 corrected = Vector3.MoveTowards(
                    go.transform.position,
                    new Vector3(serverPos.x, go.transform.position.y, serverPos.z),
                    step);
                if (rb != null) rb.position = corrected;
                else go.transform.position = corrected;
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
                if (m_RemoteTankPrefab == null)
                {
                    Debug.LogError("[GameManager] m_RemoteTankPrefab chưa được gán. Kéo prefab vào Inspector.");
                    return;
                }
                go = Instantiate(m_RemoteTankPrefab,
                    new Vector3(ts.x, ts.y, ts.z),
                    Quaternion.Euler(0, ts.yaw * Mathf.Rad2Deg, 0));
                _remoteTanks[ts.tankId] = go;

                var remoteRb = go.GetComponent<Rigidbody>();
                if (remoteRb != null) { remoteRb.isKinematic = true; remoteRb.useGravity = false; }

                AddBulletHitTrigger(go);
                
                var tm = go.GetComponent<Complete.TankMovement>();
                bool customPhysics = tm == null || tm.m_UseCustomOnlinePhysics;
                if (customPhysics) DisablePhysicsColliders(go);

                // Remote tank is driven purely by snapshots — disable all local input components
                // so they don't read keyboard input or send packets to the server.
                var remoteShooting = go.GetComponent<TankShooting>();
                if (remoteShooting != null) remoteShooting.enabled = false;
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
                if (stealth != null) stealth.ServerInBush = ts.IsInBush;

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
                rot = Quaternion.Euler(0, ts.yaw * Mathf.Rad2Deg, 0)
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
            bool myPlayerAlive = false;
            foreach (var ts in snap.Tanks)
            {
                if (ts.IsAlive) alivePlayers++;
                if (ts.tankId == m_MyPlayerId && ts.IsAlive) myPlayerAlive = true;
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

            int durationSecs = Mathf.Max(1, (int)(Time.time - _matchStartTime));

            // Show match end panel
            if (matchEndPanel != null) matchEndPanel.SetActive(true);

            string resultText = draw ? "DRAW!" : (won ? "YOU WIN!" : "YOU LOSE!");
            if (matchEndResultText != null)
                matchEndResultText.text = resultText;

            if (matchEndStatsText != null)
            {
                if (end != null)
                {
                    matchEndStatsText.text =
                        $"Kills: {_myKills}   Deaths: {_myDeaths}\n" +
                        $"Duration: {durationSecs / 60}:{durationSecs % 60:00}\n" +
                        $"Rank: #{end.Placement}";
                }
                else
                {
                    matchEndStatsText.text =
                        $"Kills: {_myKills}   Deaths: {_myDeaths}\n" +
                        $"Duration: {durationSecs / 60}:{durationSecs % 60:00}";
                }
            }

            if (end != null)
            {
                if (matchEndRpText != null)
                    matchEndRpText.text = $"{(end.RpReward > 0 ? "+" : "")}{end.RpReward}";
                    
                if (matchEndCoinText != null)
                    matchEndCoinText.text = "+25";

                if (leaderboardUI != null)
                    leaderboardUI.BuildLeaderboard(end.Players);
            }
            else
            {
                if (matchEndRpText != null) matchEndRpText.text = "+0";
                if (matchEndCoinText != null) matchEndCoinText.text = "+25";
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
    }
}