using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public class MapLoader
{
    const string PreviewRootName = "Loaded World Preview";
    const string ColliderRootName = "Colliders";
    const string HeightmapRootName = "Heightmaps";
    const string SpawnRootName = "Spawns";

    [MenuItem("Tools/Load Game World Preview")]
    public static void LoadWorldPreview()
    {
        string path = Path.Combine(Application.dataPath, "world.json");

        if (!File.Exists(path))
        {
            Debug.LogError($"World file not found: {path}");
            return;
        }

        WorldData world = JsonUtility.FromJson<WorldData>(File.ReadAllText(path));

        if (world == null)
        {
            Debug.LogError($"Failed to parse world file: {path}");
            return;
        }

        ClearWorldPreview();

        GameObject root = new GameObject(PreviewRootName);
        GameObject colliderRoot = new GameObject(ColliderRootName);
        GameObject heightmapRoot = new GameObject(HeightmapRootName);
        GameObject spawnRoot = new GameObject(SpawnRootName);

        Undo.RegisterCreatedObjectUndo(root, "Load World Preview");
        colliderRoot.transform.SetParent(root.transform);
        heightmapRoot.transform.SetParent(root.transform);
        spawnRoot.transform.SetParent(root.transform);

        Material colliderMaterial = CreatePreviewMaterial(new Color(0.1f, 0.65f, 1f, 0.28f));
        Material heightmapMaterial = CreatePreviewMaterial(new Color(0.45f, 0.75f, 0.28f, 0.55f));
        Material spawnMaterial = CreatePreviewMaterial(new Color(0.1f, 1f, 0.35f, 0.8f));

        int colliderCount = 0;
        int heightmapCount = 0;
        int spawnCount = 0;

        if (world.colliders != null)
        {
            foreach (ColliderData collider in world.colliders)
            {
                GameObject preview = CreateColliderPreview(collider, colliderMaterial);

                if (preview == null)
                    continue;

                preview.transform.SetParent(colliderRoot.transform);
                colliderCount++;
            }
        }

        if (world.heightmaps != null)
        {
            foreach (HeightmapData heightmap in world.heightmaps)
            {
                GameObject preview = CreateHeightmapPreview(heightmap, heightmapMaterial);

                if (preview == null)
                    continue;

                preview.transform.SetParent(heightmapRoot.transform);
                heightmapCount++;
            }
        }

        if (world.spawns != null)
        {
            foreach (SpawnData spawn in world.spawns)
            {
                GameObject preview = CreateSpawnPreview(spawn, spawnMaterial);
                preview.transform.SetParent(spawnRoot.transform);
                spawnCount++;
            }
        }

        Selection.activeGameObject = root;
        Debug.Log($"Loaded world preview from {path}. Colliders: {colliderCount}, heightmaps: {heightmapCount}, spawns: {spawnCount}");
    }

    [MenuItem("Tools/Clear Game World Preview")]
    public static void ClearWorldPreview()
    {
        GameObject existing = GameObject.Find(PreviewRootName);

        if (existing != null)
            Undo.DestroyObjectImmediate(existing);
    }

    static GameObject CreateColliderPreview(ColliderData data, Material material)
    {
        switch (data.type)
        {
            case "box":
                return CreateBoxPreview(data, material);
            case "sphere":
                return CreateSpherePreview(data, material);
            case "capsule":
                return CreateCapsulePreview(data, material);
            default:
                Debug.LogWarning($"Unsupported collider type '{data.type}' on '{data.name}'.");
                return null;
        }
    }

    static GameObject CreateBoxPreview(ColliderData data, Material material)
    {
        GameObject preview = GameObject.CreatePrimitive(PrimitiveType.Cube);
        preview.name = $"Box - {data.name}";
        preview.transform.position = data.center;
        preview.transform.rotation = Quaternion.Euler(data.rotation);
        preview.transform.localScale = data.size;
        ApplyMaterial(preview, material);
        return preview;
    }

    static GameObject CreateSpherePreview(ColliderData data, Material material)
    {
        GameObject preview = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        preview.name = $"Sphere - {data.name}";
        preview.transform.position = data.center;
        preview.transform.localScale = Vector3.one * (data.radius * 2f);
        ApplyMaterial(preview, material);
        return preview;
    }

    static GameObject CreateCapsulePreview(ColliderData data, Material material)
    {
        GameObject preview = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        preview.name = $"Capsule - {data.name}";
        preview.transform.position = data.center;
        preview.transform.rotation = Quaternion.Euler(data.rotation) * GetCapsuleDirectionRotation(data.direction);
        preview.transform.localScale = new Vector3(data.radius * 2f, data.height * 0.5f, data.radius * 2f);
        ApplyMaterial(preview, material);
        return preview;
    }

    static GameObject CreateSpawnPreview(SpawnData data, Material material)
    {
        GameObject preview = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        preview.name = $"Spawn {data.id}";
        preview.transform.position = data.position + Vector3.up * 0.35f;
        preview.transform.localScale = Vector3.one * 0.7f;

        SpawnMarker marker = preview.AddComponent<SpawnMarker>();
        marker.id = data.id;

        ApplyMaterial(preview, material);
        return preview;
    }

    static GameObject CreateHeightmapPreview(HeightmapData data, Material material)
    {
        if (data.heights == null || data.resolutionX < 2 || data.resolutionZ < 2)
        {
            Debug.LogWarning($"Invalid heightmap data on '{data.name}'.");
            return null;
        }

        int expectedHeightCount = data.resolutionX * data.resolutionZ;

        if (data.heights.Length < expectedHeightCount)
        {
            Debug.LogWarning($"Heightmap '{data.name}' has {data.heights.Length} heights, expected {expectedHeightCount}.");
            return null;
        }

        Vector3[] vertices = new Vector3[expectedHeightCount];
        Vector2[] uvs = new Vector2[expectedHeightCount];
        int[] triangles = new int[(data.resolutionX - 1) * (data.resolutionZ - 1) * 6];
        Quaternion rotation = Quaternion.Euler(data.rotation);

        for (int z = 0; z < data.resolutionZ; z++)
        {
            float zPercent = z / (float)(data.resolutionZ - 1);

            for (int x = 0; x < data.resolutionX; x++)
            {
                float xPercent = x / (float)(data.resolutionX - 1);
                int index = z * data.resolutionX + x;
                Vector3 localOffset = new Vector3(data.size.x * xPercent, 0f, data.size.z * zPercent);

                vertices[index] = data.origin + rotation * localOffset + Vector3.up * (data.heights[index] - data.origin.y);
                uvs[index] = new Vector2(xPercent, zPercent);
            }
        }

        int triangleIndex = 0;

        for (int z = 0; z < data.resolutionZ - 1; z++)
        {
            for (int x = 0; x < data.resolutionX - 1; x++)
            {
                int bottomLeft = z * data.resolutionX + x;
                int bottomRight = bottomLeft + 1;
                int topLeft = bottomLeft + data.resolutionX;
                int topRight = topLeft + 1;

                triangles[triangleIndex++] = bottomLeft;
                triangles[triangleIndex++] = topLeft;
                triangles[triangleIndex++] = topRight;
                triangles[triangleIndex++] = bottomLeft;
                triangles[triangleIndex++] = topRight;
                triangles[triangleIndex++] = bottomRight;
            }
        }

        Mesh mesh = new Mesh();
        mesh.name = $"{data.name} Heightmap Preview";
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();

        GameObject preview = new GameObject($"Heightmap - {data.name}");
        preview.AddComponent<MeshFilter>().sharedMesh = mesh;
        preview.AddComponent<MeshRenderer>().sharedMaterial = material;
        preview.AddComponent<MeshCollider>().sharedMesh = mesh;
        return preview;
    }

    static Quaternion GetCapsuleDirectionRotation(int direction)
    {
        switch (direction)
        {
            case 0:
                return Quaternion.Euler(0f, 0f, -90f);
            case 2:
                return Quaternion.Euler(90f, 0f, 0f);
            default:
                return Quaternion.identity;
        }
    }

    static Material CreatePreviewMaterial(Color color)
    {
        Material material = new Material(Shader.Find("Standard"));
        material.name = "Map Preview Material";
        material.color = color;
        material.SetFloat("_Mode", 3f);
        material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetInt("_ZWrite", 0);
        material.DisableKeyword("_ALPHATEST_ON");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.renderQueue = 3000;
        return material;
    }

    static void ApplyMaterial(GameObject target, Material material)
    {
        Renderer renderer = target.GetComponent<Renderer>();

        if (renderer != null)
            renderer.sharedMaterial = material;
    }

    [Serializable]
    public class WorldData
    {
        public List<ColliderData> colliders;
        public List<HeightmapData> heightmaps;
        public List<SpawnData> spawns;
    }

    [Serializable]
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

    [Serializable]
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
    }

    [Serializable]
    public class SpawnData
    {
        public string id;
        public Vector3 position;
    }
}
