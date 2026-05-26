#include "World/GameMap.hpp"
#include <fstream>
#include <iostream>
#include "Utils/json.hpp"
#include "Utils/Logger.hpp"
#include <glm/glm.hpp>
#include <glm/gtc/quaternion.hpp>

using json = nlohmann::json;

bool GameMap::LoadFromFile(const std::string& filepath, PhysicsWorld& physicsWorld) {
    std::ifstream file(filepath);
    if (!file.is_open()) {
        std::cerr << "[GameMap] Khong the mo file: " << filepath << "\n";
        return false;
    }

    json data;
    try { file >> data; }
    catch (json::parse_error& e) { return false; }

    int staticId = 10000;

    if (data.contains("colliders")) {
        for (const auto& item : data["colliders"]) {
            std::string type = item["type"];
            glm::vec3 center(item["center"]["x"], item["center"]["y"], item["center"]["z"]);

            glm::vec3 rotDeg(item["rotation"]["x"], item["rotation"]["y"], item["rotation"]["z"]);
            glm::quat q(glm::radians(rotDeg));

            if (type == "box") {
                OBBCollider box;
                box.entityId  = staticId++;
                box.isActive  = true;
                box.isWalkable = item.value("walkable", false);
                box.center = { center.x, center.y, center.z };
                box.extents = {
                    (float)item["size"]["x"] * 0.5f,
                    (float)item["size"]["y"] * 0.5f,
                    (float)item["size"]["z"] * 0.5f
                };

                glm::vec3 ax = q * glm::vec3(1, 0, 0);
                glm::vec3 ay = q * glm::vec3(0, 1, 0);
                glm::vec3 az = q * glm::vec3(0, 0, 1);

                box.axisX = { ax.x, ax.y, ax.z };
                box.axisY = { ay.x, ay.y, ay.z };
                box.axisZ = { az.x, az.y, az.z };

                physicsWorld.addBox(box);
            }
            else if (type == "capsule") {
                CapsuleCollider cap;
                cap.entityId = staticId++;
                cap.isActive = true;
                cap.radius = item["radius"];

                float height = item["height"];
                float coreHalfLength = (height - 2.0f * cap.radius) * 0.5f;
                glm::vec3 upDir = q * glm::vec3(0, 1, 0);

                glm::vec3 pA = center + upDir * coreHalfLength;
                glm::vec3 pB = center - upDir * coreHalfLength;

                cap.pA = { pA.x, pA.y, pA.z };
                cap.pB = { pB.x, pB.y, pB.z };

                physicsWorld.addCapsule(cap);
            }
        }
    }

    _tankNameToIndex.clear();
    if (data.contains("tanks")) {
        uint8_t index = 0;
        for (const auto& t : data["tanks"]) {
            std::string name = t.value("name", "BULLDOG");
            _tankNameToIndex[name] = index++;
            if (t.contains("collider_extents")) {
                TankConfig cfg;
                cfg.extentX = t["collider_extents"]["x"].get<float>();
                cfg.extentY = t["collider_extents"]["y"].get<float>();
                cfg.extentZ = t["collider_extents"]["z"].get<float>();
                if (t.contains("collider_offset")) {
                    cfg.offsetX = t["collider_offset"]["x"].get<float>();
                    cfg.offsetY = t["collider_offset"]["y"].get<float>();
                    cfg.offsetZ = t["collider_offset"]["z"].get<float>();
                } else {
                    cfg.offsetX = 0.f;
                    cfg.offsetY = cfg.extentY;
                    cfg.offsetZ = 0.f;
                }
                _tankConfigs[name] = cfg;
            }
        }
    } else if (data.contains("tank")) {
        // Fallback for old world.json format
        const auto& t = data["tank"];
        if (t.contains("collider_extents")) {
            TankConfig cfg;
            cfg.extentX = t["collider_extents"]["x"].get<float>();
            cfg.extentY = t["collider_extents"]["y"].get<float>();
            cfg.extentZ = t["collider_extents"]["z"].get<float>();
            if (t.contains("collider_offset")) {
                cfg.offsetX = t["collider_offset"]["x"].get<float>();
                cfg.offsetY = t["collider_offset"]["y"].get<float>();
                cfg.offsetZ = t["collider_offset"]["z"].get<float>();
            } else {
                cfg.offsetX = 0.f;
                cfg.offsetY = cfg.extentY;
                cfg.offsetZ = 0.f;
            }
            _tankConfigs["BULLDOG"] = cfg;
        }
    }

    if (data.contains("bullet")) {
        const auto& b = data["bullet"];
        if (b.contains("collider_radius"))
            _bulletConfig.radius    = b["collider_radius"].get<float>();
        if (b.contains("hit_radius"))
            _bulletConfig.hitRadius = b["hit_radius"].get<float>();
    }

    LOG_INFO("GameMap: loaded {} tank configs, bullet_r={:.3f} hit_r={:.3f}",
             _tankConfigs.size(), _bulletConfig.radius, _bulletConfig.hitRadius);

    if (data.contains("spawns")) {
        for (const auto& sp : data["spawns"]) {
            SpawnPoint s;
            s.id = std::stoi(sp["id"].get<std::string>());
            s.x  = sp["position"]["x"].get<float>();
            s.y  = sp["position"]["y"].get<float>();
            s.z  = sp["position"]["z"].get<float>();
            _spawnPoints.push_back(s);
        }
    }

    if (data.contains("bushes")) {
        for (const auto& item : data["bushes"]) {
            glm::vec3 center(item["center"]["x"].get<float>(), item["center"]["y"].get<float>(), item["center"]["z"].get<float>());
            glm::vec3 extents(
                item["size"]["x"].get<float>() * 0.5f,
                item["size"]["y"].get<float>() * 0.5f,
                item["size"]["z"].get<float>() * 0.5f
            );
            Bush b;
            b.min = { center.x - extents.x, center.y - extents.y, center.z - extents.z };
            b.max = { center.x + extents.x, center.y + extents.y, center.z + extents.z };
            _bushes.push_back(b);
        }
    }

    if (data.contains("heightmaps")) {
        for (const auto& hm : data["heightmaps"]) {
            try {
                HeightLayer layer;
                layer.width   = hm["resolutionX"];
                layer.height  = hm["resolutionZ"];
                layer.originX = hm["origin"]["x"].get<float>();
                layer.originZ = hm["origin"]["z"].get<float>();
                layer.baseY   = hm["origin"]["y"].get<float>();
                layer.sizeX   = hm["size"]["x"].get<float>();
                layer.sizeZ   = hm["size"]["z"].get<float>();
                layer.data    = hm["heights"].get<std::vector<float>>();
                if (layer.width > 1 && layer.height > 1 && !layer.data.empty()) {
                    LOG_INFO("GameMap: heightmap layer '{}' ({}x{} origin=({:.1f},{:.1f}) size=({:.1f},{:.1f}) Y:[{:.2f},{:.2f}])",
                             hm.value("name","?"), layer.width, layer.height,
                             layer.originX, layer.originZ, layer.sizeX, layer.sizeZ,
                             *std::min_element(layer.data.begin(), layer.data.end()),
                             *std::max_element(layer.data.begin(), layer.data.end()));
                    _layers.push_back(std::move(layer));
                }
            } catch (const std::exception& ex) {
                LOG_ERR("GameMap: skipping heightmap '{}': {}", hm.value("name","?"), ex.what());
            }
        }
    }

    return true;
}

