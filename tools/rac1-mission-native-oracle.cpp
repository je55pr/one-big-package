// Optional research oracle: original MIPS owns actor, talk and admission writes.
// Hooks replace presentation, streaming and checkpoint/file IO, not discovery.
#include <runtime/ee/ee_interpreter.h>
#include <runtime/ee/ps2_fpu.h>
#include <runtime/kernel/ee_memory.h>
#include <cstring>
#include <fstream>
#include <iostream>
#include <string>
#include <vector>

namespace rac1 {
namespace ee_detail { u8* g_ram = nullptr; u8* g_spr = nullptr; u32 g_fast_last = 0, g_read_last = 0; const u8* g_jseen = nullptr; }
std::vector<u8> ram(64 * 1024 * 1024), spr(0x4000);
u8* ee_get_ram_base() { return ram.data(); }
u8* ee_get_scratchpad_base() { return spr.data(); }
void* ee_ram_ptr_slow(u32 a, bool, u32) { return a >= SPR_BASE ? spr.data() + a - SPR_BASE : ram.data() + (a & 0x1ffffff); }
void ee_debug_backtrace() {}
void vu_execute_xgkick(VUState&, uint32_t, std::vector<GsPrimitive>&) {}
}
using namespace rac1;
constexpr uint32_t M = 0x1000000, PV = 0x1001000;
void word(uint32_t a, uint32_t v) { std::memcpy(ram.data() + a, &v, 4); }
void real(uint32_t a, float v) { std::memcpy(ram.data() + a, &v, 4); }
int16_t half(uint32_t a) { int16_t v; std::memcpy(&v, ram.data() + a, 2); return v; }
uint32_t word_at(uint32_t a) { uint32_t v; std::memcpy(&v, ram.data() + a, 4); return v; }

int main(int argc, char** argv) {
    if (argc != 4) return 2;
    std::ifstream image(argv[1], std::ios::binary);
    image.read(reinterpret_cast<char*>(ram.data()), 32 * 1024 * 1024);
    if (image.gcount() != 32 * 1024 * 1024) return 2;
    std::ifstream pvar(argv[2], std::ios::binary);
    std::vector<char> data((std::istreambuf_iterator<char>(pvar)), {});
    if (data.empty() || data.size() > 0x1000) return 2;
    std::memcpy(ram.data() + PV, data.data(), data.size());
    ee_detail::g_ram = ram.data(); ee_detail::g_spr = spr.data();
    ee_detail::g_fast_last = ee_detail::g_read_last = ram.size() - 1;
    ps2fpu::enter_ee_rounding();
    EeInterpreter ee;
    const std::string mode = argv[3];
    const bool plumber = mode == "plumber", infobot = mode == "infobot";
    const uint32_t fn = plumber ? 0x2ff118 : infobot ? 0x2d5de8 : 0x2d9dc8;
    const uint32_t talk = PV + (plumber ? 0 : 0x20), step = plumber ? 0x27b550 : 0x266970;
    const uint32_t slots = plumber ? 0x179638 : 0x1791b8;
    int retired = 0, scene_kind = -1, scene_id = -1, box_inside = 0;
    auto noop = [](EeCpu& c) { c.set32(2, 0); };
    for (uint32_t a : {0x235908u, 0x237718u, 0x24afa0u, 0x24cdb0u, 0x2777d8u,
                       0x2ff028u, 0x2783a8u, 0x29ac10u, 0x284e50u, 0x261448u, 0x24d430u,
                       0x262d38u, 0x277c38u, 0x2d6cd0u, 0x2d6bf0u}) ee.set_hook(a, noop);
    for (uint32_t a : {0x203ad8u, 0x2259e8u})
        ee.set_hook(a, [](EeCpu& c) { c.set32(2, 0x1004000); }); // empty presentation text
    ee.set_hook(0x116248, [](EeCpu& c) { ram[c.r32(4)] = 0; c.set32(2, 0); }); // sprintf
    ee.set_hook(0x25f960, [&](EeCpu& c) { c.set32(2, box_inside); });
    for (uint32_t a : {0x24eb68u, 0x2636c0u})
        ee.set_hook(a, [&](EeCpu& c) { ++retired; c.set32(2, 0); });
    for (uint32_t a : {0x298840u, 0x2ac330u}) ee.set_hook(a, [&](EeCpu& c) {
        scene_kind = 0; scene_id = c.s32(4); word(0x15f5c4, 2); c.set32(2, 0);
    });
    for (uint32_t a : {0x299460u, 0x2acf50u}) ee.set_hook(a, [&](EeCpu& c) {
        scene_kind = 1; scene_id = c.s32(4); word(0x15f5c4, 1); c.set32(2, 0);
    });
    word(M + 0x78, PV); ram[M + 0x53] = 1; ram[M + 0xb0] = 0xff;
    word(0x15ed84, plumber ? 1 : 0); word(0x15f5c4, 0);
    word(slots + (plumber ? 3 : 1) * 4, M);
    word(0x13d5b0 + 16 + 12, plumber ? 0 : 1); ram[0x13d390] = 1;
    word(0x15ed98, 0); word(0x1415f8, 4); word(0x1413d4, 0);
    word(plumber ? 0x179590 : 0x179110, 0);
    if (plumber) { ram[0x13dd41] = 1; word(0x13d510, 1); }
    if (infobot) {
        // A controlled unlinked active variant isolates the recovered pickup arm.
        word(PV, 1); word(PV + 4, 4); word(PV + 8, -1);
        word(PV + 0x50, -1); word(PV + 0x54, -1); word(PV + 0x58, -1);
        for (int i = 0; i < 8; ++i) word(PV + 0x70 + 4 * i, -1);
        ram[PV + 12] = ram[PV + 13] = ram[PV + 14] = 0xff;
    }
    auto call = [&](uint32_t address, std::initializer_list<uint32_t> args) {
        if (!ee.call(address, args, 0x166c00, 500000)) {
            std::cerr << std::hex << address << ": " << ee.error() << '\n'; std::exit(1);
        }
    };
    std::string command;
    float x, y, z, yaw; int health, player_state, triangle, bolts;
    while (std::cin >> command >> x >> y >> z >> yaw >> health >> player_state >> triangle >> bolts >> box_inside) {
        real(0x13f3d0, x); real(0x13f3d4, y); real(0x13f3d8, z); real(0x13f3e8, yaw);
        word(0x1415f8, health); word(0x1413d4, player_state); word(0x13cae4, triangle ? 0x10 : 0);
        if (bolts >= 0) word(0x15ed98, bolts);
        word(0x15f5cc, word_at(0x15f5cc) + 1);
        if (command == "complete") {
            scene_kind = scene_id = -1; word(0x15f5c4, 0);
            call(step, {M, talk, 1});
        } else if (command == "entry") {
            word(0x15ed84, word_at(0x15f5c0));
            if (word_at(0x15ed84) != 0) call(0x2607d0, {word_at(0x15ed84)});
        } else call(fn, {M});
        int count = 0; for (int i = 0; i < 20; ++i) if (ram[0x13dd40 + i]) ++count;
        std::cout << int(ram[M + 0x20]) << ' ' << half(talk + 0x36) << ' ' << half(talk + 4)
                  << ' ' << scene_kind << ' ' << scene_id << ' ' << word_at(0x15f5c0)
                  << ' ' << word_at(0x15f5d8) << ' ' << count << ' ' << word_at(0x15ed98)
                  << ' ' << retired << '\n';
    }
}
