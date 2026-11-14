#include <atomic>
#include <sstream>
#include <string>
#include <vector>
#include "card_data.h"

int mdpro3_server_main(int argc, char* argv[]);
static std::atomic<bool> running{false};
static std::atomic<bool> ready{false};
static std::atomic<bool> stop_requested{false};
extern const unsigned short PRO_VERSION;
extern "C" int mdpro3_protocol_version() { return PRO_VERSION; }
extern "C" int mdpro3_card_data_size() { return sizeof(card_data); }
extern "C" int mdpro3_server_ready() { return ready.load() ? 1 : 0; }

extern "C" bool mdpro3_stop_requested() { return stop_requested.load(); }
extern "C" int start_server(const char* arguments) {
    bool expected = false;
    if (!arguments || !running.compare_exchange_strong(expected, true)) return -1;
    stop_requested = false;
    ready = true;
    int result = -2;
    try {
        std::vector<std::string> values{"ygoserver"};
        std::istringstream stream(arguments);
        std::string value;
        while (stream >> value) values.push_back(value);
        // The legacy entry reads twelve required parameters and up to three seed words.
        if (values.size() != 13 && values.size() != 16) { ready = false; running = false; return -3; }
        std::vector<char*> argv;
        for (auto& item : values) argv.push_back(&item[0]);
        argv.push_back(nullptr);
        result = mdpro3_server_main(static_cast<int>(values.size()), argv.data());
    } catch (...) { result = -2; }
    ready = false;
    running = false;
    return result;
}
extern "C" void stop_server() {
    stop_requested = true;
}
