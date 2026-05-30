cmake_minimum_required(VERSION 3.20)
project(bench_worst_case)

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

# Exclude: server_tank main.cpp (benchmark has its own main)
# Exclude: real Kafka implementations (use stubs — benchmark never starts Kafka)
list(FILTER SERVER_SOURCES EXCLUDE REGEX ".*/server_tank/src/main\\.cpp$")
list(FILTER SERVER_SOURCES EXCLUDE REGEX ".*/Kafka/KafkaConsumer\\.cpp$")
list(FILTER SERVER_SOURCES EXCLUDE REGEX ".*/Kafka/KafkaProducer\\.cpp$")

# Include: Kafka stubs (no-op implementations)
list(APPEND SERVER_SOURCES
    "${SERVER_SRC_DIR}/Kafka/KafkaConsumer_stub.cpp"
    "${SERVER_SRC_DIR}/Kafka/KafkaProducer_stub.cpp"
)

# ── bench_worst_case (one-shot, original) ─────────────────────────────────────
set(WC_SOURCES ${SERVER_SOURCES})
list(APPEND WC_SOURCES "${CMAKE_CURRENT_SOURCE_DIR}/bench_worst_case_spread.cpp")
add_executable(bench_worst_case ${WC_SOURCES})

target_include_directories(bench_worst_case PRIVATE
    "${CMAKE_CURRENT_SOURCE_DIR}/../server_tank/include"
    "${CMAKE_CURRENT_SOURCE_DIR}/../bit_packing/src"
)
target_compile_definitions(bench_worst_case PRIVATE
    FMT_HEADER_ONLY _WIN32_WINNT=0x0601 _CRT_SECURE_NO_WARNINGS PROFILING_SINGLE_CORE)
if(MSVC)
    target_compile_options(bench_worst_case PRIVATE /utf-8 /O2)
endif()
target_link_libraries(bench_worst_case PRIVATE
    ws2_32 winmm bit_packing nlohmann_json::nlohmann_json
    RdKafka::rdkafka RdKafka::rdkafka++)

# ── bench_wc_live (infinite loop, --players=N, dùng với bench_wc_live_agent.py) ─
set(WCL_SOURCES ${SERVER_SOURCES})
list(APPEND WCL_SOURCES "${CMAKE_CURRENT_SOURCE_DIR}/bench_wc_live.cpp")
add_executable(bench_wc_live ${WCL_SOURCES})

target_include_directories(bench_wc_live PRIVATE
    "${CMAKE_CURRENT_SOURCE_DIR}/../server_tank/include"
    "${CMAKE_CURRENT_SOURCE_DIR}/../bit_packing/src"
)
target_compile_definitions(bench_wc_live PRIVATE
    FMT_HEADER_ONLY _WIN32_WINNT=0x0601 _CRT_SECURE_NO_WARNINGS PROFILING_SINGLE_CORE)
if(MSVC)
    target_compile_options(bench_wc_live PRIVATE /utf-8 /O2)
endif()
target_link_libraries(bench_wc_live PRIVATE
    ws2_32 winmm bit_packing nlohmann_json::nlohmann_json
    RdKafka::rdkafka RdKafka::rdkafka++)

# ── Copy assets to both output directories ───────────────────────────────────
foreach(TGT bench_worst_case bench_wc_live)
    add_custom_command(TARGET ${TGT} POST_BUILD
        COMMAND ${CMAKE_COMMAND} -E copy_directory
            "${CMAKE_CURRENT_SOURCE_DIR}/../server_tank/assests"
            "$<TARGET_FILE_DIR:${TGT}>/assests"
        COMMENT "Copying assets (including bench_spread.json) to ${TGT} output dir"
    )
endforeach()
