#pragma once
#include <unordered_map>
#include <functional>
#include "Network/GameCommand.hpp"
#include "Network/Opcode.hpp"

class CommandDispatcher {
public:
    using Handler = std::function<void(GameCommand&)>;

    void registerHandler(Opcode op, Handler handler);
    void dispatch(GameCommand& command);

private:
    std::unordered_map<Opcode, Handler> _handlers;
};
