cmake_minimum_required(VERSION 3.20)
project(server_tank)

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
find_package(lz4           CONFIG)
# Workaround: rdkafka's cmake interface references LZ4::lz4_static but vcpkg
# only ships a shared lz4 (LZ4::lz4_shared). Create a shim so the linker resolves it.
if(NOT TARGET LZ4::lz4_static)
    if(TARGET LZ4::lz4_shared)
        add_library(LZ4::lz4_static INTERFACE IMPORTED GLOBAL)
        set_target_properties(LZ4::lz4_static PROPERTIES
            INTERFACE_LINK_LIBRARIES LZ4::lz4_shared)
    elseif(DEFINED VCPKG_INSTALLED_DIR AND DEFINED VCPKG_TARGET_TRIPLET)
        add_library(LZ4::lz4_static UNKNOWN IMPORTED GLOBAL)
        set_target_properties(LZ4::lz4_static PROPERTIES
            IMPORTED_LOCATION         "${VCPKG_INSTALLED_DIR}/${VCPKG_TARGET_TRIPLET}/lib/lz4.lib"
            IMPORTED_LOCATION_RELEASE "${VCPKG_INSTALLED_DIR}/${VCPKG_TARGET_TRIPLET}/lib/lz4.lib"
            IMPORTED_LOCATION_DEBUG   "${VCPKG_INSTALLED_DIR}/${VCPKG_TARGET_TRIPLET}/debug/lib/lz4d.lib")
    endif()
endif()
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
    PROFILING_SINGLE_CORE    # 1 worker thread + 60s session timeout
    # DEBUG_NETWORK          # uncomment to restore per-packet recv logs
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
