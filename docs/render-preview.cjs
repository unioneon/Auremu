// Pass the absolute directory of your installed sharp package as the first argument.
const fs = require('fs');
const path = require('path');
const sharp = require(process.argv[2] || 'sharp');
const root = path.resolve(__dirname, '..');
async function main() {
  const logo = fs.readFileSync(path.join(root, 'assets/brand/logo-dark.svg'), 'utf8').replace(/<svg[^>]*>/, '<svg x="64" y="58" width="270" height="94" viewBox="0 0 127 44">');
  const image = fs.readFileSync(path.join(__dirname, 'manager.png')).toString('base64');
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="1600" height="1000" viewBox="0 0 1600 1000">
  <defs><linearGradient id="bg" x2="1" y2="1"><stop stop-color="#111321"/><stop offset="1" stop-color="#24213F"/></linearGradient><radialGradient id="glow"><stop stop-color="#7851FF" stop-opacity=".35"/><stop offset="1" stop-color="#7851FF" stop-opacity="0"/></radialGradient><clipPath id="screen"><rect x="505" y="223" width="1024" height="698" rx="30"/></clipPath></defs>
  <rect width="1600" height="1000" fill="url(#bg)"/><ellipse cx="1170" cy="560" rx="690" ry="580" fill="url(#glow)"/>
  <circle cx="1500" cy="-24" r="182" fill="#5AD8C8" opacity=".08"/>${logo}
  <g font-family="Segoe UI,Arial,sans-serif"><rect x="1260" y="68" width="270" height="42" rx="21" fill="#FFFFFF" fill-opacity=".07"/><text x="1395" y="95" text-anchor="middle" fill="#BDB7D9" font-size="17" letter-spacing="2">WINDOWS · PREVIEW 0.1</text>
  <text x="65" y="278" fill="#66DCCD" font-size="16" letter-spacing="3">ANDROID НА ВАШЕМ ПК</text>
  <g fill="#F6F4FF" font-size="57" font-weight="600"><text x="60" y="365">Ваш Android.</text><text x="60" y="438">Ваш рабочий</text><text x="60" y="511">стол.</text></g>
  <g fill="#B5AFCB" font-size="23"><text x="65" y="579">Официальный Android SDK.</text><text x="65" y="617">Телефон и планшет.</text><text x="65" y="655">Светлая и тёмная темы.</text></g>
  <rect x="64" y="731" width="324" height="62" rx="31" fill="#7851FF"/><text x="226" y="771" fill="white" font-size="23" text-anchor="middle">Установить. Запустить.</text>
  <text x="65" y="848" fill="#817B9B" font-size="18">Чистое устройство при первом запуске.</text>
  <text x="65" y="876" fill="#817B9B" font-size="18">Без чужих аккаунтов и сессий.</text>
  <rect x="492" y="210" width="1050" height="724" rx="42" fill="#0B0D19" fill-opacity=".35" stroke="#6C638D" stroke-opacity=".5"/>
  <image x="505" y="223" width="1024" height="698" href="data:image/png;base64,${image}" clip-path="url(#screen)"/>
  <text x="65" y="961" fill="#696780" font-size="16" letter-spacing="1">AUREMU / INDEPENDENT ANDROID EMULATOR INTERFACE</text>
  <text x="1530" y="961" fill="#918AA8" font-size="17" text-anchor="end">github.com/unioneon/Auremu</text></g></svg>`;
  fs.writeFileSync(path.join(__dirname, 'preview.svg'), svg);
  await sharp(Buffer.from(svg)).png().toFile(path.join(__dirname, 'preview.png'));
  const iconSvg='<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256"><rect width="256" height="256" rx="56" fill="#1B1935"/><path d="M64 180L114 56h30l48 124h-32l-12-34h-45l-12 34zm48-61h27l-13-39z" fill="white"/><circle cx="64" cy="184" r="39" fill="#5AD8C8"/><circle cx="190" cy="174" r="13" fill="#5AD8C8"/></svg>';
  const sizes=[16,32,48,64,128,256];const buffers=[];
  for(const n of sizes)buffers.push(await sharp(Buffer.from(iconSvg)).resize(n,n).png().toBuffer());
  const header=Buffer.alloc(6+16*sizes.length);header.writeUInt16LE(1,2);header.writeUInt16LE(sizes.length,4);let offset=header.length;
  buffers.forEach((b,i)=>{const at=6+16*i;header[at]=sizes[i]===256?0:sizes[i];header[at+1]=header[at];header.writeUInt16LE(1,at+4);header.writeUInt16LE(32,at+6);header.writeUInt32LE(b.length,at+8);header.writeUInt32LE(offset,at+12);offset+=b.length;});
  fs.writeFileSync(path.join(root,'assets/brand/auremu.ico'),Buffer.concat([header,...buffers]));
}
main().catch(e=>{console.error(e);process.exit(1);});
