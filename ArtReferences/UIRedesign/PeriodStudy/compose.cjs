// Composite separately generated UI onto unchanged original screenshots.
// Run from this directory after layers.json is written by the generation step.
const fs = require('fs');
const path = require('path');
const sharp = require('C:/Users/samee/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/sharp');
const folder = __dirname;
const backgrounds = [
  {id:1,name:'Autumn meadow',file:path.join(folder,'Backgrounds/source-01.png')},
  {id:2,name:'Green forest',file:path.join(folder,'Backgrounds/source-02.png')},
  {id:3,name:'Summer lakeside',file:path.join(folder,'Backgrounds/source-03.png')},
  {id:4,name:'Autumn lakeside',file:path.join(folder,'Backgrounds/source-04.png')},
  {id:5,name:'Mountain meadow',file:path.join(folder,'Backgrounds/source-05.png')}
];
const xml = s=>s.replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&apos;'}[c]));
async function composite(layer,bg) {
  const layerPath = path.join(folder,layer.file);
  const bm = await sharp(bg.file).metadata(), lm = await sharp(layerPath).metadata();
  const scale = Math.min(bm.width/lm.width,bm.height/lm.height);
  const w=Math.round(lm.width*scale),h=Math.round(lm.height*scale);
  const left=Math.floor((bm.width-w)/2),top=Math.floor((bm.height-h)/2);
  const overlay = await sharp(layerPath).resize(w,h).ensureAlpha().png().toBuffer();
  const alpha = await sharp(overlay).raw().toBuffer();
  const output = `${layer.key}-background-${String(bg.id).padStart(2,'0')}.png`;
  const outPath=path.join(folder,output);
  // The base image is never resized, cropped, recolored, darkened or regenerated.
  await sharp(bg.file).composite([{input:overlay,left,top}]).png({compressionLevel:9}).toFile(outPath);
  const before=await sharp(bg.file).removeAlpha().raw().toBuffer();
  const after=await sharp(outPath).removeAlpha().raw().toBuffer();
  let compared=0, changed=0;
  for(let y=0;y<bm.height;y++)for(let x=0;x<bm.width;x++){
    const ox=x-left,oy=y-top;
    const a=ox>=0&&oy>=0&&ox<w&&oy<h?alpha[(oy*w+ox)*4+3]:0;
    if(a!==0)continue;
    compared++;
    const at=(y*bm.width+x)*3;
    if(before[at]!==after[at]||before[at+1]!==after[at+1]||before[at+2]!==after[at+2])changed++;
  }
  const corners=[0,(w-1)*4,((h-1)*w)*4,(h*w-1)*4].map(i=>alpha[i+3]);
  if(compared<bm.width*bm.height*.1||changed!==0||corners.some(a=>a!==0))throw Error(`${output}: alpha/background check failed (${compared} tested, ${changed} changed, corners ${corners})`);
  const result={style:layer.key,background:bg.id,name:bg.name,file:output,width:bm.width,height:bm.height,unchangedOutsideOverlay:compared,changedOutsideOverlay:changed};
  console.log(JSON.stringify(result));
  return result;
}
async function board(results,filename,columns=2) {
  const cellW=1000,cellH=558,labelH=34,pad=20;
  const rows=Math.ceil(results.length/columns);
  const width=columns*cellW+(columns+1)*pad,height=rows*(cellH+labelH)+(rows+1)*pad;
  const layers=[];
  for(let i=0;i<results.length;i++){
    const row=Math.floor(i/columns),col=i%columns;
    const lastSingle=results.length%columns===1&&i===results.length-1;
    const left=lastSingle?Math.floor((width-cellW)/2):pad+col*(cellW+pad);
    const top=pad+row*(cellH+labelH+pad);
    const title=results[i].title||`${results[i].background} · ${results[i].name}`;
    const label=Buffer.from(`<svg width="${cellW}" height="${labelH}"><text x="0" y="23" fill="#eee8d8" font-family="Segoe UI,Arial" font-size="21">${xml(title)}</text></svg>`);
    const thumb=await sharp(path.join(folder,results[i].file)).resize(cellW,cellH,{fit:'contain',background:'#1b2328'}).png().toBuffer();
    layers.push({input:label,left,top},{input:thumb,left,top:top+labelH});
  }
  await sharp({create:{width,height,channels:4,background:'#1b2328'}}).composite(layers).png({compressionLevel:9}).toFile(path.join(folder,filename));
}
async function cleanLayer(style) {
  const file=path.join(folder,style.file);
  const {data,info}=await sharp(file).ensureAlpha().raw().toBuffer({resolveWithObject:true});
  // Remove extraction flecks beyond the actual UI regions, including
  // near-transparent canvas noise. Keep all panel silhouettes and content.
  const regionSets={
    quiet:[[70,68,341,776],[339,68,972,776],[969,68,1378,776],[191,777,1334,892],[1327,828,1652,893]],
    papyrus:[[66,58,340,783],[337,58,949,783],[943,58,1369,783],[225,773,1365,899],[1356,822,1646,901]],
    'wax-tablets':[[65,53,361,793],[356,53,976,793],[972,53,1393,793],[215,783,1340,913],[1334,828,1671,915]],
    'hide-bronze':[[63,61,342,785],[337,61,951,785],[945,61,1368,785],[263,780,1361,901],[1355,830,1671,902]],
    'stone-pillars':[[73,62,375,806],[370,77,958,806],[950,62,1388,806],[267,799,1331,919],[1327,835,1654,917]]
  };
  const regions=regionSets[style.key];
  for(let y=0;y<info.height;y++)for(let x=0;x<info.width;x++){
    const at=(y*info.width+x)*4+3;
    if(data[at]<=8||!regions.some(([x0,y0,x1,y1])=>x>=x0&&x<=x1&&y>=y0&&y<=y1))data[at]=0;
  }
  const buffer=await sharp(data,{raw:info}).png().toBuffer();
  fs.writeFileSync(file,buffer);
}
(async()=>{
  const styles=JSON.parse(fs.readFileSync(path.join(folder,'layers.json'),'utf8'));
  for(const style of styles)await cleanLayer(style);
  const results=[];
  for(const bg of backgrounds)results.push(await composite(styles[0],bg));
  for(const style of styles.slice(1))results.push(await composite(style,backgrounds[0]));
  fs.writeFileSync(path.join(folder,'pixel-verification.json'),JSON.stringify(results,null,2));
  await board(results.slice(0,5),'quiet-five-backgrounds.png');
  await board(results.slice(5).map(r=>({...r,title:styles.find(s=>s.key===r.style).name})),'period-directions.png');
})().catch(e=>{console.error(e);process.exitCode=1;});
