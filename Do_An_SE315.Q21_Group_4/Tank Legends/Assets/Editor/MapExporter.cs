using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;

public class MapExporter
{
    const int MeshHeightmapResolution = 32;

    [MenuItem("Tools/Export Game World (Tagged Roots)")]
    public static void ExportWorld()
    {
        GameObject[] roots = GameObject.FindObjectsOfType<GameObject>();

        List<ColliderData> colliders = new List<ColliderData>();
        List<ColliderData> bushes = new List<ColliderData>();
        List<HeightmapData> heightmaps = new List<HeightmapData>();
        List<SpawnData> spawns = new List<SpawnData>();

        var processedTerrains = new HashSet<Terrain>();

        foreach (GameObject obj in roots)
        {
            if (obj.transform.parent != null)
                continue;

            if (obj.CompareTag("Map"))
            {
                ProcessColliders(obj, colliders, bushes);
                // Track terrains already exported under Map
                foreach (var t in obj.GetComponentsInChildren<Terrain>())
                    processedTerrains.Add(t);
                ProcessHeightmaps(obj, heightmaps);
            }
            else if (obj.CompareTag("SpawnPoint"))
            {
                ProcessSpawns(obj, spawns);
            }
        }

        // Export any Terrain in scene not already under a Map-tagged root
        foreach (var terrain in GameObject.FindObjectsOfType<Terrain>())
        {
            if (processedTerrains.Contains(terrain)) continue;
            var data = CreateTerrainHeightmapData(terrain);
            if (data != null) heightmaps.Add(data);
        }

        WorldData world = new WorldData
        {
            colliders = colliders,
            bushes    = bushes,
            heightmaps = heightmaps,
            spawns    = spawns,
            tank      = BuildTankConfig(),
            bullet    = BuildBulletConfig()
        };

        string json = JsonUtility.ToJson(world, true);

        File.WriteAllText(Application.dataPath + "/world.json", json);

        Debug.Log("World export completed!");
    }

    // =========================
    // HEIGHTMAP EXPORT
    // =========================
    static void ProcessHeightmaps(GameObject root, List<HeightmapData> list)
    {
        Terrain[] terrains = root.GetComponentsInChildren<Terrain>();

        foreach (Terrain terrain in terrains)
        {
            HeightmapData data = CreateTerrainHeightmapData(terrain);

            if (data != null)
                list.Add(data);
        }

        MeshFilter[] meshes = root.GetComponentsInChildren<MeshFilter>();

        foreach (MeshFilter meshFilter in meshes)
        {
            if (!IsMapSurfaceMesh(meshFilter))
                continue;

            HeightmapData data = CreateMeshHeightmapData(meshFilter);

            if (data != null)
                list.Add(data);
        }
    }

    static HeightmapData CreateTerrainHeightmapData(Terrain terrain)
    {
        TerrainData terrainData = terrain.terrainData;

        if (terrainData == null)
            return null;

        int resolution = terrainData.heightmapResolution;
        float[,] terrainHeights = terrainData.GetHeights(0, 0, resolution, resolution);
        float[] heights = new float[resolution * resolution];

        Vector3 terrainPosition = terrain.transform.position;
        Vector3 terrainSize = terrainData.size;

        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
                heights[z * resolution + x] = terrainPosition.y + terrainHeights[z, x] * terrainSize.y;
        }