bool GameMap::HeightLayer::covers(float x, float z) const {
    return x >= originX && x <= originX + sizeX
        && z >= originZ && z <= originZ + sizeZ;
}

float GameMap::HeightLayer::sample(float x, float z) const {
    if (sizeX == 0.f || sizeZ == 0.f) return baseY;
    float gx = (x - originX) / sizeX * (width  - 1);
    float gz = (z - originZ) / sizeZ * (height - 1);
    gx = std::max(0.f, std::min(gx, (float)(width  - 1)));
    gz = std::max(0.f, std::min(gz, (float)(height - 1)));
    int x0 = (int)gx, z0 = (int)gz;
    int x1 = std::min(x0+1, width-1), z1 = std::min(z0+1, height-1);
    float fx = gx-x0, fz = gz-z0;
    return data[z0*width+x0]*(1-fx)*(1-fz) + data[z0*width+x1]*fx*(1-fz)
         + data[z1*width+x0]*(1-fx)*fz     + data[z1*width+x1]*fx*fz;
}

float GameMap::GetHeightAt(float x, float z) const
{
    float best = 0.f;   // default ground at y=0
    bool  found = false;
    for (const auto& layer : _layers) {
        if (!layer.covers(x, z)) continue;
        float h = layer.sample(x, z);
        if (!found || h > best) { best = h; found = true; }
    }
    return best;
}