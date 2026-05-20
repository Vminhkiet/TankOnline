using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Maps profile imageId strings to preset sprites in Resources.
/// Place avatars under Resources/Sprites/Avatars/ — sprite name = imageId.
/// </summary>
[CreateAssetMenu(fileName = "AvatarCatalog", menuName = "Tank Legends/Avatar Catalog")]
public class AvatarCatalog : ScriptableObject
{
    [SerializeField] private string resourcesFolder = "Sprites/Avatars";
    [SerializeField] private Sprite defaultAvatar;

    private Dictionary<string, Sprite> _byId;

    public Sprite GetSprite(string imageId)
    {
        EnsureLoaded();

        if (string.IsNullOrWhiteSpace(imageId))
            return defaultAvatar;

        string key = imageId.Trim();
        if (_byId.TryGetValue(key, out var sprite) && sprite != null)
            return sprite;

        return defaultAvatar;
    }

    public IReadOnlyList<string> GetPresetIds()
    {
        EnsureLoaded();
        return new List<string>(_byId.Keys);
    }

    private void EnsureLoaded()
    {
        if (_byId != null)
            return;

        _byId = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);

        Sprite[] sprites = Resources.LoadAll<Sprite>(resourcesFolder);
        foreach (var s in sprites)
        {
            if (s == null || string.IsNullOrWhiteSpace(s.name))
                continue;

            if (!_byId.ContainsKey(s.name))
                _byId[s.name] = s;
        }

        if (defaultAvatar == null && sprites.Length > 0)
            defaultAvatar = sprites[0];
    }
}
