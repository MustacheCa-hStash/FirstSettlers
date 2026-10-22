const fs=require('fs'),path=require('path');
const sharp=require('C:/Users/samee/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/sharp');
const folder=path.resolve('ArtReferences/UIRedesign/TransparentHotbarStudy');
const sceneFolder=path.resolve('ArtReferences/UIRedesign/PeriodStudy/Backgrounds');
const variants=[
{key:'a-open-corners',label:'A · Open stone corners',pop:{left:36,top:155,width:1972,height:212},empty:{left:36,top:505,width:1972,height:212}},
{key:'b-smoke-tiles',label:'B · Translucent tiles',pop:{left:24,top:150,width:1996,height:208},empty:{left:24,top:460,width:1996,height:208}},
{key:'c-stone-underlines',label:'C · Stone underlines',pop:{left:70,top:130,width:1940,height:232},empty:{left:70,top:445,width:1940,height:232}},
{key:'d-floating-icons',label:'D · Almost frameless',pop:{left:36,top:210,width:1972,height:210},empty:{left:36,top:520,width:1972,height:225}}
];
const scenes=[{key:'meadow',file:'source-01.png'},{key:'forest',file:'source-02.png'}];
const records=[];
function label(text,width,height,fontSize=23){return Buffer.from('<svg width="'+width+'" height="'+height+'"><text x="0" y="'+Math.round(height*.72)+'" fill="#eee7d6" font-family="Segoe UI,Arial" font-size="'+fontSize+'">'+text+'</text></svg>');}
(async()=>{
const layers={};
for(const v of variants){
const {data,info}=await sharp(path.join(folder,v.key+'-raw.png')).ensureAlpha().raw().toBuffer({resolveWithObject:true});
for(let i=0;i<data.length;i+=4)if(data[i+3]<=8)data[i]=data[i+1]=data[i+2]=data[i+3]=0;
for(const state of ['pop','empty']){
const input=await sharp(data,{raw:info}).extract(v[state]).png().toBuffer();
fs.writeFileSync(path.join(folder,v.key+'-'+state+'-layer.png'),input);
layers[v.key+'-'+state]=input;
}
}
for(const scene of scenes){
const bg=path.join(sceneFolder,scene.file),m=await sharp(bg).metadata();
const before=await sharp(bg).removeAlpha().raw().toBuffer();
const board=[],cellW=1400,cropW=2100,cropH=290,imageH=Math.round(cropH*cellW/cropW),pad=20,titleH=44,labelH=34,gap=14;
for(let vi=0;vi<variants.length;vi++){
const v=variants[vi],y=pad+titleH+vi*(labelH+imageH*2+gap*2);
board.push({input:label(v.label,cellW,labelH),left:pad,top:y});
for(let si=0;si<2;si++){
const state=si===0?'pop':'empty',src=layers[v.key+'-'+state],sm=await sharp(src).metadata();
const w=Math.round(m.width*.42),h=Math.round(sm.height*w/sm.width),left=Math.round((m.width-w)/2),top=m.height-h-Math.round(m.height*.025);
const overlay=await sharp(src).resize(w,h).png().toBuffer();
const composite=await sharp(bg).composite([{input:overlay,left,top}]).png({compressionLevel:8}).toBuffer();
const filename=v.key+'-'+state+'-on-original-'+scene.key+'.png';
if(scene.key==='meadow' && state==='pop')fs.writeFileSync(path.join(folder,filename),composite);
const after=await sharp(composite).removeAlpha().raw().toBuffer(),alpha=await sharp(overlay).ensureAlpha().raw().toBuffer();
let compared=0,changed=0;
for(let py=0;py<m.height;py++)for(let px=0;px<m.width;px++){
const ox=px-left,oy=py-top,a=ox>=0&&oy>=0&&ox<w&&oy<h?alpha[(oy*w+ox)*4+3]:0;
if(a)continue;compared++;const at=(py*m.width+px)*3;
if(before[at]!==after[at]||before[at+1]!==after[at+1]||before[at+2]!==after[at+2])changed++;
}
if(changed)throw Error('World pixels changed: '+filename);
records.push({variant:v.key,state,scene:scene.key,width:m.width,height:m.height,changedOutsideUI:changed,comparedOutsideUI:compared,savedFullFrame:scene.key==='meadow'&&state==='pop'});
const closeup=await sharp(composite).extract({left:Math.round((m.width-cropW)/2),top:m.height-cropH,width:cropW,height:cropH}).resize(cellW,imageH).png().toBuffer();
board.push({input:closeup,left:pad,top:y+labelH+si*(imageH+gap)});
}
}
board.push({input:label('Original '+scene.key+' · HUD close-ups · populated / empty',cellW,titleH,22),left:pad,top:pad});
await sharp({create:{width:cellW+pad*2,height:pad+titleH+variants.length*(labelH+imageH*2+gap*2),channels:4,background:'#1b2425'}}).composite(board).png({compressionLevel:9}).toFile(path.join(folder,'comparison-'+scene.key+'.png'));
}
fs.writeFileSync(path.join(folder,'pixel-verification.json'),JSON.stringify(records,null,2));
console.log(JSON.stringify({comparisons:records.length,backgroundChanges:records.reduce((s,r)=>s+r.changedOutsideUI,0)}));
})().catch(e=>{console.error(e);process.exitCode=1;});
