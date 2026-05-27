using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

/// <summary>
/// Discovers the game server on the LAN via UDP broadcast.
/// Sends "TANK_DISCOVER" to 255.255.255.255:8888 and listens for "TANK_SERVER:{port}" replies.
/// Results are stored in PlayerPrefs for GameApiClient to pick up.
/// </summary>
public static class ServerDiscovery
{
    private const int DISCOVERY_PORT = 8888;
    private const string DISCOVER_MSG = "TANK_DISCOVER";
    private const string RESPONSE_PREFIX = "TANK_SERVER:";
    private const float TIMEOUT_SECONDS = 2f;

    public const string PrefKeyDiscoveredIp = "discovered_server_ip";
    public const string PrefKeyDiscoveredPort = "discovered_server_port";

    /// <summary>
    /// Discovered server IP (null if not yet discovered).
    /// </summary>
    public static string DiscoveredIp { get; private set; }

    /// <summary>
    /// Discovered server port (default 8080).
    /// </summary>
    public static int DiscoveredPort { get; private set; } = 8080;

    /// <summary>
    /// True while a discovery scan is in progress.
    /// </summary>
    public static bool IsSearching { get; private set; }

    /// <summary>
    /// Try to discover a server on the LAN. Call from a coroutine or background thread.
    /// This method is blocking and should be called from a background thread.
    /// </summary>
    /// <returns>True if a server was found.</returns>
    public static bool Discover()
    {
        IsSearching = true;
        DiscoveredIp = null;
        DiscoveredPort = 8080;

        try
        {
            using (var udp = new UdpClient())
            {
                // Enable broadcast
                udp.EnableBroadcast = true;

                // Set receive timeout
                udp.Client.ReceiveTimeout = (int)(TIMEOUT_SECONDS * 1000);

                // Send discovery broadcast
                byte[] sendData = Encoding.UTF8.GetBytes(DISCOVER_MSG);
                udp.Send(sendData, sendData.Length, new IPEndPoint(IPAddress.Broadcast, DISCOVERY_PORT));
                Debug.Log("[ServerDiscovery] Sent broadcast TANK_DISCOVER to 255.255.255.255:" + DISCOVERY_PORT);

                // Wait for reply
                IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
                byte[] recvData = udp.Receive(ref remoteEP);
                string response = Encoding.UTF8.GetString(recvData);

                if (response.StartsWith(RESPONSE_PREFIX))
                {
                    string portStr = response.Substring(RESPONSE_PREFIX.Length);
                    int port = 8080;
                    int.TryParse(portStr, out port);

                    DiscoveredIp = remoteEP.Address.ToString();
                    DiscoveredPort = port;

                    Debug.Log($"[ServerDiscovery] Found server at {DiscoveredIp}:{DiscoveredPort}");
                    return true;
                }
                else
                {
                    Debug.LogWarning($"[ServerDiscovery] Unexpected reply: {response}");
                }
            }
        }
        catch (SocketException ex)
        {
            Debug.Log($"[ServerDiscovery] No server found (timeout or error): {ex.Message}");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ServerDiscovery] Error: {ex.Message}");
        }
        finally
        {
            IsSearching = false;
        }

        return false;
    }

    /// <summary>
    /// Runs discovery on a background thread and saves results to PlayerPrefs.
    /// Safe to call from the main thread.
    /// </summary>
    public static void DiscoverAsync(Action<bool> onComplete = null)
    {
        new Thread(() =>
        {
            bool found = Discover();

            if (found)
            {
                Debug.Log($"[ServerDiscovery] Async: server found at {DiscoveredIp}:{DiscoveredPort}");
            }

            // Callback on the calling context (note: not main thread!)
            onComplete?.Invoke(found);
        })
        {
            IsBackground = true,
            Name = "ServerDiscovery"
        }.Start();
    }

    /// <summary>
    /// Auto-discover server when game starts if mode is "lan_auto".
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void AutoDiscover()
    {
        string mode = PlayerPrefs.GetString(GameApiClient.ConnectionModePrefKey, "lan_auto");
        if (mode != "lan_auto") return;

        Debug.Log("[ServerDiscovery] Auto-discovery started (mode=lan_auto)");
        DiscoverAsync(found =>
        {
            if (found)
                Debug.Log($"[ServerDiscovery] Auto-discovery complete: {DiscoveredIp}:{DiscoveredPort}");
            else
                Debug.Log("[ServerDiscovery] Auto-discovery: no server found, will use localhost as fallback");
        });
    }
}
