"""Real additive C ABI: context teardown leases and concurrent wrong-owner queries."""
import ctypes as c,sys,threading,time,json
from pathlib import Path
lib=c.CDLL(sys.argv[1]);u64=c.c_uint64;u32=c.c_uint32
class Packet(c.Structure):_fields_=[(n,u64)for n in ['session','lease','epoch','commands','resources','plan','frame','retained','builtBytes']]+[(n,c.c_void_p)for n in ['draws','values','worlds']]+[(n,u32)for n in ['count','drawCount','built','reused','affine','fallback','version','flags']]
class Mesh(c.Structure):_fields_=[('vertices',c.c_void_p),('indices',c.c_void_p)]+[(n,u32)for n in ['vertexCount','indexCount','vertexStride','indexStride']]
class Draw(c.Structure):_fields_=[('batch',u64),('texture',u64)]+[(n,u32)for n in ['slot','count','vertices','indices','kind']]+[(n,c.c_int32)for n in ['enabled','x','y','w','h']]
assert c.sizeof(Packet)==128 and c.sizeof(Mesh)==32 and c.sizeof(Draw)==56
lib.rui_create.argtypes=[c.c_int,c.c_int,c.c_char_p];lib.rui_load_document.argtypes=[c.c_char_p]
create=lib.rui_native_frame_create;create.restype=u64
acquire=lib.rui_native_frame_acquire;acquire.argtypes=[u64,c.POINTER(Packet)]
mesh=lib.rui_native_frame_mesh;mesh.argtypes=[u64,u64,u64,c.POINTER(Mesh)]
release=lib.rui_native_frame_release;release.argtypes=[u64,u64]
destroy=lib.rui_native_frame_destroy;destroy.argtypes=[u64]
assert lib.rui_create(96,96,str((Path(__file__).resolve().parents[1]/'Assets/StreamingAssets/RmlUnity/Roboto-Regular.ttf')).encode())==1 and lib.rui_render_plan_enable(1)==1
session=create();assert session
assert lib.rui_load_document(b'<rml><head><style>body{margin:0px;font-family:Roboto;}div{position:absolute;left:0px;top:0px;width:20px;height:20px;background-color:#ff008080;}</style></head><body><div/></body></rml>')==1
lib.rui_update();lib.rui_render();p=Packet();status=acquire(session,c.byref(p));assert status==1 and p.count==1 and p.drawCount==1,(status,p.count,p.drawCount,p.affine,p.fallback)
d=Draw.from_address(p.draws);view=Mesh();assert mesh(session,p.lease,d.batch,c.byref(view))==1
before=(c.string_at(p.values,32),c.string_at(p.worlds,64),c.string_at(view.vertices,view.vertexCount*32),c.string_at(view.indices,view.indexCount*4))
lib.rui_destroy();assert before==(c.string_at(p.values,32),c.string_at(p.worlds,64),c.string_at(view.vertices,view.vertexCount*32),c.string_at(view.indices,view.indexCount*4));assert destroy(session)==-5
assert mesh(session,p.lease,d.batch,c.byref(view))==1 and release(session,p.lease)==1 and destroy(session)==-2
assert lib.rui_create(96,96,str((Path(__file__).resolve().parents[1]/'Assets/StreamingAssets/RmlUnity/Roboto-Regular.ttf')).encode())==1 and lib.rui_render_plan_enable(1)==1
ids=[];lock=threading.Lock();stop=threading.Event();errors=[];calls=[0]
def wrong_thread():
 try:
  while not stop.is_set():
   with lock:target=ids[-1]if ids else 0
   packet=Packet();packet.session=999;status=acquire(target,c.byref(packet));assert status in(-2,-3)and packet.session==0
   v=Mesh();v.vertexCount=999;status=mesh(target,1,1,c.byref(v));assert status in(-2,-3)and v.vertexCount==0
   status=destroy(target);assert status in(-2,-3);calls[0]+=1
 except BaseException as e:errors.append(repr(e));stop.set()
t=threading.Thread(target=wrong_thread);t.start()
try:
 for i in range(1000):
  sid=create();assert sid
  with lock:ids.append(sid)
  if i%5==0:time.sleep(0)
  assert destroy(sid)==1
finally:stop.set();t.join();lib.rui_destroy()
assert not errors,errors
print(json.dumps(dict(passed=True,realABI=True,oldLeaseAfterContextTeardown=True,ownerCreateDestroyIterations=1000,foreignConcurrentQueries=calls[0],foreignStatuses='-2 stale / -3 wrong thread; all packet/view outputs zero')))
