#include <atomic>
#include <chrono>
#include <iostream>
#include <thread>
extern "C" int mdpro3_protocol_version();
extern "C" int mdpro3_card_data_size();
extern "C" int mdpro3_server_ready();
extern "C" int start_server(const char*);
extern "C" void stop_server();
int main() {
    if (mdpro3_protocol_version() != 0x1362 || mdpro3_card_data_size() != 80) return 1;
    for (int attempt = 0; attempt < 4; ++attempt) {
        std::atomic<int> result{-999};
        std::thread server([&] { result = start_server("0 0 0 0 F T F 8000 5 1 180 0"); });
        if (attempt < 3) std::this_thread::sleep_for(std::chrono::seconds(2));
        else {
            for (int wait = 0; wait < 3000 && !mdpro3_server_ready() && result == -999; ++wait)
                std::this_thread::sleep_for(std::chrono::milliseconds(1));
            if (!mdpro3_server_ready()) { std::cerr << "Native entry did not become ready\n"; std::_Exit(5); }
        }
        if (result != -999) { server.join(); return 2; }
        stop_server();
        for (int wait = 0; wait < 50 && result == -999; ++wait)
            std::this_thread::sleep_for(std::chrono::milliseconds(100));
        if (result == -999) { std::cerr << "Server stop timed out\n"; std::_Exit(3); }
        server.join();
        if (result != 0) return 4;
    }
    std::cout << "PASS: native ABI 80 bytes, protocol 0x1362, three start/stop cycles plus immediate-stop handshake. Duel gameplay remains untested.\n";
}
