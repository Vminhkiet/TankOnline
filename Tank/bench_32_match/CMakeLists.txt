cmake_minimum_required(VERSION 3.20)
project(bench_32match)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# ── Reuse bit_packing ─────────────────────────────────────────────────────────
if(NOT TARGET bit_packing)
    add_subdirectory(${CMAKE_CURRENT_SOURCE_DIR}/../bit_packing
                     ${CMAKE_CURRENT_BINARY_DIR}/bit_packing)
endif()

# ── External dependencies (same as server_tank) ───────────────────────────────
find_package(fmt           CONFIG REQUIRED)
find_package(glm           CONFIG REQUIRED)
find_package(nlohmann_json CONFIG REQUIRED)
find_package(lz4           CONFIG)
if(NOT TARGET LZ4::lz4_static)
    if(TARGET LZ4::lz4_shared)
        add_library(LZ4::lz4_static INTERFACE IMPORTED GLOBAL)
        set_target_properties(LZ4::lz4_static PROPERTIES
            INTERFACE_LINK_LIBRARIES LZ4::lz4_shared)
    endif()
endif()
find_package(RdKafka CONFIG REQUIRED)

# ── Sources: server_tank sources EXCLUDING main.cpp and real Kafka ────────────
set(SERVER_SRC_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../server_tank/src")

file(GLOB_RECURSE SERVER_SOURCES "${SERVER_SRC_DIR}/*.cpp")

list(FILTER SERVER_SOURCES EXCLUDE REGEX ".*/server_tank/src/main\\.cpp$")
list(FILTER SERVER_SOURCES EXCLUDE REGEX ".*/Kafka/KafkaConsumer\\.cpp$")
list(FILTER SERVER_SOURCES EXCLUDE REGEX ".*/Kafka/KafkaProducer\\.cpp$")

list(APPEND SERVER_SOURCES
    "${SERVER_SRC_DIR}/Kafka/KafkaConsumer_stub.cpp"
    "${SERVER_SRC_DIR}/Kafka/KafkaProducer_stub.cpp"
)

# Benchmark main
list(APPEND SERVER_SOURCES
    "${CMAKE_CURRENT_SOURCE_DIR}/bench_32match.cpp"
)

# ── Executable ────────────────────────────────────────────────────────────────
add_executable(bench_32match ${SERVER_SOURCES})

target_include_directories(bench_32match PRIVATE
    "${CMAKE_CURRENT_SOURCE_DIR}/../server_tank/include"
    "${CMAKE_CURRENT_SOURCE_DIR}/../bit_packing/src"
)

target_compile_definitions(bench_32match PRIVATE
    FMT_HEADER_ONLY
    _WIN32_WINNT=0x0601
    _CRT_SECURE_NO_WARNINGS
)

if(MSVC)
    target_compile_options(bench_32match PRIVATE /utf-8 /O2)
endif()

target_link_libraries(bench_32match PRIVATE
    ws2_32
    winmm
    bit_packing
    nlohmann_json::nlohmann_json
    RdKafka::rdkafka
    RdKafka::rdkafka++
)

# ── Copy assets (bench_spread.json) to output directory ──────────────────────
add_custom_command(TARGET bench_32match POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E copy_directory
        "${CMAKE_CURRENT_SOURCE_DIR}/../server_tank/assests"
        "$<TARGET_FILE_DIR:bench_32match>/assests"
    COMMENT "Copying assets to output dir"
)
