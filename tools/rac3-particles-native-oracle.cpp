// Research only. Executes authorized UYA boot + TABLE1 instructions; no retail
// payload or reconstructed decomp source is included. External MIT interpreter
// attribution is in THIRD_PARTY_NOTICES.md. World/pose/camera facts are declared.
#include <runtime/ee/ee_interpreter.h>
#include <runtime/ee/ps2_fpu.h>
#include <runtime/kernel/ee_memory.h>
#include <cfenv>
#include <cmath>
#include <cstdlib>
#include <cstring>
#include <fstream>
#include <initializer_list>
#include <iomanip>
#include <iostream>
#include <string>
#include <vector>

// External interpreter ABI; these are not OBP source identities.
namespace rac1 {
namespace ee_detail {u8* g_ram=nullptr;u8* g_spr=nullptr;u32 g_fast_last=0,g_read_last=0;const u8* g_jseen=nullptr;}
std::vector<u8> ram(64*1024*1024),spr(0x4000);
u8* ee_get_ram_base(){return ram.data();}
u8* ee_get_scratchpad_base(){return spr.data();}
void* ee_ram_ptr_slow(u32 a,bool,u32){return a>=SPR_BASE?spr.data()+a-SPR_BASE:ram.data()+(a&0x1ffffff);}
void ee_debug_backtrace(){/* The caller reports the interpreter error. */}
void vu_execute_xgkick(VUState&,uint32_t,std::vector<GsPrimitive>&){/* VU1/GS rasterization is outside the descriptor oracle. */}
}
using namespace rac1;
template<class T> T get(uint32_t a){T v;std::memcpy(&v,ram.data()+a,sizeof(T));return v;}
template<class T> void put(uint32_t a,T v){std::memcpy(ram.data()+a,&v,sizeof(T));}

