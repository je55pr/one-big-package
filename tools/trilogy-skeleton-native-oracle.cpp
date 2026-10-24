// Local original-instruction skeletal oracle. No retail payloads are distributed.
// Build through trilogy-skeleton-native-trace.py; see THIRD_PARTY_NOTICES.md.
#include <runtime/ee/ee_interpreter.h>
#include <runtime/ee/ps2_fpu.h>
#include <runtime/kernel/ee_memory.h>
#include <cstring>
#include <fstream>
#include <iostream>
#include <vector>
#ifdef _WIN32
#include <io.h>
#include <fcntl.h>
#endif
#include <algorithm>
#include <stdexcept>
namespace rac1 {
namespace ee_detail { u8* g_ram=nullptr; u8* g_spr=nullptr; u32 g_fast_last=0,g_read_last=0; const u8* g_jseen=nullptr; }
std::vector<u8> ram(64*1024*1024),spr(0x4000);
u8* ee_get_ram_base(){return ram.data();}u8* ee_get_scratchpad_base(){return spr.data();}
void* ee_ram_ptr_slow(u32 a,bool,u32){return a>=SPR_BASE?spr.data()+a-SPR_BASE:ram.data()+(a&0x1ffffff);}
void ee_debug_backtrace(){}void vu_execute_xgkick(VUState&,uint32_t,std::vector<GsPrimitive>&){}
}
using namespace rac1;
void require(bool condition){if(!condition)throw std::runtime_error("Invalid bounded skeleton oracle input");}
std::vector<uint8_t> read(const std::string& p){
 std::ifstream f(p,std::ios::binary|std::ios::ate);require(f.good());auto size=f.tellg();require(size>0&&size<=32*1024*1024);
 std::vector<uint8_t> bytes(static_cast<size_t>(size));f.seekg(0);f.read(reinterpret_cast<char*>(bytes.data()),size);require(f.good());return bytes;
}
uint32_t word(const std::vector<uint8_t>& b,uint32_t at){require(at<=b.size()&&b.size()-at>=4);uint32_t v;std::memcpy(&v,b.data()+at,4);return v;}
void put(uint32_t a,uint32_t value){require(a<=ram.size()-4);std::memcpy(ram.data()+a,&value,4);}
void copy(uint32_t destination,const std::vector<uint8_t>& source,uint32_t at,size_t size){
 require(at<=source.size()&&size<=source.size()-at&&destination<=ram.size()&&size<=ram.size()-destination);
 std::memcpy(ram.data()+destination,source.data()+at,size);
}
int run(int argc,char**argv){
#ifdef _WIN32
 _setmode(_fileno(stdout),_O_BINARY);
#endif
 if(argc<3)return 2;std::string dir=argv[1],game=argv[2];bool pose=argc>3,carry=argc>3&&std::strcmp(argv[3],"carry")==0;
 float weight=argc>4?std::stof(argv[4]):1.f;require(weight>0.f&&weight<=1.f||!carry);uint32_t weightBits;std::memcpy(&weightBits,&weight,4);
 require(game=="gc"||game=="uya");require(!pose||carry||std::strcmp(argv[3],"pose")==0);
 auto image=read(dir+"/"+game+"-ram.bin"),assets=read(dir+"/"+game+"-assets.bin"),seqs=read(dir+"/"+game+"-sequences.bin");
 if(image.size()!=32*1024*1024||seqs.size()!=1024)return 2;
 std::memcpy(ram.data(),image.data(),image.size());
 ee_detail::g_ram=ram.data();ee_detail::g_spr=spr.data();ee_detail::g_fast_last=ee_detail::g_read_last=ram.size()-1;
 constexpr uint32_t C=0x1600000,M=0x1500000;
 if(pose){auto cls=read(dir+"/"+game+"-player-class.bin");require(cls.size()<0x100000&&cls.size()>0x48&&cls[8]==111);copy(C,cls,0,cls.size());
  for(uint32_t off:{0x14u,0x18u,0x1cu})put(C+off,C+word(cls,off));
  uint32_t table=word(cls,0x1c);for(uint32_t j=0;j<word(cls,table);j++)put(C+table+4+j*4,C+word(cls,table+4+j*4));
  put(M+0x24,C);put(M+0x58,0x1008000);put(M+0x5c,0x1008000);
  put(M+0x2c,word(cls,0x24));
  for(int i=0;i<16;i++)put(M+0xc0+i*4,i%5==0?0x3f800000:0);
 }
 constexpr uint32_t D=0x1000000,F=0x1004000,O=0x1008000;
 int s,f;
 while(std::cin>>s>>f){
  require(s>=0&&s<256&&f>=0);uint32_t offset=word(seqs,s*4);require(offset>0&&offset<assets.size()-0x1c&&f<assets[offset+0x10]);
  uint32_t so=word(seqs,s*4),desc=so+0x1c+assets[so+0x10]*4+assets[so+0x13]*4,frame=so+(word(assets,so+0x1c+f*4)&0xfffffff);
  uint32_t counts=desc+4+111*6;require(counts+1<assets.size());
  uint32_t descriptorSize=6+111*6+(assets[counts]+assets[counts+1])*8;
  require(descriptorSize<0x4000&&frame<assets.size());
  std::memset(ram.data()+D,0,0xc000);copy(D,assets,desc,descriptorSize);copy(F,assets,frame,std::min<size_t>(4096,assets.size()-frame));
  EeInterpreter ee;ps2fpu::enter_ee_rounding();bool ok=ee.call(game=="gc"?0x308b20:0x4397f8,{D,F,O,111},0x1dc8b0,500000);
  std::fesetround(FE_TONEAREST);if(!ok){std::cerr<<ee.error()<<'\n';return 1;}
  if(pose){
   put(M+0x40,f|(f<<8)|(s<<16)|(s<<24));put(M+0x50,0);
   if(carry){for(uint32_t side=0;side<(game=="gc"?1u:2u);side++){
    auto arm=read(dir+"/"+game+"-class-"+std::to_string(side+1)+".bin");
    uint32_t B=0x1700000+side*0x30000,L=0x1800000+side*0x100,BF=0x1801000+side*0x2000;
    require(arm.size()>0x48&&arm.size()<0x30000&&s<arm[12]);copy(B,arm,0,arm.size());put(B+0x1c,B+word(arm,0x1c));
    for(uint32_t si=0;si<arm[12];si++){uint32_t q=word(arm,0x48+si*4);put(B+0x48+si*4,B+q);for(uint32_t fi=0;fi<arm[q+16];fi++)put(B+q+28+fi*4,B+word(arm,q+28+fi*4));}
    std::memset(ram.data()+L,0,0x100);put(L,12+side+(20<<16));put(L+4,2);put(L+8,weightBits);
    put(L+0xc,B+word(arm,0x1c)+4);put(L+0x10,0x1820000+side*0x1000);put(L+0x14,C);put(L+0x18,B);
    put(L+0x1c,side?0x1800000:0);put(L+0x38,BF);put(L+0x3c,BF);
    bool good=ee.call(game=="gc"?0x306ef8:0x437200,{L,static_cast<uint32_t>(s),static_cast<uint32_t>(f),BF},game=="gc"?0x1af000:0x1dc8b0,1000000);
    if(!good){std::cerr<<ee.error()<<'\n';return 1;}
    good=ee.call(game=="gc"?0x30ab00:0x43b7d0,{L},game=="gc"?0x1af000:0x1dc8b0,1000000);
    if(!good){std::cerr<<ee.error()<<'\n';return 1;}put(M+0x50,L);
   }}
   for(uint32_t semantic=0;semantic<31;semantic++){
    bool good=ee.call(game=="gc"?0x3072f0:0x437600,{M,semantic,0x1501000},game=="gc"?0x1af000:0x1dc8b0,1000000);
    if(!good){std::cerr<<ee.error()<<'\n';return 1;}
    std::cout.write(reinterpret_cast<char*>(ram.data()+0x1501000),64);
   }
  }else std::cout.write(reinterpret_cast<char*>(ram.data()+O),2048);
 }
 return 0;
}
int main(int argc,char**argv){try{return run(argc,argv);}catch(const std::exception& e){std::cerr<<e.what()<<'\n';return 2;}}
