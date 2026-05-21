using UnityEngine;
using UnityEngine.Networking;
using TMPro;
using System.Collections;

/// <summary>
/// UI Manager cho tính năng nhập Gift Code.
/// Gắn vào scene có InputField để nhập code + Button submit + Text hiển thị kết quả.
/// </summary>
public class RedeemCodeUIManager : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("InputField để người chơi nhập code")]
    public TMP_InputField codeInput;

    [Tooltip("Text hiển thị kết quả (thành công / lỗi)")]
    public TextMeshProUGUI resultText;

    [Tooltip("Button submit (để disable khi đang gửi)")]
    public UnityEngine.UI.Button submitButton;

    [Header("API")]
    public string redeemPath = "/api/profile/giftcode/redeem";

    private bool isBusy = false;

    // ── Response DTO khớp với server RedeemCodeResponse ──────────────────────
    [System.Serializable]
    private class RedeemCodeResponseData
    {
        public bool   success;
        public string message;
        public long   coinsEarned;
        public string itemEarned;
        public long   totalCoins;
    }

    // ── Request DTO khớp với server RedeemCodeRequest ────────────────────────
    [System.Serializable]
    private class RedeemCodeRequestData
    {
        public string code;
    }

    // ── Unity Lifecycle ─────────────────────────────────────────────────────

    private void Start()
    {
        ClearResult();
    }

    // ── Public: gắn vào Button OnClick ──────────────────────────────────────

    public void OnRedeemButtonClicked()
    {
        if (isBusy) return;

        if (codeInput == null)
        {
            ShowResult("Chưa gán Code Input trong Inspector!", false);
            return;
        }

        string code = codeInput.text.Trim();
        if (string.IsNullOrEmpty(code))
        {
            ShowResult("Vui lòng nhập mã code.", false);
            return;
        }

        if (!GameApiClient.HasJwt())
        {
            ShowResult("Bạn chưa đăng nhập!", false);
            return;
        }

        StartCoroutine(RedeemCoroutine(code));
    }

    // ── Coroutine gửi request ───────────────────────────────────────────────

    private IEnumerator RedeemCoroutine(string code)
    {
        isBusy = true;
        SetButtonInteractable(false);
        ShowResult("Đang xử lý...", false);

        string json = JsonUtility.ToJson(new RedeemCodeRequestData { code = code });

        Debug.Log($"[RedeemCode] Sending redeem request | code={code} | url={GameApiClient.BuildUrl(redeemPath)}");

        using (UnityWebRequest req = GameApiClient.CreateRequest(redeemPath, "POST", json))
        {
            GameApiClient.ApiCallResult apiResult = default;
            yield return GameApiClient.Send(req, r => apiResult = r);

            Debug.Log($"[RedeemCode] Response | status={apiResult.StatusCode} | body={apiResult.Body}");

            if (apiResult.Success)
            {
                try
                {
                    RedeemCodeResponseData res = JsonUtility.FromJson<RedeemCodeResponseData>(apiResult.Body);

                    if (res.success)
                    {
                        string msg = res.message;
                        if (res.coinsEarned > 0)
                            msg += $"\n+{res.coinsEarned} coins";
                        if (!string.IsNullOrEmpty(res.itemEarned))
                            msg += $"\nNhận: {res.itemEarned}";
                        msg += $"\nTổng coins: {res.totalCoins}";

                        ShowResult(msg, true);
                        codeInput.text = "";

                        // ── Optimistic UI update ────────────────────
                        // Cập nhật coins ngay lập tức trên ProfileUI
                        if (ProfileUIManager.Instance != null)
                        {
                            ProfileUIManager.Instance.UpdateCoinsImmediate(res.totalCoins);
                            // Refresh ngầm từ server để đồng bộ toàn bộ profile
                            ProfileUIManager.Instance.Refresh();
                        }
                    }
                    else
                    {
                        ShowResult(res.message, false);
                    }
                }
                catch (System.Exception e)
                {
                    ShowResult("Lỗi parse response: " + e.Message, false);
                }
            }
            else
            {
                // Server trả lỗi (400, 401, 403, etc.)
                if (apiResult.StatusCode == 400)
                {
                    // Thử parse error body từ server
                    try
                    {
                        RedeemCodeResponseData errRes = JsonUtility.FromJson<RedeemCodeResponseData>(apiResult.Body);
                        ShowResult(errRes.message ?? "Code không hợp lệ.", false);
                    }
                    catch
                    {
                        ShowResult("Code không hợp lệ.", false);
                    }
                }
                else if (apiResult.StatusCode == 401 || apiResult.StatusCode == 403)
                {
                    ShowResult("Bạn chưa đăng nhập hoặc phiên đã hết hạn.", false);
                }
                else
                {
                    ShowResult("Lỗi kết nối: " + apiResult.Error, false);
                }
            }
        }

        isBusy = false;
        SetButtonInteractable(true);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private void ShowResult(string msg, bool isSuccess)
    {
        if (resultText != null)
        {
            resultText.text = msg;
            resultText.color = isSuccess
                ? new Color(0.2f, 0.8f, 0.2f)   // xanh lá
                : new Color(0.9f, 0.3f, 0.3f);   // đỏ
        }
        else
        {
            Debug.Log($"[RedeemCode] {msg}");
        }
    }

    private void ClearResult()
    {
        if (resultText != null) resultText.text = "";
    }

    private void SetButtonInteractable(bool interactable)
    {
        if (submitButton != null) submitButton.interactable = interactable;
    }
}
