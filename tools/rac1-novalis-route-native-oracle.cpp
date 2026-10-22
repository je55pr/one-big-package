// Research adapter. The authorized original MIPS owns mechanism transitions.
// Hooks replace sound/effect/attachment presentation, never state or motion.
#include <runtime/ee/ee_interpreter.h>
#include <runtime/ee/ps2_fpu.h>
#include <runtime/kernel/ee_memory.h>
#include <cstring>
#include <fstream>
#include <iomanip>
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
constexpr uint32_t M = 0x1000000, PV = 0x1001000, BOX = 0x1020000;
template<class T> void put(uint32_t a, T v) { std::memcpy(ram.data() + a, &v, sizeof(T)); }
template<class T> T get(uint32_t a) { T v; std::memcpy(&v, ram.data() + a, sizeof(T)); return v; }
void load(const char* file, uint32_t at) {
    std::ifstream stream(file, std::ios::binary);
    std::vector<char> bytes((std::istreambuf_iterator<char>(stream)), {});
    if (bytes.empty() || bytes.size() > 32 * 1024 * 1024) std::exit(2);
    std::memcpy(ram.data() + at, bytes.data(), bytes.size());
}
int main(int argc, char** argv) {
    if (argc != 5 && argc != 6) return 2;
    load(argv[1], 0); load(argv[2], PV); load(argv[3], BOX);
    const int cls = std::stoi(argv[4]);
    const bool door = cls == 768 || cls == 769, rock = cls == 701;
    const uint32_t fn = door ? 0x2fed68 : rock ? 0x2f9080 : 0x2f95c0;
    float x, y, z, yaw;
    if (!(std::cin >> x >> y >> z >> yaw)) return 2;
    put(M + 0x10, x); put(M + 0x14, y); put(M + 0x18, z); put(M + 0x48, yaw);
    put(M + 0x78, PV); put<int16_t>(M + 0xa6, cls); put<uint16_t>(M + 0xb2, 0);
    put(0x1600ec, BOX); put<int32_t>(0x15ed84, 1);
    put(0x15ed64, 1.0f); put(0x15ed6c, 1.0f / 60.0f); put(0x15ed70, (1.0f / 60.0f) / 60.0f);
    ram[0x1bbb04] = argc == 6 ? 1 : 0; put<uint32_t>(0x14c290, 0);
    ee_detail::g_ram = ram.data(); ee_detail::g_spr = spr.data();
    ee_detail::g_fast_last = ee_detail::g_read_last = ram.size() - 1;
    EeInterpreter ee;
    auto noop = [](EeCpu& c) { c.set32(2, 0); };
    for (uint32_t a : {0x2755f8u, 0x2f9000u, 0x317070u, 0x23cf98u}) ee.set_hook(a, noop);
    int sound = -1;
    ee.set_hook(0x2a1618, [&](EeCpu& c) { sound = c.s32(4); c.set32(2, 0); });
    ee.set_hook(0x26c930, [](EeCpu& c) { c.set32(2, 99); }); // suppress unrelated random dust
    ee.set_hook(0x26c970, [](EeCpu& c) { c.set32(2, c.r32(4)); }); // deterministic wait: lower bound
    std::cout << std::setprecision(9);
    float hx, hy, hz, cx, cy, cz;
    int run;
    while (std::cin >> hx >> hy >> hz >> cx >> cy >> cz >> run) {
        put(0x13f3d0, hx); put(0x13f3d4, hy); put(0x13f3d8, hz);
        put(0x167240, cx); put(0x167244, cy); put(0x167248, cz); put(0x1413dc, run);
        sound = -1;
        ps2fpu::enter_ee_rounding();
        if (!ee.call(fn, {M}, 0x166c00, 500000)) { std::cerr << ee.error() << '\n'; return 1; }
        // Text input/output is host IO, not EE arithmetic. Parsing decimal
        // round-trip coordinates in chop mode would silently alter the facts.
        std::fesetround(FE_TONEAREST);
        std::cout << unsigned(ram[M + 0x20]) << ' ' << unsigned(ram[M + 0xbc]) << ' '
            << get<float>(M+0x10) << ' ' << get<float>(M+0x14) << ' ' << get<float>(M+0x18) << ' '
            << (door ? get<float>(PV+0x10) : rock ? get<float>(PV+0x68) : get<float>(PV+0xac)) << ' '
            << (door ? 0 : get<int16_t>(PV+(rock ? 0x8a : 0xa6))) << ' ' << sound << '\n';
    }
}
