if (POLICY CMP0141)
  cmake_policy(SET CMP0141 NEW)
  set(CMAKE_MSVC_DEBUG_INFORMATION_FORMAT "$<IF:$<AND:$<C_COMPILER_ID:MSVC>,$<CXX_COMPILER_ID:MSVC>>,$<$<CONFIG:Debug,RelWithDebInfo>:EditAndContinue>,$<$<CONFIG:Debug,RelWithDebInfo>:ProgramDatabase>>")
endif()

find_package(fmt CONFIG REQUIRED)

# ── Game-logic sources reused from server_tank (no Network / Core / Kafka) ───
set(GAME_SOURCES
    ../server_tank/src/Entities/Bullet.cpp
    ../server_tank/src/Entities/Tank.cpp
    ../server_tank/src/Physics/PhysicsWorld.cpp
    ../server_tank/src/Physics/UniformGrid.cpp
    ../server_tank/src/Utils/Logger.cpp
    ../server_tank/src/World/GameMap.cpp
    ../server_tank/src/World/GameWorld.cpp
)

# ── Baseline-specific sources ────────────────────────────────────────────────
set(BASELINE_SOURCES
    src/main.cpp
    src/UdpServer.cpp
    src/SessionTable.cpp
    src/GameLoop.cpp
)

add_executable(server_baseline ${BASELINE_SOURCES} ${GAME_SOURCES})

target_include_directories(server_baseline PRIVATE
    src
    ../server_tank/include
    D:/Library/glm-1.0.1-light
)

target_link_libraries(server_baseline PRIVATE
    ws2_32
    winmm
    fmt::fmt
    bit_packing
)

# Copy map assets from server_tank
add_custom_command(TARGET server_baseline POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E copy_directory
    "${CMAKE_CURRENT_SOURCE_DIR}/../server_tank/assests"
    "$<TARGET_FILE_DIR:server_baseline>/assests"
)
