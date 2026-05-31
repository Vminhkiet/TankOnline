# Báo Cáo Tối Ưu Hiệu Năng — Tank Legends (Mobile)

**Thiết bị test:** Samsung Galaxy A13 (SM-A135F)  
**Chip:** Exynos 850 — 8 nhân, Mali-G52 MC2, RAM 3–4 GB  
**Mục tiêu:** Ổn định 30–60 FPS trên thiết bị tầm thấp

---

## 1. Phân Tích Profiler Ban Đầu

### 1.1 CPU Bottleneck — Scripts

**Triệu chứng:** FPS dao động 13–20, Profiler báo spike ở `FixedUpdate`.

**Nguyên nhân tìm được trong `TankMovement.cs`:**

```csharp
// Code CŨ — gọi GetComponent mỗi FixedUpdate (~50 lần/giây × số tank)
var shooting = GetComponent<TankShooting>();
if (shooting != null) {
    turretYaw = shooting.GetCurrentTurretYaw();
    reload    = shooting.ConsumeReloadIntent();
}
```

`GetComponent<T>()` là lệnh tra cứu hash tốn kém. Khi nhiều tank đồng thời gọi mỗi `FixedUpdate`, tổng chi phí lên đến **35ms/frame**.

**Giải pháp — Cache component trong `Awake()`:**

```csharp
// Awake()
m_TankShooting = GetComponent<TankShooting>();

// SendOnlineMoveDiscrete() — chỉ đọc field đã cache
if (m_TankShooting != null) {
    turretYaw = m_TankShooting.GetCurrentTurretYaw();
    reload    = m_TankShooting.ConsumeReloadIntent();
}
```

**File:** `Assets/Scripts/GamePlay/Tank/TankMovement.cs`

---

### 1.2 GPU Bottleneck — Fill-Rate

**Triệu chứng:** Main Thread bị kẹt ở `Device.Present` hơn 11ms — dấu hiệu GPU không kịp render.

**Dữ liệu Profiler (frame điển hình):**

| Thành phần | Chi phí |
|---|---|
| Render Pipeline tổng | ~63ms |
| Bloom (Post-Processing) | **11.67ms** |
| MainLightShadow | **12.53ms** |
| UberPostProcess | 12.80ms |
| Physics Solver | **16.35ms** |
| Global Illumination | 4.44ms |
| Scripts | 0.43ms ✅ |

**Kết luận:** Bottleneck nằm ở GPU rendering, không phải code logic.

---

### 1.3 Lỗi Nhận Diện Phần Cứng

**Nguyên nhân khiến thiết bị yếu vẫn chạy chất lượng Trung bình:**

```csharp
// Code CŨ — SAI
if (ramMB < 3000 || cpuCount < 4) return 2; // Thấp
```

Exynos 850 có **8 nhân** và **4 GB RAM** — vượt cả hai điều kiện → bị phân loại nhầm vào **Trung bình** → Bloom + Shadow + PostProcessing vẫn bật đầy đủ → game lag.

**Vấn đề:** `cpuCount` không phản ánh sức mạnh GPU. Chip mobile giá rẻ đã phổ cập 8 nhân từ lâu.

---

## 2. Giải Pháp Đã Triển Khai

### 2.1 Hệ Thống Chất Lượng Đồ Họa 3 Mức

**File:** `Assets/Scripts/UI/Settings/GraphicsSettingsManager.cs`

#### Bảng tổng hợp 3 mức

| Tính năng | Cao | Trung bình | Thấp |
|---|---|---|---|
| Render Scale | 1.0 | 0.5 | 0.5 |
| HDR | Bật | Bật | **Tắt** |
| MSAA | 4x | 4x | **1x** |
| Shadows | All | All | **Disable** |
| Global Volumes | Enable | Enable | **Disable** |
| Post-Processing | Bật | Bật | **Tắt** |
| Bloom | Bật | Bật | **Tắt** |
| GI (indirectScale) | 1.0 | 1.0 | **0.0** |
| Physics Solver | 6 iter | 6 iter | **2 iter** |
| GPU Instancing | — | — | **Bật** |
| VSync / Target FPS | 0 / 60 | 0 / 60 | 0 / 60 |

