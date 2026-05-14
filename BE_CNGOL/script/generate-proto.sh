#!/bin/bash

# Thư mục gốc của project
PROJECT_ROOT=$(dirname $(dirname $(realpath "$0")))

# Thư mục chứa các file .proto
PROTO_DIR="${PROJECT_ROOT}/common/proto"

# Thư mục output cho mã Go
GO_OUT_DIR="${PROJECT_ROOT}/go-game-servers/pkg/protos"

# Thư mục output cho mã Java (sẽ dùng sau này)
JAVA_OUT_DIR="${PROJECT_ROOT}/java-meta-services/src/main/java" # Hoặc thư mục package Java cụ thể

# Thư mục output cho mã C# (sẽ dùng cho Unity)
CSHARP_OUT_DIR="${PROJECT_ROOT}/unity-client/Assets/Scripts/Proto" # Giả định Unity project có cấu trúc này

echo "Cleaning old generated Go code..."
rm -rf "${GO_OUT_DIR}/meta"
rm -rf "${GO_OUT_DIR}/game"

# Tạo thư mục đầu ra cho Go nếu chưa có
mkdir -p "${GO_OUT_DIR}/meta"
mkdir -p "${GO_OUT_DIR}/game"

# Sinh mã Go cho meta_services.proto (sẽ vào thư mục meta/)
echo "Generating Go code for meta_services.proto..."
protoc \
    --proto_path="${PROTO_DIR}" \
    --go_out="${GO_OUT_DIR}/meta" \
    --go_opt=paths=source_relative \
    --go-grpc_out="${GO_OUT_DIR}/meta" \
    --go-grpc_opt=paths=source_relative \
    "${PROTO_DIR}/meta_services.proto"

# Sinh mã Go cho game_messages.proto (sẽ vào thư mục game/)
echo "Generating Go code for game_messages.proto..."
protoc \
    --proto_path="${PROTO_DIR}" \
    --go_out="${GO_OUT_DIR}/game" \
    --go_opt=paths=source_relative \
    "${PROTO_DIR}/game_messages.proto"

# Sinh mã Java (cho các dịch vụ meta) - Sẽ cần sau này
echo "Generating Java code from .proto files (for Meta Services)..."
# protoc \
#    --proto_path="${PROTO_DIR}" \
#    --java_out="${JAVA_OUT_DIR}" \
#    "${PROTO_DIR}"/meta_services.proto

# Sinh mã C# (cho Unity Client) - Sẽ cần sau này
echo "Generating C# code from .proto files (for Unity Client)..."
# protoc \
#    --proto_path="${PROTO_DIR}" \
#    --csharp_out="${CSHARP_OUT_DIR}" \
#    "${PROTO_DIR}"/game_messages.proto


echo "Proto code generation complete."