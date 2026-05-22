using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

/// <summary>
/// Loads profile page data:
/// - email, username: GET /api/user/me (auth DB)
/// - display name, avatar preset, coins: GET /api/profile/me
/// </summary>
public class ProfileUIManager : MonoBehaviour
{
    // ── Singleton accessor ──────────────────────────────────────────────────
    public static ProfileUIManager Instance { get; private set; }
    private const string ProfileMePath = "/api/profile/me";
    private const string UserMePath = "/api/user/me";

    [Header("UI")]
    [SerializeField] private TextMeshProUGUI emailText;
    [SerializeField] private TextMeshProUGUI usernameText;
    [Tooltip("Các Text khác hiển thị cùng tên đăng nhập (PlayerPrefs).")]
    [SerializeField] private TextMeshProUGUI[] extraUsernameTexts;
    [SerializeField] private TextMeshProUGUI displayNameText;
    [Tooltip("Các Text khác hiển thị cùng display name từ profile API.")]
    [SerializeField] private TextMeshProUGUI[] extraDisplayNameTexts;
    [SerializeField] private TextMeshProUGUI coinsText;
    [SerializeField] private TextMeshProUGUI rpText;
    [SerializeField] private Image avatarImage;
    [Tooltip("Các Image khác hiển thị cùng avatar (ví dụ header + card).")]
    [SerializeField] private Image[] extraAvatarImages;
    [SerializeField] private TextMeshProUGUI errorText;

    [Header("Rank UI")]
    [SerializeField] private Image rankIconImage;
    [SerializeField] private TextMeshProUGUI rankNameText;
    
    [Header("Rank Sprites")]
    [SerializeField] private Sprite bronzeSprite;
    [SerializeField] private Sprite silverSprite;
    [SerializeField] private Sprite goldSprite;
    [SerializeField] private Sprite platinumSprite;
    [SerializeField] private Sprite diamondSprite;

    [Header("Avatar presets")]
    [SerializeField] private AvatarCatalog avatarCatalog;

    [Header("Behaviour")]
    [SerializeField] private bool loadOnEnable = true;

    public ProfileResponseData CurrentProfile { get; private set; }

    public event Action<ProfileResponseData> ProfileLoaded;
    public event Action<string> ProfileLoadFailed;

    private void OnEnable()
    {
        Instance = this;
        if (loadOnEnable)
            Refresh();
    }

    private void OnDisable()
    {
        if (Instance == this)
            Instance = null;
    }

    public void Refresh()
    {
        BindCachedSession();
        StartCoroutine(LoadAllCoroutine());
    }

    private void BindCachedSession()
    {
        string email = PlayerPrefs.GetString(GameApiClient.EmailKey, "");
        string username = PlayerPrefs.GetString(GameApiClient.UsernameKey, "");
        BindAccount(email, username);
    }

    private IEnumerator LoadAllCoroutine()
    {
        if (!GameApiClient.HasJwt())
        {
            Fail("Chưa đăng nhập.");
            yield break;
        }

        yield return LoadUserMeCoroutine();
        yield return LoadProfileCoroutine();
    }

    private IEnumerator LoadUserMeCoroutine()
    {
        using var req = GameApiClient.CreateRequest(UserMePath, UnityWebRequest.kHttpVerbGET);
        GameApiClient.ApiCallResult result = default;
        yield return GameApiClient.Send(req, r => result = r);

        if (!result.Success)
        {
            Debug.LogWarning("[Profile] Không tải được email/username: " + result.ErrorMessage);
            yield break;
        }

        UserMeResponseData account;
        try
        {
            account = JsonUtility.FromJson<UserMeResponseData>(result.Body);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[Profile] Parse user/me failed: " + ex.Message);
            yield break;
        }

        if (account == null)
            yield break;

        if (!string.IsNullOrEmpty(account.email))
            PlayerPrefs.SetString(GameApiClient.EmailKey, account.email);
        if (!string.IsNullOrEmpty(account.username))
            PlayerPrefs.SetString(GameApiClient.UsernameKey, account.username);
        PlayerPrefs.Save();

        BindAccount(account.email, account.username);
    }

    private void BindAccount(string email, string username)
    {
        if (emailText != null)
            emailText.text = string.IsNullOrEmpty(email) ? "—" : email;

        ApplyUsernameText(string.IsNullOrEmpty(username) ? "—" : username);
    }

