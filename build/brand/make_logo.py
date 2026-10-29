from PIL import Image, ImageDraw, ImageFilter
import math
S=1024
def logo(S=1024, bg=True):
    im=Image.new('RGBA',(S,S),(0,0,0,0))
    if bg:
        # rounded square, vertical gradient
        grad=Image.new('RGBA',(S,S))
        gd=ImageDraw.Draw(grad)
        for y in range(S):
            t=y/S
            c=(int(46*(1-t)+16*t),int(50*(1-t)+18*t),int(64*(1-t)+24*t),255)
            gd.line([(0,y),(S,y)],fill=c)
        mask=Image.new('L',(S,S),0); ImageDraw.Draw(mask).rounded_rectangle([0,0,S-1,S-1],radius=int(S*0.22),fill=255)
        im.paste(grad,(0,0),mask)
        d=ImageDraw.Draw(im)
        d.rounded_rectangle([S*0.025,S*0.025,S*0.975,S*0.975],radius=int(S*0.2),outline=(150,104,36,255),width=int(S*0.012))
    gold=(255,182,52,255); deep=(232,140,24,255)
    layer=Image.new('RGBA',(S,S),(0,0,0,0)); d=ImageDraw.Draw(layer)
    # P: stem (a wrench handle) + bowl (a closed ring, with a small wrench-jaw notch on the right)
    t=S*0.13                          # stroke thickness
    sx0=S*0.17; sx1=sx0+t
    top=S*0.17; bot=S*0.83
    d.rounded_rectangle([sx0,top,sx1,bot],radius=int(t*0.45),fill=gold)
    # bowl: outer rounded shape from the stem to the right, inner hole
    bx1=S*0.66; by0=top; by1=S*0.56
    d.rounded_rectangle([sx0,by0,bx1,by1],radius=int((by1-by0)/2),fill=gold)
    d.rounded_rectangle([sx1,by0+t,bx1-t,by1-t],radius=int((by1-by0-2*t)/2),fill=(0,0,0,0))
    # keep the stem solid where the hole began
    d.rectangle([sx0,by0+t*0.5,sx1,by1-t*0.5],fill=gold)
    # wrench jaw notch at the right of the bowl
    my=(by0+by1)/2
    d.polygon([(bx1-t*0.95,my-t*0.18),(bx1+2,my-t*0.55),(bx1+2,my+t*0.55),(bx1-t*0.95,my+t*0.18)],fill=(0,0,0,0))
    # plus, upper right
    px,py,pl,pw=S*0.79,S*0.30,S*0.115,S*0.048
    d.rounded_rectangle([px-pl,py-pw,px+pl,py+pw],radius=int(pw*0.6),fill=gold)
    d.rounded_rectangle([px-pw,py-pl,px+pw,py+pl],radius=int(pw*0.6),fill=gold)
    # subtle shading: darker towards bottom
    shade=Image.new('L',(S,S)); sd=ImageDraw.Draw(shade)
    for y in range(S): sd.line([(0,y),(S,y)],fill=int(255*min(1,max(0,(y/S-0.2)*0.6))))
    dark=Image.new('RGBA',(S,S),deep)
    alpha=layer.split()[3]
    layer=Image.composite(Image.composite(dark,layer,shade),layer,alpha.point(lambda v:255 if v>0 else 0))
    layer.putalpha(alpha)
    # glow
    glow=layer.filter(ImageFilter.GaussianBlur(S*0.02)); 
    g2=Image.new('RGBA',(S,S),(255,170,40,0)); g2.putalpha(glow.split()[3].point(lambda v:int(v*0.45)))
    im=Image.alpha_composite(im,g2)
    im=Image.alpha_composite(im,layer)
    return im
big=logo()
big.save('/tmp/logo1024.png')
big.resize((256,256),Image.LANCZOS).save('/tmp/logo256.png')
sheet=Image.new('RGBA',(1024+40+256+40+64+40+32+16,1024),(240,240,240,255))
x=0
for s in (1024,256,64,32,16):
    sheet.paste(big.resize((s,s),Image.LANCZOS),(x,0),big.resize((s,s),Image.LANCZOS)); x+=s+40
sheet.convert('RGB').save('/tmp/logosheet.png')
sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)]
big.save('/tmp/app.ico',sizes=sizes)
big.resize((512,512),Image.LANCZOS).save('/tmp/logo512.png')
big.resize((96,96),Image.LANCZOS).save('/tmp/logo96.png')
