using UnityEngine;

namespace Complete.Skills
{
    [CreateAssetMenu(fileName = "NewSkillData", menuName = "Tank Legends/Skill Data")]
    public class SkillData : ScriptableObject
    {
        [Header("Basic Info")]
        public string skillName;
        public SkillType skillType = SkillType.Generic;
        [TextArea(2, 5)]
        public string description;
        public Sprite icon;
        public float cooldown = 5f;

        [Header("Targeting & Shape")]
        public TargetingType targetingType;
        public ShapeType shapeType;
        public float castRange = 10f; // Max distance for casting (0 for self/direction)

        [Header("Shape Dimensions")]
        public float radius = 5f;    // For Circle and Cone
        public float length = 20f;   // For Line
        [Range(0f, 360f)]
        public float angle = 45f;    // For Cone
        [Tooltip("Số tiền (gem/coin) cần để mua skill này.")]
        public int price;

        [Header("Effect Duration")]
        [Tooltip("Thời gian duy trì hiệu ứng của skill (dành cho Shield, Buff...). Bằng 0 nếu là skill nổ 1 lần.")]
        public float duration = 0f;

        [Tooltip("Thời gian tồn tại của Execute VFX trên màn hình trước khi bị xóa. Nếu <= 0, sẽ tự động xóa sau 3s.")]
        public float vfxDuration = 3f;

        [Header("Charging / Cast Delay")]
        public float chargeTime = 0f;
        [Tooltip("Phần trăm tốc độ di chuyển bị giảm khi charge skill (0 = di chuyển bình thường, 100 = đứng yên).")]
        [Range(0f, 100f)]
        public float speedReductionPercent = 100f;
        [Tooltip("Thời gian khóa nòng súng không cho tự động quay về sau khi bắn (tạo cảm giác nặng).")]
        public float postFireTurretLockTime = 0.5f;

        public GameObject chargeVfxPrefab;
        [Tooltip("Thời gian trễ để xoá VFX gồng chiêu sau khi gồng xong. Đặt là 0 để xoá ngay lập tức.")]
        public float chargeVfxDestroyDelay = 0f;
        public AudioClip chargeSound;

        [Header("Client Visuals")]
        public GameObject vfxPrefab;
        [Tooltip("The native size of your VFX prefab (e.g. if the default shield radius is 2, set this to 2). We will scale it by (radius / vfxBaseSize).")]
        public float vfxBaseSize = 1f;
        public AudioClip castSound;
        public Color indicatorColor = new Color(0f, 1f, 1f, 0.3f);
    }
}
