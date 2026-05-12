#include "Network/CommandDispatcher.hpp"

void CommandDispatcher::registerHandler(Opcode op, Handler handler) {
    if (!handler) return;
    auto [it, inserted] = _handlers.emplace(op, handler);
    if (!inserted) it->second = handler;
}

void CommandDispatcher::dispatch(GameCommand& command) {
    auto it = _handlers.find(command.op);
    if (it != _handlers.end()) it->second(command);
}
