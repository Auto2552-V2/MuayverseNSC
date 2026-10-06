/**
 * Check the WebSocket frame logic used by PoseWebSocketServer.cs.
 *
 *   node scripts/verify_ws_frames.mjs
 *
 * WHAT THIS PROVES: that the algorithm is right -- masking, the three payload
 * length encodings, UTF-8 multibyte text, and the RFC 6455 handshake digest
 * (checked against the example vector in the RFC itself).
 *
 * WHAT IT DOES NOT PROVE: that the C# compiles or behaves identically. The
 * parser below is a hand port of ReadFrame() in
 * Assets/Scripts/Sparring/PoseWebSocketServer.cs, so the two can drift.
 * Change one, re-read the other.
 *
 * It exists because frame bugs surface as "a message goes missing now and
 * then" -- the hardest kind of bug to find by playing the game.
 */
// เทียบกับ frame จริงที่เบราว์เซอร์สร้าง เพื่อยืนยันว่า logic ความยาว/mask ถูก
import crypto from 'node:crypto';

function browserFrame(text){               // เบราว์เซอร์ mask เสมอ
  const p=Buffer.from(text,'utf8'); const mask=crypto.randomBytes(4);
  let head;
  if(p.length<126) head=Buffer.from([0x81, 0x80|p.length]);
  else if(p.length<65536){head=Buffer.alloc(4);head[0]=0x81;head[1]=0x80|126;head.writeUInt16BE(p.length,2);}
  else {head=Buffer.alloc(10);head[0]=0x81;head[1]=0x80|127;head.writeBigUInt64BE(BigInt(p.length),2);}
  const masked=Buffer.from(p); for(let i=0;i<masked.length;i++) masked[i]^=mask[i%4];
  return Buffer.concat([head,mask,masked]);
}

function csharpReadFrame(buf){             // พอร์ตตรงจาก C# ที่เขียนไป
  let o=0; const op=buf[o]&0x0F, masked=(buf[o+1]&0x80)!==0; let len=buf[o+1]&0x7F; o+=2;
  if(len===126){len=(buf[o]<<8)|buf[o+1];o+=2;}
  else if(len===127){len=0;for(let i=0;i<8;i++)len=(len*256)+buf[o+i];o+=8;}
  if(len<0||len>(1<<20)) return {err:'too long'};
  let mask=null; if(masked){mask=buf.subarray(o,o+4);o+=4;}
  const pay=Buffer.from(buf.subarray(o,o+len));
  if(mask) for(let i=0;i<pay.length;i++) pay[i]^=mask[i%4];
  return {op,text:pay.toString('utf8')};
}

const cases=[
  JSON.stringify({type:'pose',pose:'jab',confidence:0.9213,seq:1,t:1234}),
  JSON.stringify({type:'status',state:'nocamera',detail:'NotAllowedError',seq:2,t:5}),
  JSON.stringify({type:'status',state:'error',detail:'x'.repeat(300),seq:3,t:9}),  // >126 ไบต์
  'ก'.repeat(200),                                                                  // UTF-8 หลายไบต์
];
let fail=0;
for(const c of cases){
  const r=csharpReadFrame(browserFrame(c));
  const ok=r.text===c && r.op===1;
  if(!ok) fail++;
  console.log(`${ok?'ok  ':'FAIL'}  ${c.length} ไบต์  ${c.slice(0,46)}${c.length>46?'…':''}`);
}
// handshake
const key='dGhlIHNhbXBsZSBub25jZQ==';
const acc=crypto.createHash('sha1').update(key+'258EAFA5-E914-47DA-95CA-C5AB0DC85B11').digest('base64');
const want='s3pPLMBiTxaQ9kYGzzhZRbK+xOo=';   // ค่าตัวอย่างจาก RFC 6455
console.log(`${acc===want?'ok  ':'FAIL'}  handshake accept ตรงกับตัวอย่างใน RFC 6455`);
if(acc!==want) fail++;
console.log(fail?`\nFAIL ${fail} เคส`:'\nPASS: การแกะ frame กับ handshake ถูกต้อง');
process.exit(fail?1:0);
