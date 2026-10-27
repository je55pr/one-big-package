// Research adapter only. Executes the authorized Novalis EE/VU instructions;
// external MIT interpreter attribution is in THIRD_PARTY_NOTICES.md.
// The caller supplies boot + overlay + the actual authored owner in private RAM.
// Allocation/free and visibility are conditioned boundaries, not simulated game logic.
#include <runtime/ee/ee_interpreter.h>
#include <runtime/ee/ps2_fpu.h>
#include <runtime/kernel/ee_memory.h>
#include <cmath>
#include <cstring>
#include <fstream>
#include <iomanip>
#include <iostream>
#include <vector>

namespace rac1 {
namespace ee_detail { u8* g_ram=nullptr; u8* g_spr=nullptr; u32 g_fast_last=0,g_read_last=0; const u8* g_jseen=nullptr; }
std::vector<u8> ram(64*1024*1024),spr(0x4000);
u8* ee_get_ram_base(){return ram.data();}
u8* ee_get_scratchpad_base(){return spr.data();}
void* ee_ram_ptr_slow(u32 a,bool,u32){return a>=SPR_BASE?spr.data()+a-SPR_BASE:ram.data()+(a&0x1ffffff);}
void ee_debug_backtrace(){}
void vu_execute_xgkick(VUState&,uint32_t,std::vector<GsPrimitive>&){}
}
using namespace rac1;
template<class T> T get(uint32_t a){T v;std::memcpy(&v,ram.data()+a,sizeof(T));return v;}
template<class T> void put(uint32_t a,T v){std::memcpy(ram.data()+a,&v,sizeof(T));}

int main(int argc,char**argv){
 if(argc!=2)return 2;
 std::ifstream in(argv[1],std::ios::binary);in.read(reinterpret_cast<char*>(ram.data()),32*1024*1024);if(!in)return 2;
 ee_detail::g_ram=ram.data();ee_detail::g_spr=spr.data();ee_detail::g_fast_last=ee_detail::g_read_last=ram.size()-1;
 constexpr uint32_t M=0x1000000,P=0x1001000,R=0x1002000,T=0x1003000;
 put<uint32_t>(M+0x78,P);
 put<uint32_t>(0x1b2500+4*ram[P+0xc7],T); // first-frame texture byte supplied by the caller
 for(uint32_t a:{0x15ed60u,0x15ed64u,0x15ed68u})put<float>(a,1);
 put<float>(0x15ed6c,1.f/60);put<uint32_t>(0x15ed70,0x3991a2b4);
 EeInterpreter ee;bool alive=false;int draws=0;
 ee.set_hook(0x2221f0,[](EeCpu&c){c.set32(2,0);}); // explicitly visible, independent culler trials are separate
 ee.set_hook(0x27c498,[&](EeCpu&c){
  if(alive){c.set32(2,0);return;}
  std::memset(ram.data()+R,0,64);ram[R]=6;alive=true;c.set32(2,R);
 });
 ee.set_hook(0x27c5e8,[&](EeCpu&c){alive=false;c.set32(2,0);});
 ee.set_cond_hook(0x1160d8,[&](EeCpu&){++draws;return false;});
 auto call=[&](uint32_t fn,std::initializer_list<uint32_t> args){
  ps2fpu::enter_ee_rounding();bool ok=ee.call(fn,args,0x166c00,500000);
  std::fesetround(FE_TONEAREST);
  if(!ok){std::cerr<<std::hex<<fn<<": "<<ee.error()<<'\n';std::exit(1);}
 };
 unsigned seed;int frames;if(!(std::cin>>seed>>frames)||frames<0||frames>1000)return 2;
 call(0x1160c8,{seed});call(0x2bd100,{M});
 auto print=[&](int tick){
  std::cout<<tick<<' '<<alive<<' '<<draws;
  for(uint32_t offset:{0x10u,0x14u,0x18u,0x20u,0x24u,0x28u,0xcu})std::cout<<' '<<get<float>(R+offset);
  std::cout<<' '<<get<uint32_t>(R+4)<<' '<<unsigned(ram[R+8])<<' '<<get<int16_t>(R+0xa)
   <<' '<<unsigned(ram[R+2])<<' '<<unsigned(ram[R+3])<<' '<<unsigned(ram[R+9])<<'\n';
 };
 std::cout<<std::setprecision(10);print(0);
 for(int tick=1;tick<=frames;tick++){if(alive)call(0x27eb08,{R});print(tick);}
}
