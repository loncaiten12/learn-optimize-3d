from pathlib import Path
from PIL import Image,ImageOps,ImageDraw
import json
src=Path(__file__).resolve().parent;out=src.parents[1]/'Assets/OptimizationLab/TutorialMedia/Editor'
W,H=1000,600
manifest={}
def frame(filename,box,focus=None):
 im=Image.open(src/filename).convert('RGB').crop(box)
 scale=min(W/im.width,H/im.height)
 nw,nh=round(im.width*scale),round(im.height*scale)
 im=im.resize((nw,nh),Image.Resampling.LANCZOS)
 x,y=(W-nw)//2,(H-nh)//2
 canvas=Image.new('RGB',(W,H),'#171b23');canvas.paste(im,(x,y))
 # focus: one (x,y,w,h) box or a list of boxes, in crop coordinates.
 boxes=[focus] if focus and isinstance(focus[0],(int,float)) else (focus or [])
 d=ImageDraw.Draw(canvas)
 for fx,fy,fw,fh in boxes:
  rect=[x+fx*scale,y+fy*scale,x+(fx+fw)*scale,y+(fy+fh)*scale]
  d.rounded_rectangle(rect,radius=8,outline='#ffdc45',width=5)
 return canvas
mainbox=(920,1100,1450,1280)
treebox=(0,160,680,760)
enablebox=(0,60,1050,650)
for lesson in ('b01','b02'):
 folder=out/lesson;folder.mkdir(parents=True,exist_ok=True)
 a=lesson+'-main-a.png'; b=lesson+('-main-b-after.png' if lesson=='b02' else '-main-b.png')
 if lesson=='b02':b0='b02-main-b.png'
 else:b0=b
 specs={
 'choose-a':[(b0,mainbox,(8,45,165,67)),(a,mainbox,(8,45,165,67))],
 'open': [(a,mainbox,None),('debugger-off.png',enablebox,(0,45,380,48))],
 'enable-a':[('debugger-off.png',enablebox,(0,45,380,48)),(lesson+'-a-tree.png',enablebox,(0,45,380,48))],
 'opaque-a':[(lesson+'-a-tree.png',treebox,(47,190,615,37)),(lesson+'-a-open.png',treebox,(72,224,590,39))],
 'draw-a':[(lesson+'-a-open.png',treebox,(72,224,590,39)),(lesson+'-a-draws.png',treebox,(130,255,500,195))],
 'disable':[(lesson+'-a-draws.png',enablebox,(0,45,380,48)),('debugger-disabled.png',enablebox,(0,45,380,48))],
 'choose-b':[(a,mainbox,(180,45,170,67)),(b,mainbox,(180,45,170,67))],
 'enable-b':[('debugger-disabled.png',enablebox,(0,45,380,48)),(lesson+'-b-tree.png',enablebox,(0,45,380,48))],
 'opaque-b':[(lesson+'-b-tree.png',treebox,(47,190,615,37)),(lesson+'-b-open.png',treebox,(72,224,590,39)),(lesson+'-b-draws.png',treebox,(130,255,500,195))],
 }
 for name,spec in specs.items():
  frames=[frame(*x) for x in spec]
  # A pointing animation when two captures have identical content: flash the annotation, never fabricate UI states.
  if len(frames)==2 and frames[0].tobytes()==frames[1].tobytes():
   frames[0]=frame(spec[0][0],spec[0][1],None)
  for i,im in enumerate(frames): im.save(folder/f'{name}_{i}.png',optimize=True)
  # GIF = sequence of actual captured Unity states, held long enough to follow.
  if len(frames)==1:frames=frames+[frames[0].copy()]
  frames[0].save(folder/f'{name}.gif',save_all=True,append_images=frames[1:],duration=[1600]*len(frames),loop=0,optimize=False)
  manifest[f'{lesson}/{name}']=len(spec)
 # Actual A/B screenshots side by side for the last card.
 ca=frame(lesson+'-a-draws.png',treebox,(130,255,500,195))
 cb=frame(lesson+'-b-draws.png',treebox,(130,255,500,195))
 compare=Image.new('RGB',(1000,600),'#171b23')
 compare.paste(ca.resize((500,300)),(0,150));compare.paste(cb.resize((500,300)),(500,150))
 compare.save(folder/'compare_0.png',optimize=True)
 ca.save(folder/'compare_1.png',optimize=True);cb.save(folder/'compare_2.png',optimize=True)
 ca.save(folder/'compare.gif',save_all=True,append_images=[cb],duration=[2200,2200],loop=0)
 manifest[f'{lesson}/compare']=3
