#pragma once
#include <winsock2.h>

constexpr int MAX_SIZE = 1024;

enum class IoOperation {
	READ,
	WRITE
};

struct IoContext {
	WSAOVERLAPPED ov;
	WSABUF wsaBuf;
    alignas(4) char buffer[MAX_SIZE];
	sockaddr_in clientAddr;
	int clientAddrLen;
	IoOperation op;

    IoContext() {
        reset(IoOperation::READ);
    }

    void reset(IoOperation op) {
        this->op = op;

        ZeroMemory(&ov, sizeof(ov));

        wsaBuf.buf = buffer;
        wsaBuf.len = MAX_SIZE;

        ZeroMemory(&clientAddr, sizeof(clientAddr));
        clientAddrLen = sizeof(clientAddr);
    }
};