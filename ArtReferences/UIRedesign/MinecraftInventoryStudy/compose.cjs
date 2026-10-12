const fs=require('fs');
const path=require('path');
const sharp=require('C:/Users/samee/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/sharp');
const folder=__dirname;
const bg=path.join(folder,'../PeriodStudy/Backgrounds/source-01.png');
const variants=[
 {key:'inventory-compact',name:'Compact inventory',kind:'inventory',regions:[[358,28,1318,891],[281,833,475,940]]},
 {key:'inventory-character',name:'Character-led inventory',kind:'inventory',regions:[[163,62,1510,902]]},
 {key:'inventory-split',name:'Split-panel inventory',kind:'inventory',regions:[[330,8,909,483],[896,73,1341,483],[330,469,1342,936]]},
 {key:'hotbar-carved',name:'Carved stone rail',kind:'hotbar',regions:[[207,535,1780,742]],crop:{left:207,top:535,width:1574,height:208}},
 {key:'hotbar-tiles',name:'Separated stone tiles',kind:'hotbar',regions:[[386,650,520,782],[553,650,1597,783]],crop:{left:386,top:650,width:1212,height:134}},
 {key:'hotbar-slate',name:'Slate inset',kind:'hotbar',regions:[[145,551,1841,756]],crop:{left:145,top:551,width:1697,height:206}}
];
const esc=s=>s.replace(/&/g,'&amp;').replace(/</g,'&lt;');
(async()=>{
 const bm=await sharp(bg).metadata();const before=await sharp(bg).removeAlpha().raw().toBuffer();
 const records=[];
 for(const v of variants){
  const filename=path.join(folder,v.key+'-ui-layer.png');
  const {data,info}=await sharp(filename).ensureAlpha().raw().toBuffer({resolveWithObject:true});
  for(let y=0;y<info.height;y++)for(let x=0;x<info.width;x++){
   const at=(y*info.width+x)*4+3;
   if(data[at]<=8||!v.regions.some(([a,b,c,d])=>x>=a&&x<=c&&y>=b&&y<=d))data[at]=0;
  }
  let clean=await sharp(data,{raw:info}).png().toBuffer();fs.writeFileSync(filename,clean);
  let w,h,left,top;
  if(v.kind==='hotbar'){
   clean=await sharp(clean).extract(v.crop).png().toBuffer();
   fs.writeFileSync(path.join(folder,v.key+'-isolated.png'),clean);
   w=Math.round(bm.width*.44);h=Math.round(v.crop.height*w/v.crop.width);
   left=Math.floor((bm.width-w)/2);top=bm.height-h-Math.round(bm.height*.02);
  }else{
   const scale=Math.min(bm.width/info.width,bm.height/info.height);
   w=Math.round(info.width*scale);h=Math.round(info.height*scale);
   left=Math.floor((bm.width-w)/2);top=Math.floor((bm.height-h)/2);
  }
  const overlay=await sharp(clean).resize(w,h).png().toBuffer();
  const alpha=await sharp(overlay).ensureAlpha().raw().toBuffer();
  const output=v.key+'-on-original-meadow.png';
  await sharp(bg).composite([{input:overlay,left,top}]).png({compressionLevel:9}).toFile(path.join(folder,output));
  const after=await sharp(path.join(folder,output)).removeAlpha().raw().toBuffer();
  let compared=0,changed=0;
  for(let y=0;y<bm.height;y++)for(let x=0;x<bm.width;x++){
   const ox=x-left,oy=y-top;
   const a=ox>=0&&ox<w&&oy>=0&&oy<h?alpha[(oy*w+ox)*4+3]:0;
   if(a!==0)continue;compared++;const at=(y*bm.width+x)*3;
   if(before[at]!==after[at]||before[at+1]!==after[at+1]||before[at+2]!==after[at+2])changed++;
  }
  if(changed!==0)throw Error(v.key+': background pixels changed');
  const record={key:v.key,name:v.name,file:output,width:bm.width,height:bm.height,comparedOutsideOverlay:compared,changedOutsideOverlay:changed};
  records.push(record);console.log(JSON.stringify(record));
 }
 fs.writeFileSync(path.join(folder,'pixel-verification.json'),JSON.stringify(records,null,2));
 for(const kind of ['inventory','hotbar']){
  const selected=records.filter(r=>r.key.startsWith(kind));
  const cellW=kind==='inventory'?1000:1200,cellH=kind==='inventory'?558:192,labelH=36,pad=20;
  const layers=[];
  for(let i=0;i<selected.length;i++){
   const v=selected[i],top=pad+i*(cellH+labelH+pad);
   const label=Buffer.from(`<svg width="${cellW}" height="${labelH}"><text x="0" y="24" fill="#efe8d5" font-family="Segoe UI,Arial" font-size="23">${esc(v.name)}</text></svg>`);
   let source=sharp(path.join(folder,v.file));
   // Detail board is explicitly a close-up; full-size composites remain uncropped.
   if(kind==='hotbar')source=source.extract({left:Math.floor((bm.width-2000)/2),top:bm.height-320,width:2000,height:320});
   const image=await source.resize(cellW,cellH,{fit:'contain',background:'#1b2328'}).png().toBuffer();
   layers.push({input:label,left:pad,top},{input:image,left:pad,top:top+labelH});
  }
  const width=cellW+pad*2,height=selected.length*(cellH+labelH+pad)+pad;
  await sharp({create:{width,height,channels:4,background:'#1b2328'}}).composite(layers).png().toFile(path.join(folder,kind+'-comparisons.png'));
 }
})().catch(e=>{console.error(e);process.exitCode=1;});