# B02 "Áp dụng vào project thật": real Inspector / Player Settings / Profiler captures (Unity GrabPixels, 2x).
def clip(lesson,name,spec):
 folder=out/lesson; frames=[frame(*x) for x in spec]
 for i,im in enumerate(frames): im.save(folder/f'{name}_{i}.png',optimize=True)
 frames[0].save(folder/f'{name}.gif',save_all=True,append_images=frames[1:],duration=[1600]*len(frames),loop=0,optimize=False)
 manifest[f'{lesson}/{name}']=len(frames)
staticbox=(0,0,880,240); staticfocus=(696,66,160,46)
clip('b02','out-static',[('b02-out-static-off.png',staticbox,staticfocus),('b02-out-static-on.png',staticbox,staticfocus)])
playertop=(500,40,1800,780); playerrow=(500,800,1800,1540)
clip('b02','out-player',[('b02-out-player.png',playertop,(694,10,600,38)),('b02-out-player.png',playertop,(325,588,309,40)),('b02-out-player.png',playerrow,(55,630,525,46))])
profbox=(540,540,2000,1340); rows=(6,450,1200,102)
clip('b02','out-profiler',[('b02-out-profiler-a.png',profbox,(10,10,360,50)),('b02-out-profiler-a.png',profbox,rows),('b02-out-profiler-b.png',profbox,rows)])
# B03: Frame Debugger / Game view / Inspector / Profiler captured with Unity GrabPixels (no macOS window chrome,
# so window crops sit 56 px higher than the b01/b02 screen captures; focus boxes stay in crop coordinates).
(out/'b03').mkdir(parents=True,exist_ok=True)
main3=(340,820,770,1010); tree3=(0,104,680,704); enable3=(0,4,1050,594)
fa,fb=(10,12,136,48),(150,12,136,48)
opaque=(47,190,615,37); loop=(72,224,590,39); draws_a=(130,255,500,195); draws_b=(130,252,500,70); enable_btn=(0,45,380,48)
clip('b03','choose-a',[('b03-main-b.png',main3,fa),('b03-main-a.png',main3,fa)])
clip('b03','open',[('b03-main-a.png',main3,None),('debugger-off.png',enablebox,enable_btn)])
clip('b03','enable-a',[('debugger-off.png',enablebox,enable_btn),('b03-a-tree.png',enable3,enable_btn)])
clip('b03','opaque-a',[('b03-a-tree.png',tree3,opaque),('b03-a-open.png',tree3,loop)])
clip('b03','draw-a',[('b03-a-open.png',tree3,loop),('b03-a-draws.png',tree3,draws_a)])
clip('b03','disable',[('b03-a-draws.png',enable3,enable_btn),('debugger-disabled.png',enablebox,enable_btn)])
clip('b03','choose-b',[('b03-main-a.png',main3,fb),('b03-main-b.png',main3,fb)])
clip('b03','enable-b',[('debugger-disabled.png',enablebox,enable_btn),('b03-b-tree.png',enable3,enable_btn)])
clip('b03','opaque-b',[('b03-b-tree.png',tree3,opaque),('b03-b-open.png',tree3,loop),('b03-b-draws.png',tree3,draws_b)])
ca=frame('b03-a-draws.png',tree3,draws_a); cb=frame('b03-b-draws.png',tree3,draws_b)
compare=Image.new('RGB',(1000,600),'#171b23'); compare.paste(ca.resize((500,300)),(0,150)); compare.paste(cb.resize((500,300)),(500,150))
compare.save(out/'b03'/'compare_0.png',optimize=True); ca.save(out/'b03'/'compare_1.png',optimize=True); cb.save(out/'b03'/'compare_2.png',optimize=True)
ca.save(out/'b03'/'compare.gif',save_all=True,append_images=[cb],duration=[2200,2200],loop=0)
manifest['b03/compare']=3
matbox=(0,40,880,780)
clip('b03','out-material',[('b03-out-material-closed.png',matbox,(20,525,300,40)),('b03-out-material-open.png',matbox,(60,688,420,42)),('b03-out-material-on.png',matbox,(60,688,420,42))])
clip('b03','out-profiler',[('b03-out-profiler-a.png',profbox,(10,10,360,50)),('b03-out-profiler-a.png',profbox,[(6,450,1200,36),(6,532,1200,42)]),('b03-out-profiler-b.png',profbox,[(6,450,1200,36),(6,532,1200,42)])])
(out/'media_manifest.json').write_text(json.dumps(manifest,indent=2))
print('Built',len(manifest),'clips; PNG flipbooks and GIFs from real captures')
