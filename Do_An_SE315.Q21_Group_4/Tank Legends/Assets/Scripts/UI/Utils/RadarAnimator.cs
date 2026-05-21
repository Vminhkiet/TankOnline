using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class RadarAnimator : MonoBehaviour
{
    [Header("Sweep Animation")]
    [Tooltip("Kéo object Sweep vào đây")]
    public RectTransform sweepTransform;
    [Tooltip("Tốc độ xoay của thanh quét (độ/giây)")]
    public float rotationSpeed = 180f;

    [Header("Dots Settings")]
    [Tooltip("Kéo object Dots (dùng làm cha chứa các chấm) vào đây")]
    public RectTransform dotsContainer;
    [Tooltip("Danh sách các ảnh (Sprite) của các loại Dot khác nhau")]
    public List<Sprite> dotSprites;
    [Tooltip("Prefab của Dot (kéo Prefab từ Project vào đây)")]
    public GameObject dotPrefab;
    
    [Header("Spawning Timing")]
    public float spawnIntervalMin = 0.5f;
    public float spawnIntervalMax = 2f;
    public float dotLifetime = 2.5f;

    [Header("Audio")]
    public AudioSource audioSource;
    public AudioClip beepSound;
    public float beepIntervalMin = 0.5f;
    public float beepIntervalMax = 3f;

    private float nextSpawnTime;
    private float nextBeepTime;

    private void Start()
    {
        ScheduleNextSpawn();
        ScheduleNextBeep();
    }

    private void Update()
    {
        // 1. Animate thanh Sweep (Xoay liên tục)
        if (sweepTransform != null)
        {
            // Xoay quanh trục Z, dấu trừ để xoay theo chiều kim đồng hồ
            sweepTransform.Rotate(0, 0, -rotationSpeed * Time.deltaTime);
        }

        // 2. Kiểm tra thời gian để sinh Dot mới
        if (Time.time >= nextSpawnTime)
        {
            SpawnDot();
            ScheduleNextSpawn();
        }

        // 3. Kiểm tra thời gian để phát tiếng Beep
        if (Time.time >= nextBeepTime)
        {
            PlayBeep();
            ScheduleNextBeep();
        }
    }

    private void ScheduleNextSpawn()
    {
        nextSpawnTime = Time.time + Random.Range(spawnIntervalMin, spawnIntervalMax);
    }

    private void ScheduleNextBeep()
    {
        nextBeepTime = Time.time + Random.Range(beepIntervalMin, beepIntervalMax);
    }

    private void SpawnDot()
    {
        // Kiểm tra xem đã thiết lập đủ chưa
        if (dotsContainer == null || dotPrefab == null || dotSprites == null || dotSprites.Count == 0) return;

        // Instantiate từ Prefab đã có
        GameObject dotObj = Instantiate(dotPrefab, dotsContainer);

        // Lấy Image và gán sprite ngẫu nhiên từ danh sách
        Image dotImage = dotObj.GetComponent<Image>();
        if (dotImage != null)
        {
            dotImage.sprite = dotSprites[Random.Range(0, dotSprites.Count)];
        }
        
        // Random vị trí nằm trong hình tròn nội tiếp của dotsContainer
        RectTransform rectTransform = dotObj.GetComponent<RectTransform>();
        if (rectTransform != null)
        {
            // Bán kính hình tròn nội tiếp = nửa cạnh nhỏ nhất của Rect
            float radius = Mathf.Min(dotsContainer.rect.width, dotsContainer.rect.height) / 2f;
            Vector2 randomPos = Random.insideUnitCircle * radius;
            rectTransform.anchoredPosition = randomPos;
        }

        // Xóa chấm đỏ sau khoảng thời gian dotLifetime
        Destroy(dotObj, dotLifetime);
    }

    private void PlayBeep()
    {
        if (audioSource != null && beepSound != null)
        {
            audioSource.PlayOneShot(beepSound);
        }
    }
}
