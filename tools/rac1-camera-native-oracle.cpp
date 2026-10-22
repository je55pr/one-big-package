#include <runtime/ee/ee_interpreter.h>
#include <runtime/ee/ps2_fpu.h>
#include <runtime/kernel/ee_memory.h>
#include <fstream>
#include <iostream>
#include <iomanip>
#include <cmath>
#include <cstring>
#include <vector>
namespace rac1 {
namespace ee_detail {u8* g_ram=nullptr;u8* g_spr=nullptr;u32 g_fast_last=0,g_read_last=0;const u8* g_jseen=nullptr;}
std::vector<u8> ram(64*1024*1024),spr(0x4000);
u8* ee_get_ram_base(){return ram.data();} u8* ee_get_scratchpad_base(){return spr.data();}
void* ee_ram_ptr_slow(u32 a,bool,u32){return a>=SPR_BASE ? spr.data()+(a-SPR_BASE):ram.data()+(a&0x1ffffff);}
void ee_debug_backtrace(){} void vu_execute_xgkick(VUState&,uint32_t,std::vector<GsPrimitive>&){}
}
using namespace rac1;
constexpr uint32_t C=0x167150,W=0x169110,H=0x1000000;
void word(uint32_t a,uint32_t v){std::memcpy(ram.data()+a,&v,4);} void real(uint32_t a,float v){std::memcpy(ram.data()+a,&v,4);}
float f(uint32_t a){float v;std::memcpy(&v,ram.data()+a,4);return v;}
void vec(uint32_t a,float x,float y,float z){real(a,x);real(a+4,y);real(a+8,z);real(a+12,0);}
int main(int argc,char**argv){
 if(argc<2)return 2;
 std::ifstream in(argv[1],std::ios::binary);in.read(reinterpret_cast<char*>(ram.data()),32*1024*1024);
 ee_detail::g_ram=ram.data();ee_detail::g_spr=spr.data();ee_detail::g_fast_last=ee_detail::g_read_last=ram.size()-1;
 ps2fpu::enter_ee_rounding(); EeInterpreter ee;
 for(uint32_t a:{0x1efc70u,0x1f0d60u,0x1f2868u,0x2e9aa0u})ee.set_hook(a,[](EeCpu&c){c.set32(2,0);});
 auto call=[&](uint32_t a){if(!ee.call(a,{C})){std::cerr<<std::hex<<a<<": "<<ee.error()<<"\n";std::exit(1);}};
 word(0x166e00,C);word(C+0x70,W);word(0x1413d0,H);word(0x168fc0,0x169610);
 for(uint32_t a:{0x13f350u,H+0xc0}){vec(a,1,0,0);vec(a+16,0,1,0);vec(a+32,0,0,1);}
 vec(0x166e30,0,0,1);vec(0x166e40,0,0,1);vec(0x13f5e0,0,0,-1);vec(0x166e50,0,0,1);
 word(0x15ef80,0);word(0x167010,0);word(0x166edc,0);word(0x13f65c,0);word(0x1413f4,0);
 call(0x2e6d60);std::memcpy(ram.data()+0x169610,ram.data()+W,0x280);
 real(0x169610+0x15c,6);real(0x169610+0x188,6);
 auto tick=[&](){call(0x1eca70);call(0x2e9c28);std::memcpy(ram.data()+0x166dc0,ram.data()+C+0x30,16);};
 for(int i=0;i<300;i++)tick();
 std::cout<<std::setprecision(10);
 float dx,dy,dz,rx,ry;int ox,oy,speed;
 while(std::cin>>dx>>dy>>dz>>rx>>ry>>ox>>oy>>speed){
  real(0x13f3d0,f(0x13f3d0)+dx);real(0x13f3d4,f(0x13f3d4)+dy);real(0x13f3d8,f(0x13f3d8)+dz);
  real(0x13ca40,rx);real(0x13ca44,ry);word(0x15ede0,ox);word(0x15eddc,oy);word(0x15ede4,speed);
  tick();
  std::cout<<f(C+0x30)<<' '<<f(C+0x34)<<' '<<f(C+0x38)<<' '<<f(C)<<' '<<f(C+4)<<' '<<f(C+8)<<' '<<f(W+0x1a8)<<' '<<f(W+0x1b0)<<' '<<f(W+0x130)<<' '<<f(W+0x134)<<' '<<f(W+0x138)<<'\n';
 }
}
