# Tự động quét file nguồn của thư viện
file(GLOB_RECURSE NET_SOURCES "src/*.cpp")
file(GLOB_RECURSE NET_HEADERS "src/*.h")

# Tạo thư viện tĩnh
add_library(bit_packing STATIC ${NET_SOURCES} ${NET_HEADERS})

# Cho phép các project khác include các file trong thư mục src của nó
target_include_directories(bit_packing PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/src)

add_executable(bit_packing_test main.cpp)

target_link_libraries(bit_packing_test PRIVATE bit_packing)