#### Logic tắt Post-Processing ở mức Thấp

```csharp
// Disable toàn bộ Global Volume (cắt vòng lặp Post-Processing)
foreach (Volume vol in FindObjectsOfType<Volume>())
    if (vol != null && vol.isGlobal)
        vol.enabled = highQuality;

// Tắt trên camera
camData.renderPostProcessing = highQuality;

// Tắt GI contribution
DynamicGI.indirectScale = highQuality ? 1f : 0f;

// Giảm Physics solver
Physics.defaultSolverIterations = highQuality ? 6 : 2;
```

---

### 2.2 Thuật Toán Nhận Diện Thiết Bị

Thay `cpuCount` bằng hệ thống 2 bước: **RAM gate** + **GPU score**.

```
Bước 1 — RAM gate (cứng):
    RAM < 6 GB → Thấp ngay (bắt A13, A03, Helio G80, v.v.)

Bước 2 — GPU score (chỉ khi RAM >= 6 GB):
    shaderLevel >= 50  → +2  (Vulkan / Metal compute-capable)
    shaderLevel >= 45  → +1  (OpenGL ES 3.1+)
    vramMB >= 2048     → +1
    supportsCS = true  → +1  (Compute Shader)

Phân loại:
    score < 2  → Thấp  (RAM ổn nhưng GPU yếu)
    score 2–3  → Trung bình
    score >= 4 → Cao
```

**Nguyên tắc:** Nghi ngờ → fallback về **Thấp**. Thà mượt hơn đẹp mà lag.

#### Ví dụ thiết bị thực tế

| Thiết bị | RAM | shaderLevel | Score | Kết quả |
|---|---|---|---|---|
| Samsung A13 (Exynos 850) | 4 GB | 46 | — | **Thấp** (RAM gate) |
| Samsung A13 (Exynos 850) | 3 GB | 46 | — | **Thấp** (RAM gate) |
| Redmi Note 11 (Snapdragon 680) | 4 GB | 46 | — | **Thấp** (RAM gate) |
| Samsung A54 (Exynos 1380) | 8 GB | 50 | 3 | **Trung bình** |
| iPhone 13 (A15 Bionic) | 6 GB | 50+ | 4+ | **Cao** |
| Samsung S23 (Snapdragon 8 Gen 2) | 8–12 GB | 60 | 4+ | **Cao** |

---

## 3. Kết Quả Tối Ưu (Dự Tính)

### Tiết kiệm trên Samsung A13 khi vào mức Thấp

| Thành phần | Trước | Sau |
|---|---|---|
| Bloom | 11.67ms | **0ms** |
| Shadows | 12.53ms | **0ms** |
| PostProcessing | 15.42ms | **~0ms** |
| Physics Solver | 16.35ms | **~8ms** |
| Scripts (GetComponent) | 35ms spike | **~0ms** |
| **Tổng frame time** | ~94ms | **~46ms** |
| **FPS** | ~13–20 | **~30–40** |

---

## 4. Các File Đã Chỉnh Sửa

| File | Thay đổi |
|---|---|
| `Assets/Scripts/GamePlay/Tank/TankMovement.cs` | Cache `TankShooting` trong `Awake()` |
| `Assets/Scripts/UI/Settings/GraphicsSettingsManager.cs` | Toàn bộ hệ thống graphics quality |

---

## 5. Lưu Ý Khi Phát Triển Tiếp

- `urpAsset.renderScale` / `supportsHDR` / `msaaSampleCount` sửa vào **shared URP Asset** — thay đổi persist trong Editor. Sau khi test nhớ revert qua **Project Settings → Graphics**.
- `Physics.defaultSolverIterations = 2` ở mức Thấp có thể gây artifact vật lý nhẹ ở các va chạm phức tạp. Tăng lên `3–4` nếu cần độ chính xác cao hơn.
- `DynamicGI.indirectScale = 0` tắt *kết quả hiển thị* GI nhưng Enlighten solver vẫn chạy ngầm. Để tắt hoàn toàn, cần disable Realtime GI trong **Lighting Settings** của từng Scene trước khi build.
