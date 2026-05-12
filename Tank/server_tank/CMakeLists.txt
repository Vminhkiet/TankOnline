if (POLICY CMP0141)
  cmake_policy(SET CMP0141 NEW)
  set(CMAKE_MSVC_DEBUG_INFORMATION_FORMAT "$<IF:$<AND:$<C_COMPILER_ID:MSVC>,$<CXX_COMPILER_ID:MSVC>>,$<$<CONFIG:Debug,RelWithDebInfo>:EditAndContinue>,$<$<CONFIG:Debug,RelWithDebInfo>:ProgramDatabase>>")
endif()

# ── External dependencies ────────────────────────────────────────────────────
find_package(asio          CONFIG REQUIRED)
find_package(fmt           CONFIG REQUIRED)
find_package(RdKafka       CONFIG REQUIRED)
find_package(nlohmann_json CONFIG REQUIRED)

# ── Sources ──────────────────────────────────────────────────────────────────
file(GLOB_RECURSE SERVER_SOURCES "src/*.cpp")
file(GLOB_RECURSE SERVER_HEADERS "include/*.hpp" "include/*.h")

add_executable(server_tank ${SERVER_SOURCES} ${SERVER_HEADERS})

# ── Include paths ────────────────────────────────────────────────────────────
target_include_directories(server_tank PRIVATE
    include
    D:/Library/glm-1.0.1-light
)

# ── Link libraries ───────────────────────────────────────────────────────────
target_link_libraries(server_tank PRIVATE
    ws2_32
    winmm          # timeBeginPeriod / timeEndPeriod — 1 ms timer resolution
    fmt::fmt
    bit_packing
    RdKafka::rdkafka++
    nlohmann_json::nlohmann_json
)

# ── Copy assets to output dir ────────────────────────────────────────────────
add_custom_command(TARGET server_tank POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E copy_directory
    "${CMAKE_CURRENT_SOURCE_DIR}/assests"
    "$<TARGET_FILE_DIR:server_tank>/assests"
)
