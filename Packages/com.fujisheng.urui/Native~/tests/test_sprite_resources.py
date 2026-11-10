"""Document-owned immutable atlas regions through the real native resource protocol."""
import ctypes as C
from pathlib import Path
import sys
import threading
import unittest

LIB=C.CDLL(str(Path(sys.argv.pop(1)).resolve()))
U,I,F,S,P=C.c_uint64,C.c_int,C.c_float,C.c_char_p,C.c_void_p

class Command(C.Structure):
    _fields_=[("geometry",U),("texture",U),("tx",F),("ty",F),("flags",I),("x",I),("y",I),("w",I),("h",I)]

class SpriteResources(unittest.TestCase):
    def fn(self,name,result,*arguments):
        value=getattr(LIB,name,None)
        self.assertIsNotNone(value,'Missing document sprite capability: '+name)
        value.restype,value.argtypes=result,list(arguments)
        return value
    def setUp(self):
        self.sprite=self.fn('urui_document_register_sprite',I,U,S,S,F,F,F,F,F)
        self.assertEqual(self.fn('urui_document_resource_api_version',I)(),3)
        self.assertEqual(self.fn('urui_create',I,I,I)(200,200),1)
        self.close=self.fn('urui_document_close',I,U)
        self.create=self.fn('urui_document_create',I,P)
        self.rgba=self.fn('urui_document_register_rgba_texture',I,U,S,I,I,P,I,P)
        self.load=self.fn('urui_document_load_html',I,U,S,S)
        self.present=self.fn('urui_document_set_presentation',I,U,I,I,I,I)
        self.box=self.fn('urui_get_element_box',I,U,S,P)
        self.update=self.fn('urui_update',None)
        self.pixels=(C.c_ubyte*256)(*([255,0,0,255]*64))
    def tearDown(self):
        self.fn('urui_destroy',None)()
    def document(self):
        owner=U();self.assertEqual(self.create(C.byref(owner)),1)
        texture=U();self.assertEqual(self.rgba(owner,b'ui/atlas.png',8,8,self.pixels,256,C.byref(texture)),1)
        return owner.value,texture.value
    def show(self,owner):
        text=b'<!doctype html><html><head><style>body{margin:0;}</style></head><body><img id="image" data-urui-sprite="icon"></body></html>'
        self.assertEqual(self.load(owner,text,b'ui/page.html'),1)
        self.assertEqual(self.present(owner,0,0,1,0),1);self.update()
    def size(self,owner):
        box=(F*6)();self.assertEqual(self.box(owner,b'image',box),1)
        return tuple(box)[2:4]
    def test_scopes_share_image_but_keep_region_and_candidate_rollback(self):
        a,image=self.document();b,other=self.document();self.assertEqual(image,other)
        self.assertEqual(self.sprite(a,b'icon',b'ui/atlas.png',0,0,3,4,1),1)
        self.assertEqual(self.sprite(b,b'icon',b'ui/atlas.png',4,4,2,2,2),1)
        self.show(a);self.show(b);self.assertEqual(self.size(a),(3,4));self.assertEqual(self.size(b),(4,4))
        self.assertEqual(self.sprite(a,b'icon',b'ui/atlas.png',0,0,5,2,1),1)
        self.assertLess(self.load(a,b'<html><body><div>',b'ui/invalid.html'),0)
        self.assertEqual(self.size(a),(3,4));self.assertEqual(self.size(b),(4,4))
        self.assertEqual(self.close(a),1);self.update();self.assertEqual(self.size(b),(4,4))
        self.assertEqual(self.fn('urui_texture_exists',I,U)(image),1)
        self.assertEqual(self.close(b),1);self.update();self.assertEqual(self.fn('urui_texture_exists',I,U)(image),0)
    def test_rect_bounds_source_and_owner_are_validated_before_mutation(self):
        owner,_=self.document()
        for args in [(b'icon',b'ui/missing.png',0,0,2,2,1),(b'icon',b'ui/atlas.png',7,0,2,2,1),
                     (b'icon',b'ui/atlas.png',-1,0,2,2,1),(b'icon',b'ui/atlas.png',0,0,0,2,1),
                     (b'icon',b'ui/atlas.png',0,0,2,2,float('nan'))]:
            self.assertLess(self.sprite(owner,*args),0)
        self.assertEqual(self.sprite(owner,b'icon',b'ui/atlas.png',0,0,3,4,1),1);self.show(owner)
        self.assertEqual(self.size(owner),(3,4))
        self.assertLess(self.sprite(0,b'icon',b'ui/atlas.png',0,0,3,4,1),0)
        self.assertEqual(self.close(owner),1)
        self.assertLess(self.sprite(owner,b'icon',b'ui/atlas.png',0,0,3,4,1),0)
    def test_ninepatch_regions_share_the_source_group(self):
        owner,image=self.document()
        self.assertEqual(self.sprite(owner,b'outer',b'ui/atlas.png',0,0,8,8,1),1)
        self.assertEqual(self.sprite(owner,b'inner',b'ui/atlas.png',2,2,4,4,1),1)
        page=b'<!doctype html><html><head><style>#box{width:40px;height:40px;--urui-decorator:ninepatch(outer,inner);}</style></head><body><div id="box"></div></body></html>'
        self.assertEqual(self.load(owner,page,b'ui/page.html'),1)
        self.assertEqual(self.present(owner,0,0,1,0),1);self.update();self.fn('urui_render',None)()
        count=self.fn('urui_command_count',I)();commands=(Command*count)()
        self.assertEqual(self.fn('urui_get_commands',I,P,I)(commands,count),count)
        self.assertTrue(any(command.texture==image for command in commands))
    def test_foreign_thread_is_rejected(self):
        owner,_=self.document();result=[]
        thread=threading.Thread(target=lambda:result.append(self.sprite(owner,b'icon',b'ui/atlas.png',0,0,2,2,1)))
        thread.start();thread.join();self.assertEqual(result,[-3])

if __name__=='__main__':unittest.main()
