// Executes original NTSC-U UYA beam/ring instructions, including boot VU0 math.
// Hooks replace only the muzzle fact and GS submission. See THIRD_PARTY_NOTICES
// for the externally built MIT interpreter; no original code/assets are included.
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
 if(argc<2)return 2;
 std::ifstream in(argv[1],std::ios::binary);in.read(reinterpret_cast<char*>(ram.data()),32*1024*1024);if(!in)return 2;
 ee_detail::g_ram=ram.data();ee_detail::g_spr=spr.data();ee_detail::g_fast_last=ee_detail::g_read_last=ram.size()-1;
 // Original UYA boot DMA END / VIF0 MPG uploads the math routines at 0x190.
 ee_vif0_chain(0x10f1a0,true);
 constexpr uint32_t M=0x1000000;
 for(int i=0;i<16;i++)put<float>(M+i*4,i%5==0?1.f:0.f);
 EeInterpreter ee;
 if(argc>2 && std::strcmp(argv[2],"volume")==0){
  float distance,minimum,maximum;int low,high,flags,quadratic;
  while(std::cin>>distance>>minimum>>maximum>>low>>high>>flags>>quadratic){
   put<int>(M+8,low);put<int>(M+12,high);ram[M+25]=static_cast<uint8_t>(flags);
   ee.set_cond_hook(0x4a53d0,[&](EeCpu&c){c.fpr[12]=distance;c.fpr[13]=minimum;c.fpr[14]=maximum;return false;});
   ps2fpu::enter_ee_rounding();bool ok=ee.call(0x4a53d0,{M,static_cast<uint32_t>(quadratic)},0x1dc8b0,500000);
   std::fesetround(FE_TONEAREST);if(!ok){std::cerr<<ee.error()<<'\n';return 1;}
   std::cout<<ee.cpu().s32(2)<<'\n';
  }
  return 0;
 }
 if(argc>2){
  constexpr uint32_t P=0x1a4be0,W=M+0x2000;
  std::memset(ram.data()+P,0,0x4000);
  float ax=1,ay=0,az=0;std::cin>>ax>>ay>>az;
  put<uint32_t>(P+0x25c0,W);put<float>(W+0xc0,ax);put<float>(W+0xc4,ay);put<float>(W+0xc8,az);
  put<float>(0x1dc8b0+0x1594,1.1f);put<float>(0x1dc8b0+0x1598,1.1f);
  put<int>(0x1e1f24,29);
  ee.set_hook(0x437698,[](EeCpu&c){for(int k=0;k<4;k++)put<float>(c.r32(6)+4*k,k<3?float(k+2):1.f);});
  int calls=0;
  ee.set_hook(0x409a50,[&](EeCpu&c){
   if(calls++)std::cout<<',';
   std::cout<<"{\"texture\":"<<c.s32(5)<<",\"color\":"<<c.r32(6)<<",\"rings\":"<<c.s32(7)<<",\"phase\":"<<c.fpr[12]<<",\"matrix\":[";
   for(int k=0;k<16;k++){if(k)std::cout<<',';std::cout<<get<float>(c.r32(4)+4*k);}std::cout<<"]}";
  });
  std::cout<<std::setprecision(10)<<'[';ps2fpu::enter_ee_rounding();
  bool ok=ee.call(0x3873f8,{W},0x1dc8b0,500000);
  std::fesetround(FE_TONEAREST);if(!ok){std::cerr<<ee.error()<<'\n';return 1;}
  std::cout<<"]\n";return 0;
 }
 for(uint32_t fn:{0x4c1fe8u,0x401cd0u,0x4051b8u})ee.set_hook(fn,[](EeCpu&c){c.set32(2,0);});
 int strips=0;
 ee.set_hook(0x4066dc,[&](EeCpu&c){
  if(strips++)std::cout<<',';
  std::cout<<'[';
  for(uint32_t i=0;i<c.r32(4);i++){
   if(i)std::cout<<',';
   auto p=c.r32(5)+12*i,co=c.r32(6)+4*i,uv=c.r32(7)+8*i;
   std::cout<<'['<<get<float>(p)<<','<<get<float>(p+4)<<','<<get<float>(p+8)<<','<<get<uint32_t>(co)<<','<<get<float>(uv)<<','<<get<float>(uv+4)<<']';
  }
  std::cout<<']';
 });
 int rings;unsigned color,blend;float scroll;
 std::cout<<std::setprecision(10);
 while(std::cin>>rings>>color>>blend>>scroll){
  strips=0;
  ee.set_cond_hook(0x409a50,[&](EeCpu&c){c.fpr[12]=scroll;c.set32(8,blend);return false;});
  std::cout<<'[';ps2fpu::enter_ee_rounding();
  bool ok=ee.call(0x409a50,{M,23,color,static_cast<uint32_t>(rings)},0x1dc8b0,500000);
  std::fesetround(FE_TONEAREST);if(!ok){std::cerr<<ee.error()<<'\n';return 1;}
  std::cout<<"]\n";
 }
}
