// Research adapter only: original GC NTSC-U boot + Museum EE/VU instructions.
// No retail instructions, assets, or reconstructed GC C sources are included.
// The external MIT interpreter is acknowledged in THIRD_PARTY_NOTICES.md.
#include <runtime/ee/ee_interpreter.h>
#include <runtime/ee/ps2_fpu.h>
#include <runtime/kernel/ee_memory.h>
#include <cmath>
#include <cfenv>
#include <cstdlib>
#include <cstring>
#include <fstream>
#include <initializer_list>
#include <iomanip>
#include <iostream>
#include <string>
#include <vector>

// Names belong to the external interpreter's ABI, not OBP source identities.
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
 if(argc!=3)return 2;
 constexpr uint32_t GP=0x1aeff0,CI=0x1000000,M=0x1700000,P=M+0x1000,POOL=0x1800000;
 std::ifstream input(argv[1],std::ios::binary);
 input.read(reinterpret_cast<char*>(ram.data()),32*1024*1024);if(!input)return 2;
 ee_detail::g_ram=ram.data();ee_detail::g_spr=spr.data();ee_detail::g_fast_last=ee_detail::g_read_last=ram.size()-1;
 EeInterpreter ee;int draws=0;
 ee.set_cond_hook(0x1163b0,[&](EeCpu&){++draws;return false;});
 auto call=[&](uint32_t fn,std::initializer_list<uint32_t> args){
  ps2fpu::enter_ee_rounding();bool ok=ee.call(fn,args,GP,20000000);std::fesetround(FE_TONEAREST);
  if(!ok){std::cerr<<std::hex<<fn<<": "<<ee.error()<<'\n';std::exit(1);}
 };
 if(std::string(argv[2])=="edit"){
  constexpr uint32_t MENU=M+0x10000,MP=MENU+0x1000;
  put<uint32_t>(MENU+0x68,MP);
  std::memset(ram.data()+0x260e30,0,32*4);
  // Rendering callback registration is outside the numeric menu write trial.
  ee.set_hook(0x2edb20,[](EeCpu&){});
  int count;if(!(std::cin>>count)||count!=17)return 2;
  for(int i=0;i<count;i++){
   int offset,kind,minimum,maximum;if(!(std::cin>>offset>>kind>>minimum>>maximum))return 2;
   ee.cpu().set32(8,minimum);ee.cpu().set32(9,maximum);
   call(0x413528,{MENU,P+static_cast<uint32_t>(offset),0,static_cast<uint32_t>(kind)});
  }
  int index,step;
  while(std::cin>>index>>step){
   if(index<0||index>=17||(step!=1&&step!=-1&&step!=5&&step!=-5))return 2;
   put<uint32_t>(MP,index);put<uint16_t>(MENU+0x96,5);
   uint32_t buttons=step>0?0x2000:0x8000;if(std::abs(step)==5)buttons|=1;
   put<uint64_t>(0x138180+0x1a0,buttons);call(0x4130e8,{MENU});
   std::cout<<"E "<<index<<' '<<step;
   for(int k=0;k<20;k++)std::cout<<' '<<get<uint32_t>(P+4*k);std::cout<<'\n';
  }
  return 0;
 }
 const bool full=std::string(argv[2])=="full";
 if(std::string(argv[2])!="emit"&&!full)return 2;
 // Only external player/menu interaction is conditioned. Allocation, RNG,
 // registration, constructor, pool walk, callback and free run original code.
 ee.set_hook(0x326868,[](EeCpu&c){c.set32(2,0);});
 call(0x3042a8,{CI+get<uint32_t>(CI+0x6c),0x1000,CI+get<uint32_t>(CI+0x54),get<uint32_t>(CI+0x50)});
 for(int id=0;id<111;id++){auto p=get<uint32_t>(0x1ff940+id*4);std::cout<<"D "<<id<<' '<<unsigned(ram[p])<<'\n';}
 for(int id=0;id<109;id++)std::cout<<"T "<<id<<' '<<get<uint32_t>(0x1ff340+id*8)<<' '<<get<uint32_t>(0x1ff344+id*8)<<'\n';
 std::memset(ram.data()+0x1ff140,0,0x200);std::memset(ram.data()+POOL,0xa5,2048*64);
 put<uint32_t>(GP+0x3f40,POOL);put<int>(GP+0x3f44,0);put<int>(GP+0x3f48,-1);put<int>(GP+0x3f4c,0);
 call(0x331720,{});put<uint32_t>(M+0x68,P);
 unsigned seed;int frames,repeat;float distance;
 if(!(std::cin>>seed>>frames>>repeat>>distance)||frames<0||frames>4096||repeat<0||repeat>1)return 2;
 if(full)for(int i=0;i<2048;i++)call(0x330670,{85});
 int tick=0,slot=-1;
 // A camera with identity rotation, a declared projection W and an admitted
 // one-sprite visible list. PartProc's original material/descriptor path runs;
 // frustum/sort policy and VU1/GS rasterization are not oracle claims.
 if(distance>0){
  constexpr uint32_t V=0x1b9c80;
  std::memset(ram.data()+V,0,0x150);
  for(int i=0;i<3;i++)put<float>(V+i*16+i*4,1);
  put<float>(V+0x140,get<float>(M+0x10));put<float>(V+0x144,get<float>(M+0x14));put<float>(V+0x148,get<float>(M+0x18)-distance);
  put<float>(GP-0x2690,1.f/420000);put<float>(GP-0x268c,-1.f/420000);put<float>(GP-0x2688,.5f);put<float>(GP-0x2684,1.f/420000);
  put<uint32_t>(GP+0x3f50,0x7fffffff);put<uint32_t>(GP+0x4498,0x1900000);put<uint32_t>(GP+0x37cc,0);put<uint32_t>(GP-0x7d20,0x100000);
  ee.set_cond_hook(0x330ed8,[&](EeCpu&c){
   std::memcpy(spr.data(),&slot,2);c.set32(14,SPR_BASE);c.set32(15,SPR_BASE+2);return false;
  });
  ee.set_cond_hook(0x3311f4,[&](EeCpu&c){
   auto a=c.r32(24)-SPR_BASE;std::cout<<"S "<<tick<<' '<<slot;
   for(int i=0;i<16;i++){uint32_t w;std::memcpy(&w,spr.data()+a+4*i,4);std::cout<<' '<<w;}std::cout<<'\n';return false;
  });
 }
 call(0x1163a0,{seed});call(0x4186b0,{M});
 if(full){std::cout<<"F 0 "<<get<int>(GP+0x3f4c)<<' '<<draws<<' '<<get<float>(P+0xc)<<'\n';return 0;}
 auto print=[&](){
  std::cout<<"F "<<tick<<' '<<get<int>(GP+0x3f4c)<<' '<<draws<<' '<<get<float>(P+0xc)<<' '<<get<int16_t>(P+0x4c)<<' '<<get<int16_t>(P+0x4e)<<'\n';slot=-1;
  for(int i=0;i<=get<int>(GP+0x3f48);i++){
   auto r=POOL+i*64;if(ram[r+1]&0x80)continue;if(slot<0)slot=i;
   std::cout<<"R "<<tick<<' '<<i;for(int k=0;k<16;k++)std::cout<<' '<<get<uint32_t>(r+4*k);std::cout<<'\n';
  }
  if(distance>0&&slot>=0){
   // Reset transient cache/cursor; repeat exactly the same declared GS bindings.
   put<uint32_t>(GP+0x37cc,0);put<uint32_t>(GP-0x7d20,0x100000);
   call(0x3042a8,{CI+get<uint32_t>(CI+0x6c),0x1000,CI+get<uint32_t>(CI+0x54),109});
   call(0x330a60,{});ram[POOL+slot*64+1]&=~0x20;
  }
 };
 std::cout<<std::setprecision(10);print();
 for(tick=1;tick<=frames;tick++){call(0x3309d0,{});if(repeat)call(0x4186b0,{M});print();}
}
