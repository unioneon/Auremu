// Render the repository's 2:1 social card. Pass a sharp package path if needed.
const fs = require('fs');
const path = require('path');
const sharp = require(process.argv[2] || 'sharp');
async function main() {
  const logo = fs.readFileSync(path.join(__dirname, '../assets/brand/logo-light.svg'), 'utf8')
    .replace(/<svg[^>]*>/, '<svg x="52" y="34" width="184" height="64" viewBox="0 0 127 44">');
  const tablet = fs.readFileSync(path.join(__dirname, 'android-tablet.jpg')).toString('base64');
  const phone = fs.readFileSync(path.join(__dirname, 'android-phone.jpg')).toString('base64');
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="1280" height="640" viewBox="0 0 1280 640">
  <defs>
    <linearGradient id="bg" x2="1" y2="1"><stop stop-color="#FAF9FF"/><stop offset="1" stop-color="#E5DEFF"/></linearGradient>
    <radialGradient id="mint"><stop stop-color="#5AD8C8" stop-opacity=".35"/><stop offset="1" stop-color="#5AD8C8" stop-opacity="0"/></radialGradient>
    <filter id="shadow" x="-25%" y="-25%" width="150%" height="160%"><feDropShadow dx="0" dy="14" stdDeviation="18" flood-color="#30206C" flood-opacity=".16"/></filter>
    <clipPath id="tablet"><rect x="475" y="170" width="754" height="450" rx="24"/></clipPath>
    <clipPath id="phone"><rect x="1021" y="224" width="204" height="352" rx="21"/></clipPath>
  </defs>
  <rect width="1280" height="640" fill="url(#bg)"/>
  <ellipse cx="1100" cy="625" rx="430" ry="370" fill="url(#mint)"/>
  <circle cx="1240" cy="-65" r="215" fill="#7851FF" opacity=".045"/>
  ${logo}
  <g font-family="Segoe UI,Arial,sans-serif">
    <rect x="1051" y="49" width="177" height="30" rx="15" fill="#FFFFFF" fill-opacity=".75"/>
    <text x="1139" y="69" text-anchor="middle" fill="#827B97" font-size="11" letter-spacing="1.6">WINDOWS · ANDROID 17</text>
    <text x="54" y="151" fill="#7851FF" font-size="12" font-weight="600" letter-spacing="2">ANDROID ON YOUR DESKTOP</text>
    <text x="50" y="231" fill="#211D39" font-size="56" font-weight="600" letter-spacing="-1.7">Your Android.</text>
    <text x="50" y="301" fill="#7851FF" font-size="56" font-weight="600" letter-spacing="-1.7">Your desktop.</text>
    <text x="54" y="358" fill="#7C748F" font-size="19">Official SDK. Standard Android.</text>
    <text x="54" y="390" fill="#7C748F" font-size="19">Phone and tablet. Your space.</text>
    <rect x="52" y="444" width="288" height="46" rx="23" fill="#FFFFFF" fill-opacity=".85"/>
    <circle cx="76" cy="467" r="5" fill="#5AD8C8"/>
    <text x="94" y="473" fill="#5D546F" font-size="15">Made for your Windows desktop</text>
    <text x="54" y="600" fill="#948BA6" font-size="13" letter-spacing=".3">github.com/unioneon/Auremu</text>
  </g>
  <g filter="url(#shadow)"><rect x="467" y="162" width="770" height="466" rx="30" fill="#FAF9FF"/>
    <image x="475" y="170" width="754" height="450" href="data:image/jpeg;base64,${tablet}" clip-path="url(#tablet)"/>
  </g>
  <g filter="url(#shadow)"><rect x="1014" y="217" width="218" height="366" rx="27" fill="#F8F5FF"/>
    <image x="1021" y="224" width="204" height="352" href="data:image/jpeg;base64,${phone}" clip-path="url(#phone)"/>
  </g>
  </svg>`;
  fs.writeFileSync(path.join(__dirname, 'social-preview.svg'), svg);
  await sharp(Buffer.from(svg)).png({compressionLevel:9}).toFile(path.join(__dirname, 'social-preview.png'));
  const info = await sharp(path.join(__dirname, 'social-preview.png')).metadata();
  const size = fs.statSync(path.join(__dirname, 'social-preview.png')).size;
  if (info.width !== 1280 || info.height !== 640 || size >= 1000000) throw new Error('Invalid social card dimensions or file size');
  console.log(JSON.stringify({width:info.width,height:info.height,bytes:size}));
}
main().catch(e=>{console.error(e);process.exit(1);});
