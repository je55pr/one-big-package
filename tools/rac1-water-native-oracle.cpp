// Research adapter: executes authorized NTSC-U Novalis EE instructions.
// Hooks supply conditioned input and remove presentation/contact side effects;
// water motion, springs, stroke sampling and bobbing execute original code.
#include <runtime/ee/ee_interpreter.h>
#include <runtime/ee/ps2_fpu.h>
#include <runtime/kernel/ee_memory.h>
#include <cmath>
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
constexpr uint32_t P = 0x13f350, PAD = 0x13c940, M = 0x1000000;
template<class T> void put(uint32_t a, T v) { std::memcpy(ram.data() + a, &v, sizeof(T)); }
template<class T> T get(uint32_t a) { T v; std::memcpy(&v, ram.data() + a, sizeof(T)); return v; }
int main(int argc, char** argv) {
    if (argc != 3) return 2;
    std::ifstream in(argv[1], std::ios::binary);
    in.read(reinterpret_cast<char*>(ram.data()), 32 * 1024 * 1024);
    if (!in) return 2;
    std::memset(ram.data() + P, 0, 0x2400);
    put(P+0x2080, M); put(0x15ed60, 1.0f); put(0x15ed64, 1.0f);
    put(0x15ed68, 1.0f); put(0x15ed6c, 1.0f / 60.0f); put<uint32_t>(0x15ed70, 0x3991a2b4);
    put(PAD+0x1d4, 1); put(PAD+0x1dc, 0x79);
    ee_detail::g_ram = ram.data(); ee_detail::g_spr = spr.data();
    ee_detail::g_fast_last = ee_detail::g_read_last = ram.size() - 1;
    EeInterpreter ee;
    auto call = [&](uint32_t fn) {
        ps2fpu::enter_ee_rounding();
        if (!ee.call(fn, {}, 0x166c00, 500000)) {
            std::cerr << std::hex << fn << ": " << ee.error() << '\n'; std::exit(1);
        }
        std::fesetround(FE_TONEAREST);
    };
    std::cout << std::setprecision(10);
    std::string mode(argv[2]);
    if (mode == "entry" || mode == "entry-query" || mode == "oxygen") {
        int domain, action, apex, diving, oxygen, mask; float z, surface, depth, request, contact, pitch;
        // This hook observes the dispatcher request, not its predicate.
        int selected = -1;
        float queryStart = 0, queryEnd = 0;
        int queryCount = 0;
        ee.set_hook(0x23cf98, [&](EeCpu& c) { selected = c.s32(4); c.set32(2, 1); });
        ee.set_hook(0x236738, [](EeCpu& c) { c.set32(2, 0); });
        ee.set_hook(0x24ad98, [](EeCpu& c) { c.set32(2, 0); }); // camera request
        // Segment results are explicit external facts: no periodic water ray
        // or ceiling hit in this predicate witness.
        ee.set_hook(0x211870, [&](EeCpu& c) {
            queryStart = get<float>(c.r32(4) + 8);
            queryEnd = get<float>(c.r32(5) + 8);
            ++queryCount;
            c.set32(2, 0);
        });
        while (std::cin >> domain >> action >> apex >> diving >> oxygen >> mask >> z >> surface >> depth >> request >> contact >> pitch) {
            selected = -1;
            queryStart = queryEnd = 0; queryCount = 0;
            put(P+0x208c, domain); put(P+0x2084, action); put<int16_t>(P+0x41e, apex);
            put(P+0x2088, diving); put(P+0x22a0, oxygen); ram[0x13d4c6] = mask;
            put(P+0x88, z); put(P+0x2f0, surface); put(P+0x22a4, depth);
            put(P+0xe8, request); put(P+0x108, contact); put(P+0x94, pitch); ram[P+0x12e4] = 1;
            // Prevent the unrelated periodic segment query in entry mode.
            put(0x15f5cc, mode == "entry-query" ? 16 : 1);
            call(mode == "oxygen" ? 0x2406b0 : 0x2408e8);
            std::cout << selected << ' ' << get<int>(P+0x22a0);
            if (mode == "entry-query") std::cout << ' ' << queryStart << ' ' << queryEnd << ' ' << queryCount;
            std::cout << '\n';
        }
        return 0;
    }
    auto noop = [](EeCpu& c) { c.set32(2, 0); };
    // Audio, rumble, particle/bone presentation, and wall/geometry queries.
    for (uint32_t a : {0x236798u, 0x2a1348u, 0x22b140u, 0x22b3a8u, 0x22b5e0u,
            0x22b5f8u, 0x22b610u, 0x2840e0u, 0x2645a8u, 0x232978u}) ee.set_hook(a, noop);
    ee.set_hook(0x26c930, [](EeCpu& c) { c.set32(2, 99); });
    ee.set_hook(0x26c970, [](EeCpu& c) { c.set32(2, c.r32(4)); });
    float magnitude = 0, heading = 0;
    ee.set_hook(0x231f70, [&](EeCpu& c) {
        // P+190 is stick scale * conditioned magnitude. P+180 retains the
        // previous desired heading at neutral, just as the native producer.
        put(P+0x190, magnitude * c.fpr[12]);
        put(P+0x180, magnitude > 0 ? heading : get<float>(P+0x98));
        c.set32(2, 0);
    });
    int action, age, diving, previousAction, previousDomain, earlierDomain, buttons;
    float z, surface, scalar, yaw, pitch, yawVelocity, pitchVelocity, roll, rollVelocity;
    float bob, bobVelocity, bobSpring, waterVelocity, filteredWater, cx, cy, cz, t, advance;
    int strokeTimer, wrapped;
    while (std::cin >> action >> age >> diving >> previousAction >> previousDomain >> earlierDomain >> buttons
        >> z >> surface >> scalar >> yaw >> pitch >> yawVelocity >> pitchVelocity >> roll >> rollVelocity
        >> bob >> bobVelocity >> bobSpring >> filteredWater >> waterVelocity
        >> cx >> cy >> cz >> magnitude >> heading >> t >> advance >> strokeTimer >> wrapped) {
        put(P+0x2084, action); put(P+0x198, age); put(P+0x2088, diving);
        put(P+0x2090, previousAction); put(P+0x2094, previousDomain); put(P+0x20a0, earlierDomain);
        put(PAD+0x1a0, buttons); put(PAD+0x128, (buttons&0x40) ? 1.0f : 0.0f);
        put(PAD+0x12c, (buttons&0x80) ? 1.0f : 0.0f);
        put(P+0x88, z); put(P+0x2f0, surface); put(P+0x194, scalar);
        put(P+0x98, yaw); put(P+0x94, pitch); put(P+0x184, yawVelocity); put(P+0x8e8, pitchVelocity);
        put(P+0x90, roll); put(P+0x8e4, rollVelocity); put(P+0x180, heading);
        put(P+0x690, bob); put(P+0x694, bobVelocity); put(P+0x8fc, bobSpring);
        put(P+0x69c, filteredWater); put(P+0x6a0, waterVelocity);
        put(P+0x150, cx); put(P+0x154, cy); put(P+0x158, cz);
        put(P+0x229c, magnitude); put(P+0xaa8, t); put(P+0xaac, advance);
        put(P+0xa98, wrapped ? 2 : 0); put(P+0x698, strokeTimer);
        put(P+0x8f0, 100); put(P+0x22a0, 10000);
        call(0x2370b8);
        for (auto off : {0xe0u, 0xe4u, 0xe8u, 0x88u, 0x194u, 0x98u, 0x94u, 0x90u,
                0x184u, 0x8e8u, 0x8e4u, 0x690u, 0x694u, 0x8fcu, 0x69cu, 0x6a0u})
            std::cout << get<float>(P+off) << ' ';
        std::cout << get<int>(P+0x698) << '\n';
    }
}
