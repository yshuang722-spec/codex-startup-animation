/* Mac adaptation of Artemis / Tom's Codex Engine Boot (MIT).
 * Upstream: yshuang722-spec/codex-startup-animation @ c64fee6a56217a85a668ae8825511c858d9ac1d9
 * Decorative animation only: the generated code, model names and percentages are not live client telemetry.
 */
(() => {
  'use strict';
  const canvas = document.getElementById('stage');
  const ctx = canvas.getContext('2d', {alpha:true});
  const LOGO = new Path2D(window.UPSTREAM_LOGO_PATH);
  const TOTAL = 15.28, SCENE_END = 14.95, PROGRESS_AT = 6.8;
  const green = '#57ffa9', mono = 'Menlo, Monaco, monospace', sans = '-apple-system, BlinkMacSystemFont, "PingFang SC", sans-serif';
  let w=1440, h=900, dpr=1, reduced=false, started=0, handoffStarted=null, duration=TOTAL, raf=null, done=false;
  const clamp=(n,a=0,b=1)=>Math.max(a,Math.min(b,n));
  const rgba=(r,g,b,a=1)=>`rgba(${r},${g},${b},${clamp(a)})`;
  const notify=s=>{if(window.webkit?.messageHandlers?.boot)window.webkit.messageHandlers.boot.postMessage(s);};
  function resize(width=window.innerWidth,height=window.innerHeight,pixelRatio=window.devicePixelRatio||1){
    if(w===width&&h===height&&dpr===pixelRatio&&canvas.width===Math.round(width*pixelRatio)&&canvas.height===Math.round(height*pixelRatio))return;
    w=width;h=height;dpr=pixelRatio;canvas.width=Math.round(w*dpr);canvas.height=Math.round(h*dpr);
  }
  function rect(x,y,width,height,color){if(width<=0||height<=0)return;ctx.fillStyle=color;ctx.fillRect(x,y,width,height);}
  function round(x,y,width,height,r,color,stroke=null,lineWidth=1){
    if(width<=0||height<=0)return;ctx.beginPath();ctx.roundRect(x,y,width,height,Math.min(r,width/2,height/2));
    if(color){ctx.fillStyle=color;ctx.fill();}if(stroke){ctx.strokeStyle=stroke;ctx.lineWidth=lineWidth;ctx.stroke();}
  }
  function line(x,y,x2,y2,color,width=1){ctx.beginPath();ctx.moveTo(x,y);ctx.lineTo(x2,y2);ctx.strokeStyle=color;ctx.lineWidth=width;ctx.stroke();}
  function circle(x,y,r,color,stroke=null,width=1){ctx.beginPath();ctx.arc(x,y,r,0,Math.PI*2);if(color){ctx.fillStyle=color;ctx.fill();}if(stroke){ctx.strokeStyle=stroke;ctx.lineWidth=width;ctx.stroke();}}
  function text(s,x,y,size,color=green,code=false,align='left',maxWidth=null){
    ctx.font=`${size}px ${code?mono:sans}`;ctx.textBaseline='top';ctx.textAlign=align;ctx.fillStyle=color;
    if(maxWidth){while(ctx.measureText(s).width>maxWidth && s.length>1)s=s.slice(0,-2)+'…';}
    ctx.fillText(s,x,y);
  }
  function glow(s,x,y,size,align='left',code=false){ctx.save();ctx.shadowColor='#30ff87';ctx.shadowBlur=9;text(s,x,y,size,'#6cff9d',code,align);ctx.restore();}
  function clipped(x,y,width,height,draw){ctx.save();ctx.beginPath();ctx.rect(x,y,width,height);ctx.clip();draw();ctx.restore();}
  function logo(x,y,size,rotation=0,alpha=1){
    ctx.save();ctx.globalAlpha*=alpha;ctx.translate(x,y);ctx.rotate(rotation);ctx.translate(-size/2,-size/2);ctx.scale(size/721,size/721);
    let gradient=ctx.createLinearGradient(0,0,721,721);gradient.addColorStop(0,'#00d3ee');gradient.addColorStop(1,'#19ff9e');ctx.fillStyle=gradient;ctx.fill(LOGO);ctx.restore();
  }
  function codexMark(x,y,size){
    ctx.save();ctx.translate(x-size/2,y-size/2);ctx.scale(size/68,size/68);
    ctx.beginPath();ctx.moveTo(32,5);[[39,5,44,8,47,13],[56,13,61,20,59,29],[66,36,62,47,53,50],[51,59,42,64,34,60],[25,65,15,61,13,52],[4,49,1,39,6,32],[2,23,7,14,16,13],[18,7,25,3,32,5]].forEach(v=>ctx.bezierCurveTo(...v));ctx.closePath();
    ctx.moveTo(22,25);ctx.lineTo(27,33);ctx.lineTo(22,41);ctx.moveTo(36,42);ctx.lineTo(47,42);
    ctx.lineCap='round';ctx.lineJoin='round';ctx.strokeStyle='#5eff8f';ctx.lineWidth=2.25;ctx.shadowBlur=8;ctx.shadowColor='#2aff69';ctx.stroke();ctx.restore();
  }
  function engine(t){
    const size=Math.min(w*.352,h*.688),x=w/2,y=h/2;
    logo(x,y,size,reduced?0:(-31*t*t*t)*Math.PI/180,Math.min(.84,t/.55));
    const radius=size*.56;
    for(let i=0;i<36;i++){const a=(i*10-90)*Math.PI/180;line(x+radius*Math.cos(a),y+radius*Math.sin(a),x+radius*Math.cos(a+.10),y+radius*Math.sin(a+.10),rgba(36,255,201,.08+.75*clamp(t*9-i)),Math.max(2,size*.008));}
    if(!reduced){const flash=Math.max(0,1-Math.abs(t-4.65)/.18);if(flash>0){for(let k=8;k>=1;k--)circle(x,y,size*k*.025,rgba(170,255,244,flash*.07));line(x-size*.65,y,x+size*.65,y,rgba(220,255,255,flash),2);}}
  }
  const nouns=['horizon','workspace','afterglow','quietField','softSignal','starlight','canvas','daybreak','contour','echo','northWindow','luminousField','openCircuit','moonGarden','clearCurrent','velvetSky','signalBloom'];
  const actions=['shape','weave','render','trace','compose','settle','align','gather','tune','bloom','thread','unfold','illuminate','reconcile','synchronize','discover','carry'];
  const moods=['softly','in the distance','under glass','slowly','between quiet layers','without a map','in green light','after midnight','like distant starlight','almost silently','with patient hands','beneath a pale sky','where edges soften','in a room of dawn','along the horizon','as noise falls away','under a calm current'];
  const words=['paper moon','open air','small orbit','green room','distant shore','soft focus','clear water','silver thread','night garden','warm static','quiet constellation','window after rain','a map of light','the shape of silence','a garden in winter','slow-moving weather','a signal from home','the first line of morning','where the river turns','the color of remembering','a lantern in the fog'];
  const places=['north window','violet horizon','open water','upper gallery','quiet station','glass garden','far shoreline','winter orchard','small observatory','inner courtyard','blue hour','unfinished room','edge of morning','silver valley','soft-lit archive','western sky','hidden atrium'];
  function terminalLine(n){
    const slot=n%60;if([0,15,16,29,30,40,41,52,59].includes(slot))return '';
    const noun=nouns[(n*17+Math.floor(n/7)*13+3)%nouns.length],action=actions[(n*11+Math.floor(n/9)*7+5)%actions.length];
    const phrase=words[(n*13+Math.floor(n/6)*5)%words.length],place=places[(n*7+Math.floor(n/4)*3+2)%places.length],mood=moods[(n*19+Math.floor(n/5)*3+1)%moods.length];
    const lines=[`// "${phrase}" settles at the ${place} as the noise falls away`,`const ${noun} = await interface.${action}("${phrase}", {`,`  title: "${phrase} / ${place}",`,`  palette: "#42F5B3", // held in green light`,`  intention: "room around every thought",`,`  atmosphere: "${mood} over the ${place}",`,`  motion: "quiet drift", // a breath between beats`,`  anchor: "${place} near ${phrase}",`,`  spacing: ${18+(n*7)%47}px, // keep the quiet parts open`,`  signal: await channel.${action}("${noun}"),`,`  layers: ["${phrase}", "${words[(n+9)%words.length]}"],`,`  texture: "thin light on the surface",`,`  onReady: scene => scene.${action}("${mood}")`,`}); // leave room for whatever comes next`];
    return lines[(slot-1)%lines.length];
  }
  function floatingCard(p,start,closeAt,x,y,width,height,category,title,detail,extra,status,accent){
    let age=p-start;if(age<0||p>closeAt+.22)return;
    let a=clamp(age/.2),b=clamp((p-closeAt)/.22);a=a*a*(3-2*a);b=b*b*(3-2*b);
    ctx.save();ctx.globalAlpha*=a*(1-b);ctx.translate(0,reduced?0:(1-a)*13-7*b);
    if(!reduced){const s=.95+.05*a-.04*b;ctx.translate(x+width/2,y+height/2);ctx.scale(s,s);ctx.translate(-x-width/2,-y-height/2);}
    round(x,y,width,height,9,rgba(4,14,18,.96),rgba(88,203,190,.64),1.1);rect(x+1,y+1,width-2,2,accent);circle(x+15,y+17,3.2,accent);
    text(category,x+25,y+10,Math.min(10,width/30),rgba(158,220,211,.9),true,'left',width-54);line(x+13,y+31,x+width-13,y+31,rgba(105,158,152,.25));
    text(title,x+14,y+Math.max(38,height*.29),Math.min(17,width/22),'#e9faf6',false,'left',width-28);
    text(detail,x+14,y+Math.max(59,height*.47),Math.min(11,width/31),rgba(148,196,188,.87),true,'left',width-28);
    if(extra)text(extra,x+14,y+height*.67,Math.min(10,width/34),rgba(126,224,204,.88),true,'left',width-28);
    const by=y+height-32,bw=Math.min(width-28,Math.max(112,status.length*7.2+24));round(x+13,by,bw,20,5,rgba(7,40,35,.93));rect(x+20,by+8,5,5,accent);text(status,x+32,by+3,Math.min(10,(bw-26)/(status.length*.61)),rgba(174,255,221,.96),true);text('×',x+width-23,y+8,14,rgba(171,211,203,.7),true);ctx.restore();
  }
  function telemetry(p,sidebar){
    const top=427,available=h-top-24;if(available<125)return;
    const width=sidebar-36,height=available,x=18,fs=Math.min(10,width/26),gy=top+57,gh=Math.max(150,height-244);
    ctx.save();ctx.globalAlpha*=clamp((p-1.4)*.8,0,.9);text('NEURAL CONSTELLATION',x+8,top+8,Math.min(11,width/18),rgba(139,241,204,.9),true);text('VERTICAL SIGNAL MAP',x+8,top+24,fs,rgba(99,174,154,.75),true);line(x+8,top+42,x+width-8,top+42,rgba(89,205,170,.25));
    if(height<330){
      const chartY=top+64,chartH=Math.max(20,Math.min(64,height-118)),bw=(width-20-2*19)/20;
      for(let i=0;i<20;i++){const bh=6+(chartH-6)*clamp(.5+.3*Math.sin(p*.8+i*2.39),.08,1);rect(x+10+i*(bw+2),chartY+chartH-bh,bw,bh,rgba(57,230,157,.5));}
      text('Artemis 原作 · Mac 适配',x+8,top+height-44,Math.min(10,width/21),rgba(104,235,169,.72));
      text('SIGNAL FLOW // ONLINE',x+8,top+height-18,fs,rgba(130,223,191,.78),true);ctx.restore();return;
    }
    clipped(x,gy-5,width,gh+10,()=>{
      for(let i=0;i<52;i++){const sx=x+12+(width-24)*((i*47%97)/97),sy=gy+gh*((i*67%89)/89),twinkle=.35+.35*(1+Math.sin(p*1.7+i*2.13));circle(sx,sy,i%9===0?1.35:.65,rgba(127,255,215,.16+twinkle*.28));}
      const layers=[3,5,7,8,7,4].map((count,l)=>Array.from({length:count},(_,n)=>({x:x+21+(width-42)*(n+.5)/count+Math.sin(n*2.1+l*1.7)*width*.035+Math.sin(p*.33+l*.9)*width*.035,y:gy+gh*(l+.5)/6+Math.sin(n*2.4+l)*5})));
      layers.slice(0,-1).forEach((layer,l)=>layer.forEach((from,a)=>{const mapped=Math.round((a+.5)*layers[l+1].length/layer.length-.5);for(let offset=-1;offset<=1;offset++){const b=mapped+offset,to=layers[l+1][b];if(!to)continue;line(from.x,from.y,to.x,to.y,rgba(56,185,145,offset===0?.2:.105),offset===0?.85:.6);if((a*3+b+l)%3===0){const flow=(p*.25+l*.21+a*.17+b*.13)%1;circle(from.x+(to.x-from.x)*flow,from.y+(to.y-from.y)*flow,1.25,rgba(166,255,221,.9));}}}));
      layers.forEach((layer,l)=>layer.forEach((point,n)=>{const pulse=.4+.3*(1+Math.sin(p*2.4-l*.8-n*.7));circle(point.x,point.y,9,rgba(48,255,166,.06+pulse*.065));circle(point.x,point.y,3.5,rgba(3,25,21,.98),rgba(105,255,194,pulse*.82),.9);circle(point.x,point.y,1.35,rgba(170,255,225,pulse));}));
    });
    const chartY=top+height-156,chartX=x+10,chartW=width-20,chartH=53;
    text('RUNTIME SIGNAL',chartX,chartY-18,fs,rgba(126,211,179,.82),true);text('NODES 34',chartX+chartW*.66,chartY-18,fs,rgba(126,211,179,.82),true);line(chartX,chartY+chartH,chartX+chartW,chartY+chartH,rgba(92,194,159,.32));
    for(let i=0;i<28;i++){const bw=(chartW-2*27)/28,seed=(i*73+19)%101/101,wave=clamp(.48+.24*Math.sin(p*(.65+seed*.8)+i*2.39)+.17*Math.sin(p*1.31+i*4.71)+.11*Math.sin(p*.43+i*.83),.06,1),bh=5+wave*(chartH-6);rect(chartX+i*(bw+2),chartY+chartH-bh,bw,bh,rgba(57,230,157,.19+clamp(p/6.8,.15,1)*(.16+.19*wave)));}
    text('Artemis 原作 · Mac 适配',chartX,chartY+chartH+28,Math.min(11,width/21),rgba(104,235,169,.72));text(p>4?'SIGNAL FLOW // ONLINE':'SIGNAL FLOW // SYNCHRONIZING',x+8,top+height-17,fs,rgba(130,223,191,.78),true);ctx.restore();
  }
  function selector(p,x,y,width,height){
    const progress=clamp(p/PROGRESS_AT),sp=clamp((progress-2.5/PROGRESS_AT)/(1-2.5/PROGRESS_AT)),charged=progress>=1;
    const names=['GPT-5.6 Luna','GPT-5.6 Terra','GPT-5.6 Sol','GPT-6 Luna','GPT-6 Sol','GPT-6.1 Sol','GPT-6 Astra'],levels=['轻度','中高','极高','极高','极高','极高','Ultra'],index=Math.min(6,Math.floor(sp*7));
    const cw=Math.min(274,width-16),ch=Math.min(88,height-40);if(cw<140||ch<60)return;
    const cx=x+width-cw-8,cy=y+height-ch-8,center=cx+cw/2;
    round(cx-3,cy-3,cw+6,ch+6,19,rgba(48,255,173,.035+.12*sp));round(cx,cy,cw,ch,17,rgba(5,20,18,.98),rgba(70,156+99*sp,117+75*sp,.55+.4*sp),1.15);
    text('ϟ',cx+12,cy+8,27,charged?'#57ff88':'#a0d7b8');round(center-cw*.29,cy+3,cw*.58,43,10,rgba(43,255,172,.035+.12*sp));
    text(levels[index],center,cy+5,14,rgba(111,188+67*sp,128+65*sp,.7+.3*sp),false,'center');text(names[index],center,cy+26,Math.min(15,cw*.045),rgba(171,220+35*sp,194+61*sp,.78+.22*sp),false,'center');
    const tx=cx+13,ty=cy+ch-25,tw=cw-26;round(tx,ty,tw,14,7,rgba(21,72,54,.95));
    const gradient=ctx.createLinearGradient(tx,ty,tx+tw,ty);gradient.addColorStop(0,charged?'#24ef5b':'#26b774');gradient.addColorStop(1,charged?'#8effaf':'#68fff0');round(tx,ty,tw*sp,14,7,gradient);
    const thumbX=tx+7.5+(tw-15)*sp;circle(thumbX,ty+7,12,rgba(105,255,189,.2+.16*sp));circle(thumbX,ty+7,9,'#e4fff0','#50e0a0',.8);
  }
  function ready(p){
    if(p<PROGRESS_AT+.02)return;ctx.save();ctx.globalAlpha*=clamp((p-PROGRESS_AT-.02)/.18);
    const fs=Math.min(46,w/34,h/25)*1.3,title='SYSTEM READY TO LAUNCH';ctx.font=`${fs}px ${mono}`;
    const boxW=Math.min(w-32,ctx.measureText(title).width+72),titleSize=Math.min(fs,(boxW-36)/(title.length*.61)),boxH=Math.max(154,28+titleSize*1.24+8+29+36),bx=(w-boxW)/2,by=(h-boxH)/2;
    round(bx,by,boxW,boxH,12,rgba(1,13,15,.97),rgba(104,241,212,.92),1.5);rect(bx+22,by+12,boxW-44,2,rgba(93,255,210,.86));
    text(title,w/2,by+28,titleSize,green,true,'center');text('GPT INTERFACE  //  ONLINE',w/2,by+28+titleSize*1.24+8,Math.min(12,(boxW-32)/16),rgba(143,255,217,.96),true,'center');
    if(p>=7.06)text('> ENTER  ↵',w/2,by+28+titleSize*1.24+8+29,16,green,true,'center');ctx.restore();
  }
  function compactScene(p){
    rect(0,0,w,h,'#020a0c');codexMark(w/2,h*.19,70);glow('我们要构建什么？',w/2,h*.19+65,24,'center');
    const top=h*.4;round(18,top,w-36,h-top-44,16,'#00120f','#1d5141');text('GPT ENGINE / VISUAL SEQUENCE',32,top+18,Math.min(11,(w-64)/19),green,true);
    clipped(30,top+50,w-60,h-top-110,()=>{for(let i=0;i<22;i++)text(terminalLine(Math.floor(p*16)+i),32,top+52+i*20,11,rgba(78,255,157,.75),true);});
    if(p>2&&p<6){round(28,h*.53,w-56,110,9,'#040e12','#58cbbe');text('INTERFACE / COMPOSITION',44,h*.53+16,10,green,true);text('Preparing the next idea',44,h*.53+43,17,'#e9faf6');text('DECORATIVE ANIMATION',44,h*.53+76,10,'#94c4bc',true);}
    text('VISUAL SEQUENCE',w/2,h-30,9,'#749b8b',true,'center');rect(0,h-5,w*clamp(p/PROGRESS_AT),3,green);ready(p);
  }
  function scene(p){
    if(w<760){compactScene(p);return;}
    const progress=clamp(p/PROGRESS_AT),fs=Math.max(11,Math.min(15,w/100)),lh=fs*1.48,sidebar=w*.21,terminal=w*.58;
    rect(0,0,w,h,'#020a0c');
    if(!reduced)for(let i=0;i<w/24;i++){const y=(p*(55+i%7*13)+i*89)%(h+180)-180;for(let j=0;j<12;j++)text(((i*17+j*31)%16).toString(16).toUpperCase(),i*24,y+j*18,12,rgba(15,210,126,.035+j*.006),true);}
    rect(terminal,0,w-terminal,h,rgba(0,12,10,.94));text('GPT ENGINE / INTERFACE COMPILER',terminal+20,18,fs,green,true,'left',w-terminal-36);
    clipped(terminal+16,48,w-terminal-24,h-110,()=>{
      const rows=Math.ceil((h-95)/lh),generated=Math.floor(p*(reduced?12:32));ctx.font=`${fs}px ${mono}`;const maxChars=Math.max(32,Math.floor((w-terminal-52)/ctx.measureText('M').width));
      for(let i=0;i<Math.min(rows,generated);i++){let s=terminalLine(Math.max(0,generated-rows)+i);if(s.length>maxChars)s=s.slice(0,maxChars-2)+'..';if(s)text(s,terminal+20,50+i*lh,fs,rgba(78,255,157,.65+i%4*.1),true);}
    });
    ctx.save();ctx.globalAlpha*=Math.min(1,p*2);rect(0,0,sidebar,h,rgba(13,26,27,.95));glow('Codex',24,25,22);ctx.restore();
    const nav=['+  New chat','Search conversations','Library','Explore GPTs','Recent conversations','Project workspace','Settings'];
    nav.forEach((s,i)=>{if(p>.16+i*.19){round(14,85+i*47,sidebar-28,36,10,rgba(28,65,53,.65),rgba(90,231,175,.19),.7);text(s,24,93+i*47,Math.min(14,fs),green,false,'left',sidebar-46);}});telemetry(p,sidebar);
    if(p>.75)glow('Codex',sidebar+24,24,18);
    if(p>1.3){const cx=(sidebar+terminal)/2;codexMark(cx,h*.36-64,88.8);glow('我们要构建什么？',cx,h*.36+35,Math.min(34,w/34),'center');}
    const inputH=Math.min(190,h*.28),inputX=sidebar+12,inputY=h-inputH-23,inputW=terminal-sidebar-24;
    ['Create','Explore','Think'].forEach((s,i)=>{if(p>1.7+i*.24){const cw=(terminal-sidebar-64)/3,x=sidebar+20+i*(cw+12),y=inputY-80;round(x,y,cw,60,13,rgba(19,49,41,.9),rgba(90,231,175,.19),.7);text(s,x+14,y+20,fs,green,false,'left',cw-20);}});
    if(p>2.5){round(inputX,inputY,inputW,inputH,22,'#223f38',rgba(90,231,175,.19),.7);text('Message Codex  _',inputX+15,inputY+11,fs,'#c3f2d8');selector(p,inputX,inputY,inputW,inputH);}
    const cards=[
      [.98,5.15,.03,.20,.23,.13,'ACCESS / GATEWAY','Conversation channel','Encrypted stream established','','ACCESS GRANTED','#3befbe'],
      [1.53,5.33,.38,.13,.42,.23,'MEMORY / INDEX','Context archive linked','Recent history mapped to core','CONTEXT WINDOW  //  ALIGNED','SYNC COMPLETE','#68beff'],
      [2.08,5.51,.34,.40,.19,.12,'TOOLS / RUNTIME','Sandbox bridge','Toolchain attached','','READY','#b38fff'],
      [2.63,5.69,.46,.43,.27,.15,'ENGINE / TELEMETRY','Neural bus stable','Signal integrity nominal','','CORE ONLINE','#ffcd75'],
      [3.18,5.87,.045,.52,.41,.23,'MODEL / ROUTING','Inference path selected','Primary route confirmed','MODEL ROUTE  //  COMMITTED','ROUTE LOCKED','#ff917d'],
      [3.73,6.05,.75,.62,.22,.13,'NETWORK / RELAY','Secure relay active','Latency corridor clear','','LINK STABLE','#66dfff'],
      [4.28,6.23,.33,.69,.25,.12,'UI / RENDERER','Interface layers built','Navigation and canvas linked','','RENDER READY','#9bff9a'],
      [4.83,6.41,.68,.20,.20,.15,'SAFETY / GUARDRAIL','Policy layer mounted','Response boundary loaded','','PROTECTION ON','#ee97d5']
    ];
    cards.forEach(c=>floatingCard(p,c[0],c[1],w*c[2],h*c[3],w*c[4],Math.max(110,h*c[5]),...c.slice(6)));
    for(let y=0;y<h;y+=4)rect(0,y,w,1/dpr,rgba(0,0,0,.012));
    text(p<PROGRESS_AT?`COMPILING  ${Math.min(99,Math.floor(progress*100))}%`:'CORE ONLINE / INTERFACE READY',terminal+20,h-51,fs,green,true,'left',w-terminal-36);
    text('VISUAL SEQUENCE',terminal+20,h-28,Math.min(10,fs),rgba(132,173,154,.8),true,'left',w-terminal-36);
    const barH=Math.max(3,h*.004),by=h-barH-2;rect(0,by,w,barH,rgba(24,61,53,.88));const gradient=ctx.createLinearGradient(0,by,w,by);gradient.addColorStop(0,'#2befb0');gradient.addColorStop(1,'#71ffee');rect(0,by,w*progress,barH,gradient);ready(p);
  }
  function renderAt(t,reduceMotion=reduced){
    reduced=reduceMotion;ctx.setTransform(dpr,0,0,dpr,0,0);ctx.globalAlpha=1;ctx.shadowBlur=0;ctx.clearRect(0,0,w,h);
    if(t<5){engine(Math.max(0,t));return;}
    if(t<SCENE_END){scene(t-5);return;}
    const c=t-SCENE_END;if(c>=.33)return;
    if(reduced){ctx.save();ctx.globalAlpha=1-clamp(c/.33);scene(9.95);ctx.restore();return;}
    if(c<.055){ctx.save();ctx.translate(w/2,h/2);ctx.scale(1,Math.max(.001,1-c/.055));ctx.translate(-w/2,-h/2);scene(9.95);ctx.restore();}
    else if(c<.20)rect(0,0,w,h,'#000');
    else{const band=(h/2)*clamp((c-.20)/.09);rect(0,0,w,h/2-band,'#000');rect(0,h/2+band,w,h/2-band,'#000');}
    if(c<.29){const a=c<.20?1:clamp(1-(c-.20)/.09);rect(0,h/2-8,w,16,rgba(30,255,226,a*.26));rect(0,h/2-4,w,8,rgba(150,255,250,a*.62));rect(0,h/2-1,w,2,rgba(255,255,255,a));}
  }
  function frame(now){
    const elapsed=(now-started)/1000*TOTAL/duration;
    const t=handoffStarted===null?Math.min(elapsed,SCENE_END-.001):SCENE_END+(now-handoffStarted)/1000*TOTAL/duration;
    renderAt(t);if(t>=TOTAL){if(!done){done=true;document.documentElement.classList.add('finished');notify('finished');}return;}raf=requestAnimationFrame(frame);
  }
  window.boot={
    start(seconds=TOTAL,reduceMotion=false){cancelAnimationFrame(raf);resize();duration=seconds;reduced=reduceMotion;started=performance.now();handoffStarted=null;done=false;document.documentElement.classList.remove('finished');raf=requestAnimationFrame(frame);},
    handoff(){if(handoffStarted===null)handoffStarted=performance.now();},
    capture(width,height,t,reduceMotion=false){resize(width,height,1);renderAt(t,reduceMotion);const data=ctx.getImageData(0,0,w,h).data;let transparent=0,opaque=0;for(let i=3;i<data.length;i+=4){if(data[i]===0)transparent++;if(data[i]===255)opaque++;}return {png:canvas.toDataURL('image/png'),width:w,height:h,time:t,reducedMotion:reduceMotion,transparentPixels:transparent,opaquePixels:opaque};},
    selfTest(){
      const checks=[];const assert=(v,name)=>{if(!v)throw new Error(name);checks.push(name);};
      assert(TOTAL===SCENE_END+.33,'complete timeline is bounded');
      assert(terminalLine(0)==='','code paragraphs preserve blank lines');
      assert(Array.from({length:60},(_,i)=>terminalLine(i)).join('\n')!==Array.from({length:60},(_,i)=>terminalLine(i+60)).join('\n'),'generated code varies between blocks');
      for(const [name,t,expectTransparent] of [['intro',2.5,true],['scene',8,false],['finished',15.28,true]]){const c=this.capture(160,100,t);assert(expectTransparent?c.transparentPixels>0:c.opaquePixels===16000,`${name} alpha matches overlay phase`);if(name==='finished')assert(c.transparentPixels===16000,'finished overlay is completely transparent');}
      assert(!document.querySelector('a,iframe,form'),'animation has no external navigation controls');
      return checks;
    }
  };
  window.addEventListener('resize',()=>resize());
  document.getElementById('skip').addEventListener('click',()=>notify('skip'));
  window.addEventListener('error',event=>notify('error:'+event.message+' @ '+event.filename+':'+event.lineno+':'+event.colno+' '+(event.error?.stack||'')));
  resize();renderAt(0);
})();