int main(int argc,char**argv){
 if(argc!=3)return 2;
 constexpr uint32_t GP=0x1dc8b0,CI=0x1000000,M=0x1700000,P=M+0x1000,SP=0x17e0000,POOL=0x1800000;
 std::ifstream input(argv[1],std::ios::binary);
 input.read(reinterpret_cast<char*>(ram.data()),32*1024*1024);if(!input)return 2;
 ee_detail::g_ram=ram.data();ee_detail::g_spr=spr.data();ee_detail::g_fast_last=ee_detail::g_read_last=ram.size()-1;
 ee_vif0_chain(0x10f1a0,true); // Original UYA VU0 math upload, not R&C1's default.
 EeInterpreter ee;int draws=0;
 ee.set_cond_hook(0x13d3e0,[&](EeCpu&){++draws;return false;});
 auto call=[&](uint32_t fn,std::initializer_list<uint32_t> args){
  ps2fpu::enter_ee_rounding();bool ok=ee.call(fn,args,GP,20000000);std::fesetround(FE_TONEAREST);
  if(!ok){std::cerr<<std::hex<<fn<<": "<<ee.error()<<'\n';std::exit(1);}
 };
 auto load=[&](){call(0x4224c8,{CI+get<uint32_t>(CI+0x6c),0x1000,CI+get<uint32_t>(CI+0x54),65});};
 load();
 for(int id=0;id<129;id++){auto a=get<uint32_t>(0x221118+id*4);std::cout<<"D "<<id<<' '<<unsigned(ram[a])<<'\n';}
 for(int id=0;id<65;id++)std::cout<<"T "<<id<<' '<<get<uint32_t>(0x220ac0+id*8)<<' '<<get<uint32_t>(0x220ac4+id*8)<<'\n';
 std::memset(ram.data()+0x2208c0,0,0x200);std::memset(ram.data()+POOL,0xa5,2048*64);
 put<uint32_t>(GP+0x3a48,POOL);put<int>(GP+0x3a4c,0);put<int>(GP+0x3a50,-1);put<int>(GP+0x3a54,0);
 call(0x45f4d0,{});
 // Explicit static world: execute the real null-frame helper and ordinary gravity branch.
 put<uint32_t>(GP-0x1884,0);put<uint32_t>(0x1c673c,0);
 unsigned seed;int frames,age,repeat,full;float distance;
 if(!(std::cin>>seed>>frames>>age>>repeat>>full>>distance)||frames<0||frames>1000||age<1||age>78)return 2;
 const bool beam=std::string(argv[2])=="beam",constructor=std::string(argv[2])=="constructor";
 if(!beam&&!constructor&&std::string(argv[2])!="charge")return 2;
 uint32_t begin=beam?0x3865cc:0x385e80,end=beam?0x38675c:0x385f50,returnTo=0;
 ee.set_cond_hook(begin,[&](EeCpu&c){
  returnTo=c.r32(31);c.set32(2,0x220000);c.set32(18,P);put<uint32_t>(SP+0x188,M);put<int>(P+0xc,age);
  // At this bounded entry VF1 is the already prepared charged-shot endpoint.
  if(beam)std::memcpy(&c.vu0->vf[1],ram.data()+M+16,16);
  return false;
 });
 ee.set_hook(end,[&](EeCpu&c){c.set32(31,returnTo);}); // Return after the complete selected recipe, before unrelated controller work.
 auto emit=[&](){
  ee.cpu().set32(29,SP);
  if(constructor){
   ee.cpu().fpr[12]=21000;ee.cpu().fpr[13]=115500;ee.cpu().fpr[14]=0;
   for(int reg=8;reg<=11;reg++)ee.cpu().set32(reg,reg==10?127:0);
   call(0x46dcb8,{M,0x1c66b0,100,0x123456});
  }else call(begin,{});
 };
 std::memcpy(ram.data()+0x1e1f50,ram.data()+M,16);std::memcpy(ram.data()+0x1e1f60,ram.data()+M+16,16);
 if(full)for(int i=0;i<2048;i++)call(0x45e438,{97});
 int tick=0,drawSlot=-1;
 if(distance>0&&!full){
  constexpr uint32_t V=0x1e2900;
  std::memset(ram.data()+V,0,0x150);for(int i=0;i<3;i++)put<float>(V+i*16+i*4,1);
  put<float>(V+0x140,get<float>(M));put<float>(V+0x144,get<float>(M+4));put<float>(V+0x148,get<float>(M+8)-distance);
  put<float>(GP-0x2850,1.f/420000);put<float>(GP-0x284c,-1.f/420000);put<float>(GP-0x2848,.5f);put<float>(GP-0x2844,1.f/420000);
  put<uint32_t>(GP+0x3a58,0x7fffffff);put<uint32_t>(GP+0x4838,0x1900000);
  // Admit one sprite after native culling/sorting; retain original descriptor/material instructions and SPR DMA.
  ee.set_cond_hook(0x45ec88,[&](EeCpu&c){std::memcpy(spr.data(),&drawSlot,2);c.set32(14,SPR_BASE);c.set32(15,SPR_BASE+2);return false;});
  ee.set_cond_hook(0x45efa4,[&](EeCpu&c){
   auto a=c.r32(24)-SPR_BASE;std::cout<<"S "<<tick<<' '<<drawSlot;
   for(int k=0;k<16;k++){uint32_t w;std::memcpy(&w,spr.data()+a+4*k,4);std::cout<<' '<<w;}std::cout<<'\n';return false;
  });
 }
 call(0x13d3d0,{seed});emit();
 if(full){std::cout<<"F 0 "<<get<int>(GP+0x3a54)<<' '<<draws<<'\n';return 0;}
 auto print=[&](){
  std::cout<<"F "<<tick<<' '<<get<int>(GP+0x3a54)<<' '<<draws<<'\n';drawSlot=-1;
  for(int i=0;i<=get<int>(GP+0x3a50);i++){
   auto r=POOL+i*64;if(ram[r+1]&0x80)continue;
   if(drawSlot<0)drawSlot=i;
   std::cout<<"R "<<tick<<' '<<i;
   for(int k=0;k<16;k++)std::cout<<' '<<get<uint32_t>(r+4*k);
   std::cout<<'\n';
  }
  if(distance>0&&drawSlot>=0){
   put<uint32_t>(GP+0x314c,0);put<uint32_t>(GP-0x7d00,0x100000);load();
   call(0x45e810,{});ram[POOL+drawSlot*64+1]&=~0x20;
  }
 };
 std::cout<<std::setprecision(10);print();
 for(tick=1;tick<=frames;tick++){call(0x45e788,{});if(repeat)emit();print();}
}
