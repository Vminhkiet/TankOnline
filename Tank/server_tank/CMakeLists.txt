if (POLICY CMP0141)
  cmake_policy(SET CMP0141 NEW)
  set(CMAKE_MSVC_DEBUG_INFORMATION_FORMAT "$<IF:$<AND:$<C_COMPILER_ID:MSVC>,$<CXX_COMPILER_ID:MSVC>>,$<$<CONFIG:Debug,RelWithDebInfo>:EditAndContinue>,$<$<CONFIG:Debug,RelWithDebInfo>:ProgramDatabase>>")
endif()

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# bit_packing
if(NOT TARGET bit_packing)
    add_subdirectory(${CMAKE_CURRENT_SOURCE_DIR}/../bit_packing
                     ${CMAKE_CURRENT_BINARY_DIR}/bit_packing)
endif()

# ── External dependencies ────────────────────────────────────────────────────
find_package(asio          CONFIG REQUIRED)
find_package(fmt           CONFIG REQUIRED)
find_package(glm           CONFIG REQUIRED)
find_package(nlohmann_json CONFIG REQUIRED)
find_package(RdKafka       CONFIG REQUIRED)

# ── Sources (exclude stubs — using real Kafka implementation) ────────────────
file(GLOB_RECURSE SERVER_SOURCES "src/*.cpp")
list(FILTER SERVER_SOURCES EXCLUDE REGEX ".*/Kafka/Kafka(Consumer|Producer)_stub\\.cpp$")
file(GLOB_RECURSE SERVER_HEADERS "include/*.hpp" "include/*.h")

add_executable(server_tank ${SERVER_SOURCES} ${SERVER_HEADERS})

# ── Include paths ─────────────────────────────────────────────────────────────
target_include_directories(server_tank PRIVATE
    include
    ${CMAKE_CURRENT_SOURCE_DIR}/../bit_packing/src
    ${VCPKG_INCLUDE}
)

# ── Compile definitions ───────────────────────────────────────────────────────
target_compile_definitions(server_tank PRIVATE
    FMT_HEADER_ONLY          # fmt header-only (không cần .lib)
    _WIN32_WINNT=0x0601      # Windows 7+
    _CRT_SECURE_NO_WARNINGS  # tắt cảnh báo getenv/strcpy trên MSVC
)

# ── Compile options ───────────────────────────────────────────────────────────
if(MSVC)
    target_compile_options(server_tank PRIVATE /utf-8)  # fmt yêu cầu UTF-8
endif()

# ── Link libraries ────────────────────────────────────────────────────────────
target_link_libraries(server_tank PRIVATE
    ws2_32
    winmm
    bit_packing
    nlohmann_json::nlohmann_json
    RdKafka::rdkafka
    RdKafka::rdkafka++
)

# ── Copy assets to output dir ─────────────────────────────────────────────────
add_custom_command(TARGET server_tank POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E copy_directory
    "${CMAKE_CURRENT_SOURCE_DIR}/assests"
    "$<TARGET_FILE_DIR:server_tank>/assests"
)
