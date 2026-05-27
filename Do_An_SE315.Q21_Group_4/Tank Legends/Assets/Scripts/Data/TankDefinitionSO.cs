using System;
using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "TankDefinition", menuName = "Tank Legends/Tank Definition")]
public class TankDefinitionSO : ScriptableObject
{
    [Header("Identity")]
    [SerializeField] private string tankName;
    [TextArea(3, 6)]
    [SerializeField] private string description;
    [Min(0)]
    [SerializeField] private int price;

    [Header("Prefabs")]
    [SerializeField] private GameObject previewPrefab;
    [SerializeField] private GameObject gameplayPrefab;

    [Header("Preview Transform")]
    [SerializeField] private Vector3 previewLocalPosition = Vector3.zero;
    [SerializeField] private Vector3 previewLocalEulerAngles = new Vector3(0f, 180f, 0f);
    [SerializeField] private Vector3 previewLocalScale = Vector3.one;

    [Header("Combat Stats")]
    [SerializeField] private TankStats stats;
    [SerializeField] private WeaponType weaponType = WeaponType.Projectile;

    [Header("Special Ability")]
    [SerializeField] private TankSpecialAbility specialAbility;

    [Header("Real Gameplay Stats")]
    [SerializeField] private RealGameplayStats realStats;

    public string TankName => string.IsNullOrWhiteSpace(tankName) ? name : tankName;
    public string Description => description;
    public int Price => price;
    public GameObject PreviewPrefab => previewPrefab != null ? previewPrefab : gameplayPrefab;
    public GameObject GameplayPrefab => gameplayPrefab != null ? gameplayPrefab : previewPrefab;
    public Vector3 PreviewLocalPosition => previewLocalPosition;
    public Vector3 PreviewLocalEulerAngles => previewLocalEulerAngles;
    public Vector3 PreviewLocalScale => previewLocalScale == Vector3.zero ? Vector3.one : previewLocalScale;
    public TankStats Stats => stats;
    public WeaponType WeaponType => weaponType;
    public TankSpecialAbility SpecialAbility => specialAbility;
    public RealGameplayStats RealStats => realStats;

#if UNITY_EDITOR
    private void OnValidate()
    {
        price = Mathf.Max(0, price);
        stats.ClampValues();
    }
#endif
}

[Serializable]
public struct TankStats
{
    [Range(0, 10)]
    [SerializeField] private int damage;
    [Range(0, 10)]
    [SerializeField] private int armor;
    [Range(0, 10)]
    [SerializeField] private int speed;
    [Range(0, 10)]
    [SerializeField] private int fireRate;
    [Range(0, 10)]
    [FormerlySerializedAs("mobility")]
    [SerializeField] private int fireRange;

    public int Damage => damage;
    public int Armor => armor;
    public int Speed => speed;
    public int FireRate => fireRate;
    public int FireRange => fireRange;

    public int GetValue(TankStatType statType)
    {
        switch (statType)
        {
            case TankStatType.Damage:
                return damage;
            case TankStatType.Armor:
                return armor;
            case TankStatType.Speed:
                return speed;
            case TankStatType.FireRate:
                return fireRate;
            case TankStatType.FireRange:
                return fireRange;
            default:
                return 0;
        }
    }

    public void ClampValues()
    {
        damage = Mathf.Clamp(damage, 0, 10);
        armor = Mathf.Clamp(armor, 0, 10);
        speed = Mathf.Clamp(speed, 0, 10);
        fireRate = Mathf.Clamp(fireRate, 0, 10);
        fireRange = Mathf.Clamp(fireRange, 0, 10);
    }
}

[Serializable]
public struct RealGameplayStats
{
    [Tooltip("Lượng máu tối đa của Tank (VD: 100, 150, 200)")]
    [SerializeField] private float maxHealth;
    
    [Tooltip("Tốc độ di chuyển thực tế (VD: 12)")]
    [SerializeField] private float movementSpeed;
    
    [Tooltip("Tốc độ bắn thực tế - số phát/giây (VD: 1.5)")]
    [SerializeField] private float fireRate;
    
    [Tooltip("Sát thương tối đa của đạn tại tâm vụ nổ (VD: 20, 40)")]
    [SerializeField] private float damage;
    
    [Tooltip("Tầm bắn thực tế của súng (VD: 30)")]
    [SerializeField] private float fireRange;

    public float MaxHealth => maxHealth == 0f ? 100f : maxHealth;
    public float MovementSpeed => movementSpeed == 0f ? 12f : movementSpeed;
    public float FireRate => fireRate == 0f ? 1.5f : fireRate;
    public float Damage => damage == 0f ? 20f : damage;
    public float FireRange => fireRange == 0f ? 30f : fireRange;
}

[Serializable]
public struct TankSpecialAbility
{
    [SerializeField] private string abilityName;
    [SerializeField] private Sprite icon;
    [TextArea(2, 5)]
    [SerializeField] private string description;

    public string AbilityName => abilityName;
    public Sprite Icon => icon;
    public string Description => description;
}

public enum TankStatType
{
    Damage,
    Armor,
    Speed,
    FireRate,
    [InspectorName("Fire Range")]
    FireRange
}

public enum WeaponType
{
    Projectile,
    Hitscan
}
