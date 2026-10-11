const fs=require('fs');
const path=require('path');
const sharp=require('C:/Users/samee/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/sharp');
const folder=__dirname;
const background=path.join(folder,'../PeriodStudy/Backgrounds/source-01.png');
const screens=[
 {key:'pack',name:'Pack & equipment',regions:[[76,58,386,778],[386,71,1220,778],[1218,58,1601,778],[92,771,1590,908]]},
 {key:'storage',name:'Storage transfer',regions:[[58,60,795,742],[789,233,909,538],[899,60,1614,742],[86,742,1291,903],[1284,742,1590,903]]},
 {key:'crafting',name:'Crafting & requirements',regions:[[118,1,452,817],[449,15,1061,817],[1057,1,1560,817],[132,807,1547,924]]},
 {key:'hotbar',name:'Hotbar during play',regions:[[171,710,360,771],[168,755,1369,922],[1365,791,1635,886]]}
];
const esc=s=>s.replace(/&/g,'&amp;').replace(/</g,'&lt;');
(async()=>{
 const bm=await sharp(background).metadata();
 const before=await sharp(background).removeAlpha().raw().toBuffer();
 const proofs=[],previews=[];
 for(let i=0;i<screens.length;i++){
  const screen=screens[i],file=path.join(folder,screen.key+'-ui-layer.png');
  const {data,info}=await sharp(file).ensureAlpha().raw().toBuffer({resolveWithObject:true});
  // Remove stray extraction pixels outside the intended interface geometry.
  for(let y=0;y<info.height;y++)for(let x=0;x<info.width;x++){
   const at=(y*info.width+x)*4+3;
   if(data[at]<=8||!screen.regions.some(([a,b,c,d])=>x>=a&&x<=c&&y>=b&&y<=d))data[at]=0;
  }
  const clean=await sharp(data,{raw:info}).png().toBuffer();fs.writeFileSync(file,clean);
  const scale=Math.min(bm.width/info.width,bm.height/info.height)*(screen.key==='hotbar'?.6:1);
  const w=Math.round(info.width*scale),h=Math.round(info.height*scale);
  const left=Math.floor((bm.width-w)/2),top=screen.key==='hotbar'?bm.height-h:Math.floor((bm.height-h)/2);
  const overlay=await sharp(clean).resize(w,h).png().toBuffer();
  const mask=await sharp(overlay).ensureAlpha().raw().toBuffer();
  const output=screen.key+'-on-original-meadow.png';
  await sharp(background).composite([{input:overlay,left,top}]).png({compressionLevel:9}).toFile(path.join(folder,output));
  const after=await sharp(path.join(folder,output)).removeAlpha().raw().toBuffer();
  let compared=0,changed=0;
  for(let y=0;y<bm.height;y++)for(let x=0;x<bm.width;x++){
   const ox=x-left,oy=y-top;
   const a=ox>=0&&ox<w&&oy>=0&&oy<h?mask[(oy*w+ox)*4+3]:0;
   if(a!==0)continue;compared++;const at=(y*bm.width+x)*3;
   if(before[at]!==after[at]||before[at+1]!==after[at+1]||before[at+2]!==after[at+2])changed++;
  }
  if(changed!==0)throw Error(screen.key+': altered background pixels');
  const proof={screen:screen.key,file:output,width:bm.width,height:bm.height,comparedOutsideOverlay:compared,changedOutsideOverlay:changed};
  proofs.push(proof);console.log(JSON.stringify(proof));
  const label=Buffer.from(`<svg width="1000" height="36"><text x="0" y="24" fill="#f0ead8" font-family="Segoe UI,Arial" font-size="22">${esc(screen.name)}</text></svg>`);
  const thumb=await sharp(path.join(folder,output)).resize(1000,558,{fit:'contain',background:'#1b2328'}).png().toBuffer();
  const x=20+(i%2)*1020,y=20+Math.floor(i/2)*614;
  previews.push({input:label,left:x,top:y},{input:thumb,left:x,top:y+36});
 }
 fs.writeFileSync(path.join(folder,'pixel-verification.json'),JSON.stringify(proofs,null,2));
 await sharp({create:{width:2060,height:1248,channels:4,background:'#1b2328'}}).composite(previews).png().toFile(path.join(folder,'inventory-overview.png'));
})().catch(e=>{console.error(e);process.exitCode=1;});