        return new HeightmapData
        {
            name = terrain.gameObject.name,
            sourceType = "terrain",
            origin = terrainPosition,
            rotation = terrain.transform.eulerAngles,
            size = terrainSize,
            resolutionX = resolution,
            resolutionZ = resolution,
            heights = heights
        };
    }

    static HeightmapData CreateMeshHeightmapData(MeshFilter meshFilter)
    {
        Renderer renderer = meshFilter.GetComponent<Renderer>();
        Mesh mesh = meshFilter.sharedMesh;

        if (renderer == null || mesh == null)
            return null;

        Bounds bounds = renderer.bounds;
        Vector3[] worldVertices = GetWorldVertices(meshFilter);
        int[] triangles = mesh.triangles;
        float[] heights = new float[MeshHeightmapResolution * MeshHeightmapResolution];

        for (int z = 0; z < MeshHeightmapResolution; z++)
        {
            float zPercent = z / (float)(MeshHeightmapResolution - 1);
            float sampleZ = Mathf.Lerp(bounds.min.z, bounds.max.z, zPercent);

            for (int x = 0; x < MeshHeightmapResolution; x++)
            {
                float xPercent = x / (float)(MeshHeightmapResolution - 1);
                float sampleX = Mathf.Lerp(bounds.min.x, bounds.max.x, xPercent);
                heights[z * MeshHeightmapResolution + x] = SampleMeshHeight(sampleX, sampleZ, worldVertices, triangles, bounds.center.y);
            }
        }

        return new HeightmapData
        {
            name = meshFilter.gameObject.name,
            sourceType = "mesh",
            origin = new Vector3(bounds.min.x, bounds.center.y, bounds.min.z),
            rotation = Vector3.zero,
            size = new Vector3(bounds.size.x, 0f, bounds.size.z),
            resolutionX = MeshHeightmapResolution,
            resolutionZ = MeshHeightmapResolution,
            heights = heights
        };
    }

    static Vector3[] GetWorldVertices(MeshFilter meshFilter)
    {
        Vector3[] localVertices = meshFilter.sharedMesh.vertices;
        Vector3[] worldVertices = new Vector3[localVertices.Length];

        for (int i = 0; i < localVertices.Length; i++)
            worldVertices[i] = meshFilter.transform.TransformPoint(localVertices[i]);

        return worldVertices;
    }

    static float SampleMeshHeight(float x, float z, Vector3[] vertices, int[] triangles, float fallbackHeight)
    {
        bool found = false;
        float height = fallbackHeight;

        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 a = vertices[triangles[i]];
            Vector3 b = vertices[triangles[i + 1]];
            Vector3 c = vertices[triangles[i + 2]];

            if (!TryGetBarycentricXZ(x, z, a, b, c, out Vector3 barycentric))
                continue;

            float candidateHeight = a.y * barycentric.x + b.y * barycentric.y + c.y * barycentric.z;

            if (!found || candidateHeight > height)
            {
                height = candidateHeight;
                found = true;
            }
        }

        return height;
    }

    static bool TryGetBarycentricXZ(float x, float z, Vector3 a, Vector3 b, Vector3 c, out Vector3 barycentric)
    {
        Vector2 point = new Vector2(x, z);
        Vector2 pointA = new Vector2(a.x, a.z);
        Vector2 pointB = new Vector2(b.x, b.z);
        Vector2 pointC = new Vector2(c.x, c.z);

        Vector2 v0 = pointB - pointA;
        Vector2 v1 = pointC - pointA;
        Vector2 v2 = point - pointA;

        float d00 = Vector2.Dot(v0, v0);
        float d01 = Vector2.Dot(v0, v1);
        float d11 = Vector2.Dot(v1, v1);
        float d20 = Vector2.Dot(v2, v0);
        float d21 = Vector2.Dot(v2, v1);
        float denominator = d00 * d11 - d01 * d01;

        if (Mathf.Abs(denominator) < 0.000001f)
        {
            barycentric = Vector3.zero;
            return false;
        }

        float v = (d11 * d20 - d01 * d21) / denominator;
        float w = (d00 * d21 - d01 * d20) / denominator;
        float u = 1f - v - w;
        const float tolerance = -0.0001f;

        barycentric = new Vector3(u, v, w);
        return u >= tolerance && v >= tolerance && w >= tolerance;
    }

    static bool IsMapSurfaceMesh(MeshFilter meshFilter)
    {
        if (meshFilter.sharedMesh == null)
            return false;

        string objectName = meshFilter.gameObject.name.ToLowerInvariant();
        string meshName = meshFilter.sharedMesh.name.ToLowerInvariant();

        return objectName.Contains("groundplane")
            || objectName.Contains("plane")
            || objectName.Contains("ground")
            || objectName.Contains("floor")
            || meshName.Contains("plane");
    }

    // =========================
    // COLLIDER EXPORT
    // =========================
    static void ProcessColliders(GameObject root, List<ColliderData> list, List<ColliderData> bushesList)
    {
        Collider[] cols = root.GetComponentsInChildren<Collider>();

        foreach (var col in cols)
        {
            ColliderData data = new ColliderData();

            data.name = col.gameObject.name;

            // WORLD SPACE (QUAN TRỌNG)
            data.position = col.transform.position;
            data.rotation = col.transform.eulerAngles;

            if (col is BoxCollider box)
            {
                data.type = "box";
                data.center = col.transform.TransformPoint(box.center);
                data.size = Vector3.Scale(box.size, col.transform.lossyScale);
                data.walkable = col.gameObject.CompareTag("Surface");
            }
            else if (col is SphereCollider sphere)
            {
                data.type = "sphere";
                data.center = col.transform.TransformPoint(sphere.center);

                float scale = Mathf.Max(
                    col.transform.lossyScale.x,
                    col.transform.lossyScale.y,
                    col.transform.lossyScale.z
                );

                data.radius = sphere.radius * scale;
            }
            else if (col is CapsuleCollider capsule)
            {
                data.type = "capsule";
                data.center = col.transform.TransformPoint(capsule.center);

                float scaleX = col.transform.lossyScale.x;
                float scaleY = col.transform.lossyScale.y;
                float scaleZ = col.transform.lossyScale.z;

                data.radius = capsule.radius * Mathf.Max(scaleX, scaleZ);
                data.height = capsule.height * scaleY;
                data.direction = capsule.direction;
            }
            else
            {
                continue; // ignore MeshCollider etc
            }

            if (col.gameObject.CompareTag("Bush"))
            {
                bushesList.Add(data);
            }
            else
            {
                list.Add(data);
            }
        }
    }

    // =========================
    // SPAWN EXPORT
    // =========================
    static void ProcessSpawns(GameObject root, List<SpawnData> list)
    {
        SpawnMarker[] sp = root.GetComponentsInChildren<SpawnMarker>();

        foreach (var s in sp)
        {
            SpawnData data = new SpawnData();
            data.id = s.id;
            data.position = s.transform.position;

            list.Add(data);
        }
    }

    // =========================
    // TANK / BULLET CONFIG
    // =========================
    static TankConfigData BuildTankConfig()
    {
        GameObject tank = GameObject.FindWithTag("Tank");
        var box = tank != null ? tank.GetComponentInChildren<BoxCollider>(true) : null;
        if (box == null)
        {
            Debug.LogWarning("[MapExporter] No GameObject tagged 'Tank' with BoxCollider found – using defaults.");
            return new TankConfigData { collider_extents = new Vec3Data(0.9f, 1.0f, 1.2f) };
        }

        Vector3 s = box.size, sc = box.transform.lossyScale;
        float ex = s.x * Mathf.Abs(sc.x) * 0.5f;
        float ey = s.y * Mathf.Abs(sc.y) * 0.5f;
        float ez = s.z * Mathf.Abs(sc.z) * 0.5f;
        Debug.Log($"[MapExporter] Tank collider extents: ({ex:F4}, {ey:F4}, {ez:F4})");
        return new TankConfigData { collider_extents = new Vec3Data(ex, ey, ez) };
    }

    static BulletConfigData BuildBulletConfig()
    {
        GameObject shell = GameObject.FindWithTag("Shell");
        var sph = shell != null ? shell.GetComponentInChildren<SphereCollider>(true) : null;
        if (sph == null)
        {
            Debug.LogWarning("[MapExporter] No GameObject tagged 'Shell' with SphereCollider found – using default radius 0.25.");
            return new BulletConfigData { collider_radius = 0.25f };
        }

        float maxSc = Mathf.Max(Mathf.Abs(sph.transform.lossyScale.x),
                                Mathf.Abs(sph.transform.lossyScale.y),
                                Mathf.Abs(sph.transform.lossyScale.z));
        float radius = sph.radius * maxSc;
        Debug.Log($"[MapExporter] Bullet collider radius: {radius:F4}");
        return new BulletConfigData { collider_radius = radius };
    }

    // =========================
    // DATA STRUCTS
    // =========================
    [System.Serializable]
    public class WorldData
    {
        public List<ColliderData> colliders;
        public List<ColliderData> bushes;
        public List<HeightmapData> heightmaps;
        public List<SpawnData> spawns;
        public TankConfigData   tank;
        public BulletConfigData bullet;
    }

    [System.Serializable]
    public class Vec3Data
    {
        public float x, y, z;
        public Vec3Data(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    }

    [System.Serializable]
    public class TankConfigData { public Vec3Data collider_extents; }

    [System.Serializable]
    public class BulletConfigData { public float collider_radius; }

    [System.Serializable]
    public class HeightmapData
    {
        public string name;
        public string sourceType;

        public Vector3 origin;
        public Vector3 rotation;
        public Vector3 size;

        public int resolutionX;
        public int resolutionZ;
        public float[] heights;
    }

    [System.Serializable]
    public class ColliderData
    {
        public string name;
        public string type;

        public Vector3 position;
        public Vector3 rotation;

        public Vector3 center;

        public Vector3 size;

        public float radius;
        public float height;
        public int direction;
        public bool walkable;
    }

    [System.Serializable]
    public class SpawnData
    {
        public string id;
        public Vector3 position;
    }
}
