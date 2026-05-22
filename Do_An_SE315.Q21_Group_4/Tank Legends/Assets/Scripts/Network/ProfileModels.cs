using System;

[Serializable]
public class UserMeResponseData
{
    public string userId;
    public string username;
    public string email;
}

[Serializable]
public class ProfileResponseData
{
    public string userId;
    public string displayName;
    public string imageId;
    public long coins;
    public int rp;
    public string createdAt;
    public string updatedAt;
}

[Serializable]
public class UpdateProfileRequestData
{
    public string displayName;
    public string imageId;
}