    private IEnumerator LoadProfileCoroutine()
    {
        using var req = GameApiClient.CreateRequest(ProfileMePath, UnityWebRequest.kHttpVerbGET);
        GameApiClient.ApiCallResult result = default;

        yield return GameApiClient.Send(req, r => result = r);

        if (!result.Success)
        {
            Fail(result.ErrorMessage ?? "Không tải được profile.");
            yield break;
        }

        ProfileResponseData profile;
        try
        {
            profile = JsonUtility.FromJson<ProfileResponseData>(result.Body);
        }
        catch (Exception ex)
        {
            Fail("Lỗi parse profile: " + ex.Message);
            yield break;
        }

        if (profile == null || string.IsNullOrEmpty(profile.userId))
        {
            Fail("Profile trống hoặc chưa được tạo trên server.");
            yield break;
        }

        CurrentProfile = profile;
        BindProfile(profile);
        ClearError();
        ProfileLoaded?.Invoke(profile);
    }

    private void BindProfile(ProfileResponseData profile)
    {
        string displayName = string.IsNullOrEmpty(profile.displayName) ? "—" : profile.displayName;
        ApplyDisplayNameText(displayName);

        if (coinsText != null)
            coinsText.text = profile.coins.ToString("N0");
            
        if (rpText != null)
            rpText.text = profile.rp.ToString("N0");

        UpdateRankUI(profile.rp);

        if (avatarCatalog != null)
        {
            Sprite sprite = avatarCatalog.GetSprite(profile.imageId);
            ApplyAvatarSprite(sprite);
        }
    }

    private void UpdateRankUI(int rp)
    {
        string rankName = "Bronze";
        Sprite rankSprite = bronzeSprite;

        if (rp >= 5500)
        {
            rankName = "Diamond";
            rankSprite = diamondSprite;
        }
        else if (rp >= 3500)
        {
            rankName = "Platinum";
            rankSprite = platinumSprite;
        }
        else if (rp >= 2000)
        {
            rankName = "Gold";
            rankSprite = goldSprite;
        }
        else if (rp >= 1000)
        {
            rankName = "Silver";
            rankSprite = silverSprite;
        }

        if (rankNameText != null)
            rankNameText.text = rankName;

        if (rankIconImage != null)
        {
            rankIconImage.sprite = rankSprite;
            rankIconImage.enabled = rankSprite != null;
        }
    }

    private void ApplyUsernameText(string text)
    {
        ApplyTextToAll(usernameText, extraUsernameTexts, text);
    }

    private void ApplyDisplayNameText(string text)
    {
        ApplyTextToAll(displayNameText, extraDisplayNameTexts, text);
    }

    private static void ApplyTextToAll(TextMeshProUGUI primary, TextMeshProUGUI[] extras, string text)
    {
        if (primary != null)
            primary.text = text;

        if (extras == null) return;
        for (int i = 0; i < extras.Length; i++)
        {
            if (extras[i] != null)
                extras[i].text = text;
        }
    }

    private void ApplyAvatarSprite(Sprite sprite)
    {
        void ApplyOne(Image img)
        {
            if (img == null) return;
            img.sprite = sprite;
            img.enabled = sprite != null;
        }

        ApplyOne(avatarImage);
        if (extraAvatarImages == null) return;
        for (int i = 0; i < extraAvatarImages.Length; i++)
            ApplyOne(extraAvatarImages[i]);
    }

    private void Fail(string message)
    {
        Debug.LogWarning("[Profile] " + message);
        if (errorText != null)
            errorText.text = message;
        ProfileLoadFailed?.Invoke(message);
    }

    private void ClearError()
    {
        if (errorText != null)
            errorText.text = string.Empty;
    }

    // ── Public: cập nhật coins ngay trên UI (optimistic) ────────────────────

    /// <summary>
    /// Cập nhật số coins trên UI ngay lập tức mà không cần gọi lại server.
    /// Dùng khi biết chính xác tổng coins mới (ví dụ sau redeem code).
    /// </summary>
    public void UpdateCoinsImmediate(long newTotalCoins)
    {
        if (CurrentProfile != null)
            CurrentProfile.coins = newTotalCoins;

        if (coinsText != null)
            coinsText.text = newTotalCoins.ToString("N0");

        Debug.Log($"[Profile] Coins updated immediately: {newTotalCoins}");
    }

    /// <summary>
    /// Cộng thêm coins vào số hiện tại (optimistic).
    /// </summary>
    public void AddCoinsImmediate(long coinsToAdd)
    {
        long current = CurrentProfile != null ? CurrentProfile.coins : 0;
        UpdateCoinsImmediate(current + coinsToAdd);
    }
}
